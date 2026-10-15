using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

public enum AmountTone
{
    Neutral,
    Income,
    Expense,
    Envelope,
}

/// <summary>One line of the operations list, worded from the point of view of the viewed account.</summary>
public sealed record OperationRow(Transaction Transaction, string Title, string Subtitle, string AmountText, string? SecondaryAmount, AmountTone Tone)
{
    public Guid Id => Transaction.Id;

    public static OperationRow Create(Transaction t, Guid? viewed, IReadOnlyDictionary<Guid, Account> accounts, IReadOnlyDictionary<Guid, Category> categories, CurrencyCode baseCurrency)
    {
        ArgumentNullException.ThrowIfNull(t);
        string NameOf(Guid? id) => id is { } x && accounts.TryGetValue(x, out var a) ? a.Name : "?";
        var source = accounts.GetValueOrDefault(t.AccountId);
        var note = t.Note ?? string.Empty;

        if (t.IsTransfer)
        {
            var destination = accounts.GetValueOrDefault(t.ToAccountId!.Value);
            var tone = destination?.IsEnvelope == true || source?.IsEnvelope == true ? AmountTone.Envelope : AmountTone.Neutral;
            if (viewed == t.AccountId)
            {
                return new OperationRow(t, (destination?.IsEnvelope == true ? "В конверт «" : "Перевод → «") + NameOf(t.ToAccountId) + "»",
                    note, Format.Signed(-t.Amount, t.Currency), null, tone);
            }

            if (viewed == t.ToAccountId)
            {
                return new OperationRow(t, (source?.IsEnvelope == true ? "Из конверта «" : "Перевод ← «") + NameOf(t.AccountId) + "»",
                    note, Format.Signed(t.ToAmount!.Value, destination!.Currency), null, tone);
            }

            var received = t.Currency == destination?.Currency ? null : Format.Money(t.ToAmount!.Value, destination?.Currency ?? t.Currency);
            return new OperationRow(t, $"{NameOf(t.AccountId)} → {NameOf(t.ToAccountId)}", note, Format.Money(t.Amount, t.Currency), received, tone);
        }

        var category = t.CategoryId is { } c && categories.TryGetValue(c, out var found) ? found.Name : "?";
        var subtitle = viewed is null ? string.Join(" · ", new[] { note, NameOf(t.AccountId) }.Where(s => s.Length > 0)) : note;
        var signed = t.Type == TransactionType.Income ? t.Amount : -t.Amount;

        // In «all operations» amounts are in the base currency, so the day totals add up; on an account page — in its own.
        var (amount, secondary) = viewed is null && t.Currency != baseCurrency
            ? (Format.Signed(t.Type == TransactionType.Income ? t.BaseAmount : -t.BaseAmount, baseCurrency), Format.Money(t.Amount, t.Currency))
            : (Format.Signed(signed, t.Currency), (string?)null);
        return new OperationRow(t, category, subtitle, amount, secondary, t.Type == TransactionType.Income ? AmountTone.Income : AmountTone.Expense);
    }
}

/// <summary>«Сегодня · −1 550 ₽» and its operations.</summary>
public sealed record DayGroup(DateOnly Day, string Title, string TotalText, IReadOnlyList<OperationRow> Rows);

/// <summary>
/// Operations of one account (or all of them) for a month, grouped by day like in a banking app.
/// Deleting needs no confirmation: it can be undone (Ctrl+Z), several steps back.
/// </summary>
public sealed partial class OperationsViewModel(IFinanceBackend backend, TimeProvider timeProvider) : ObservableObject
{
    private const int UndoDepth = 20;

    private readonly List<(Transaction Deleted, string Description)> _undo = [];
    private PeriodOverview? _overview;

    public static IReadOnlyList<FilterChoice> TypeChoices { get; } =
    [
        new("Все", null),
        new("Расходы", TransactionType.Expense),
        new("Доходы", TransactionType.Income),
        new("Переводы", TransactionType.Transfer),
    ];

    /// <summary>The account or envelope shown; <c>null</c> — all operations.</summary>
    [ObservableProperty]
    public partial Account? Account { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthTitle))]
    public partial YearMonth Month { get; private set; } = YearMonth.Of(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

    public string MonthTitle => Format.Period(Period.Of(Month));

    public string Title => Account?.Name ?? "Все операции";

    /// <summary>«За октябрь: +50 000 / −17 100 ₽».</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial FilterChoice TypeFilter { get; set; } = TypeChoices[0];

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    public ObservableCollection<DayGroup> Days { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial string UndoText { get; private set; } = string.Empty;

    public bool CanUndo => _undo.Count > 0;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial OperationRow? Selected { get; set; }

    /// <summary>Raised to open the edit form for a row.</summary>
    public event EventHandler<Transaction>? EditRequested;

    /// <summary>Raised after a change that affects balances (delete, undo).</summary>
    public event EventHandler? Changed;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public async Task ShowAsync(Account? account, CancellationToken cancellationToken = default)
    {
        Account = account;
        OnPropertyChanged(nameof(Title));
        await LoadAsync(cancellationToken);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _overview = await backend.LoadPeriodAsync(Period.Of(Month), cancellationToken);
            Status = string.Empty;
            Rebuild();
        }
#pragma warning disable CA1031 // Shown in place; the window stays usable.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось загрузить операции: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task PreviousMonthAsync() => GoToAsync(Month.Previous);

    [RelayCommand]
    private Task NextMonthAsync() => GoToAsync(Month.Next);

    [RelayCommand]
    private Task CurrentMonthAsync() => GoToAsync(YearMonth.Of(Today));

    [RelayCommand]
    private void Edit(OperationRow? row)
    {
        if ((row ?? Selected) is { } target)
        {
            EditRequested?.Invoke(this, target.Transaction);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(OperationRow? row)
    {
        if ((row ?? Selected) is not { } target)
        {
            return;
        }

        try
        {
            var deleted = await backend.DeleteAsync(target.Id);
            _undo.Add((deleted, $"Удалено: {target.Title} {target.AmountText}"));
            if (_undo.Count > UndoDepth)
            {
                _undo.RemoveAt(0);
            }

            UndoText = _undo[^1].Description;
            await LoadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        var (deleted, _) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        UndoText = _undo.Count > 0 ? _undo[^1].Description : string.Empty;
        try
        {
            await backend.RestoreAsync(deleted);
            await LoadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void DismissUndo()
    {
        _undo.Clear();
        UndoText = string.Empty;
    }

    partial void OnTypeFilterChanged(FilterChoice value) => Rebuild();

    partial void OnSearchChanged(string value) => Rebuild();

    private async Task GoToAsync(YearMonth month)
    {
        Month = month;
        await LoadAsync();
    }

    private void Rebuild()
    {
        if (_overview is null)
        {
            return;
        }

        var accounts = _overview.Accounts.ToDictionary(a => a.Id);
        var categories = _overview.Categories.ToDictionary(c => c.Id);
        var baseCurrency = _overview.BaseCurrency;
        var viewed = Account?.Id;
        var search = Search.Trim();

        var ofAccount = _overview.Transactions
            .Where(t => viewed is null || t.AccountId == viewed || t.ToAccountId == viewed)
            .ToList();

        Subtitle = BuildSubtitle(ofAccount, viewed, baseCurrency);

        var rows = ofAccount
            .Where(t => TypeFilter.Type is null || t.Type == TypeFilter.Type)
            .Select(t => OperationRow.Create(t, viewed, accounts, categories, baseCurrency))
            .Where(r => search.Length == 0
                || r.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || r.Subtitle.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        Days.Clear();
        foreach (var day in rows.GroupBy(r => r.Transaction.Date).OrderByDescending(g => g.Key))
        {
            var total = viewed is { } id
                ? Format.Signed(day.Sum(r => r.Transaction.EffectOn(id)), Account!.Currency)
                : Format.Signed(day.Sum(r => r.Transaction.Type switch
                {
                    TransactionType.Income => r.Transaction.BaseAmount,
                    TransactionType.Expense => -r.Transaction.BaseAmount,
                    _ => 0m,
                }), baseCurrency);
            Days.Add(new DayGroup(day.Key, Format.Day(day.Key, Today), total, day.ToList()));
        }

        IsEmpty = Days.Count == 0;
    }

    private string BuildSubtitle(IReadOnlyList<Transaction> transactions, Guid? viewed, CurrencyCode baseCurrency)
    {
        if (viewed is { } id)
        {
            var incoming = transactions.Sum(t => Math.Max(t.EffectOn(id), 0));
            var outgoing = transactions.Sum(t => Math.Min(t.EffectOn(id), 0));
            return $"За месяц: {Format.Signed(incoming, Account!.Currency)} / {Format.Signed(outgoing, Account.Currency)}";
        }

        var income = transactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.BaseAmount);
        var expense = transactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.BaseAmount);
        return $"За месяц: {Format.Signed(income, baseCurrency)} / {Format.Signed(-expense, baseCurrency)}";
    }
}

/// <summary>An option of a filter dropdown; <see cref="Type"/> is <c>null</c> for «all».</summary>
public sealed record FilterChoice(string Title, TransactionType? Type)
{
    public override string ToString() => Title;
}
