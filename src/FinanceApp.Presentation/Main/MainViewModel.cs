using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Presentation.Main;

/// <summary>
/// The main window: a month at a time — totals against the previous month, expenses by category,
/// the transaction list with filters, editing, and deletion with undo (no confirmation dialogs).
/// </summary>
public sealed partial class MainViewModel(IMainBackend backend, TimeProvider timeProvider) : ObservableObject
{
    private const int UndoDepth = 20;

    private static readonly FilterOption<TransactionType?> AllTypes = new("Все типы", null);

    private readonly Stack<(Transaction Deleted, string Description)> _undo = new();
    private PeriodOverview? _overview;
    private List<TransactionRow> _allRows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial YearMonth Month { get; set; } = YearMonth.Of(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

    public string Title => MonthTitle.Of(Month);

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>An error or a short notice; empty when there is none.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    public ObservableCollection<TotalCard> Cards { get; } = [];

    public ObservableCollection<CategoryBar> CategoryBars { get; } = [];

    public ObservableCollection<TransactionRow> Rows { get; } = [];

    public ObservableCollection<FilterOption<TransactionType?>> TypeOptions { get; } =
    [
        AllTypes,
        new("Расходы", TransactionType.Expense),
        new("Доходы", TransactionType.Income),
        new("Накопления", TransactionType.Saving),
    ];

    public ObservableCollection<FilterOption<Guid?>> CategoryOptions { get; } = [];

    public ObservableCollection<FilterOption<Guid?>> AccountOptions { get; } = [];

    [ObservableProperty]
    public partial FilterOption<TransactionType?> TypeFilter { get; set; } = AllTypes;

    [ObservableProperty]
    public partial FilterOption<Guid?>? CategoryFilter { get; set; }

    [ObservableProperty]
    public partial FilterOption<Guid?>? AccountFilter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>«Показано 12 из 40» or «40 операций».</summary>
    [ObservableProperty]
    public partial string RowsSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial TransactionRow? SelectedRow { get; set; }

    /// <summary>The open edit panel, or <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    public partial TransactionEditorViewModel? Editor { get; set; }

    public bool IsEditing => Editor is not null;

    /// <summary>«Удалено: −350 ₽ · Еда» while undo is available.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial string UndoText { get; set; } = string.Empty;

    public bool CanUndo => _undo.Count > 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    [RelayCommand]
    private Task PreviousMonthAsync() => GoToAsync(Month.Previous);

    [RelayCommand]
    private Task NextMonthAsync() => GoToAsync(Month.Next);

    [RelayCommand]
    private Task CurrentMonthAsync() => GoToAsync(YearMonth.Of(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)));

    [RelayCommand]
    private void ResetFilters()
    {
        TypeFilter = AllTypes;
        CategoryFilter = CategoryOptions.FirstOrDefault();
        AccountFilter = AccountOptions.FirstOrDefault();
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void Edit(TransactionRow? row)
    {
        row ??= SelectedRow;
        if (row is null || _overview is null)
        {
            return;
        }

        Editor = new TransactionEditorViewModel(row.Transaction, _overview.Categories, _overview.Accounts);
        Editor.PropertyChanged += (_, _) => SaveEditCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CancelEdit() => Editor = null;

    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private async Task SaveEditAsync(CancellationToken cancellationToken)
    {
        var editor = Editor!;
        await RunAsync(async ct =>
        {
            await backend.UpdateAsync(editor.Transaction.Id, editor.ToInput(), ct);
            Editor = null;
            await RefreshAsync(ct);
        }, cancellationToken);
    }

    [RelayCommand]
    private async Task DeleteAsync(TransactionRow? row)
    {
        row ??= SelectedRow;
        if (row is null)
        {
            return;
        }

        await RunAsync(async ct =>
        {
            var deleted = await backend.DeleteAsync(row.Id, ct);
            PushUndo(deleted, $"Удалено: {row.AmountText} · {row.CategoryName}");
            if (Editor?.Transaction.Id == row.Id)
            {
                Editor = null;
            }

            await RefreshAsync(ct);
        }, CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync(CancellationToken cancellationToken)
    {
        var (deleted, _) = _undo.Pop();
        UndoText = _undo.TryPeek(out var next) ? next.Description : string.Empty;
        await RunAsync(async ct =>
        {
            await backend.RestoreAsync(deleted, ct);
            await RefreshAsync(ct);
        }, cancellationToken);
    }

    [RelayCommand]
    private void DismissUndo()
    {
        _undo.Clear();
        UndoText = string.Empty;
    }

    partial void OnTypeFilterChanged(FilterOption<TransactionType?> value) => ApplyFilters();

    partial void OnCategoryFilterChanged(FilterOption<Guid?>? value) => ApplyFilters();

    partial void OnAccountFilterChanged(FilterOption<Guid?>? value) => ApplyFilters();

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    private bool CanSaveEdit() => Editor?.CanSave == true;

    private async Task GoToAsync(YearMonth month)
    {
        Month = month;
        Editor = null;
        await RefreshAsync(CancellationToken.None);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var overview = await backend.LoadMonthAsync(Month, cancellationToken);
            Apply(overview);
            Status = string.Empty;
        }
#pragma warning disable CA1031 // The window stays open; the error is shown in the status line.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось загрузить данные: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
#pragma warning disable CA1031 // Domain errors, offline rates, locked database: show and keep going.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = ex.Message;
        }
    }

    private void Apply(PeriodOverview overview)
    {
        _overview = overview;
        var currency = overview.BaseCurrency;
        var summary = overview.Summary;
        var previous = summary.Previous ?? PeriodTotals.Empty;

        Replace(Cards,
        [
            TotalCard.Create("Доходы", summary.Current.Income, previous.Income, moreIsGood: true, currency),
            TotalCard.Create("Расходы", summary.Current.Expense, previous.Expense, moreIsGood: false, currency),
            TotalCard.Create("В конверты", summary.Current.IntoEnvelopes, previous.IntoEnvelopes, moreIsGood: true, currency),
            TotalCard.Create("Остаток", summary.Current.Balance, previous.Balance, moreIsGood: true, currency),
        ]);
        Replace(CategoryBars, CategoryBar.Create(summary, currency));

        var categoryNames = overview.Categories.ToDictionary(c => c.Id, c => c.Name);
        var accountNames = overview.Accounts.ToDictionary(a => a.Id, a => a.Name);
        _allRows = overview.Transactions.Select(t => TransactionRow.Create(t, categoryNames, accountNames, currency)).ToList();

        // Keep the chosen filters across refreshes when they still make sense.
        var categoryId = CategoryFilter?.Value;
        var accountId = AccountFilter?.Value;
        Replace(CategoryOptions, overview.Categories
            .Where(c => !c.IsHidden || _allRows.Any(r => r.Transaction.CategoryId == c.Id))
            .Select(c => new FilterOption<Guid?>($"{c.Name} · {TypeShort(c.Type)}", c.Id))
            .Prepend(new FilterOption<Guid?>("Все категории", null)));
        Replace(AccountOptions, overview.Accounts
            .Where(a => !a.IsHidden || _allRows.Any(r => r.Transaction.AccountId == a.Id))
            .Select(a => new FilterOption<Guid?>(a.Name, a.Id))
            .Prepend(new FilterOption<Guid?>("Все счета", null)));
        CategoryFilter = CategoryOptions.FirstOrDefault(o => o.Value == categoryId) ?? CategoryOptions[0];
        AccountFilter = AccountOptions.FirstOrDefault(o => o.Value == accountId) ?? AccountOptions[0];

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var search = SearchText.Trim();
        var filtered = _allRows.Where(r =>
                (TypeFilter.Value is null || r.Type == TypeFilter.Value)
                && (CategoryFilter?.Value is null || r.Transaction.CategoryId == CategoryFilter.Value)
                && (AccountFilter?.Value is null || r.Transaction.AccountId == AccountFilter.Value)
                && (search.Length == 0
                    || (r.Note?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
                    || r.CategoryName.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        Replace(Rows, filtered);
        IsEmpty = filtered.Count == 0;
        RowsSummary = filtered.Count == _allRows.Count
            ? $"Операций: {_allRows.Count}"
            : $"Показано {filtered.Count} из {_allRows.Count}";
    }

    private void PushUndo(Transaction deleted, string description)
    {
        _undo.Push((deleted, description));
        if (_undo.Count > UndoDepth)
        {
            var keep = _undo.Take(UndoDepth).Reverse().ToList();
            _undo.Clear();
            keep.ForEach(_undo.Push);
        }

        UndoText = description;
    }

    private static string TypeShort(TransactionType type) =>
        type switch
        {
            TransactionType.Income => "доход",
            TransactionType.Expense => "расход",
            _ => "накопл.",
        };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
