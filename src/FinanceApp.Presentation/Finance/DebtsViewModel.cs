using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Debts;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>«Мне должны» or «Я должен».</summary>
public sealed record DebtDirectionChoice(DebtDirection Direction, string Title)
{
    public static IReadOnlyList<DebtDirectionChoice> All { get; } =
    [
        new(DebtDirection.Lent, "Мне должны"),
        new(DebtDirection.Borrowed, "Я должен"),
    ];
}

/// <summary>
/// A line of «Долги»: «Саша», «3 000 ₽ из 5 000 ₽», «дал 12 октября · вернуть до 15 ноября», overdue in the warning color.
/// </summary>
public sealed record DebtItem(Debt Debt, string Person, string AmountText, string Details, string DueText, bool IsOverdue)
{
    public bool IsLent => Debt.Direction == DebtDirection.Lent;

    public bool IsClosed => Debt.IsClosed;
}

/// <summary>
/// «Ещё → Долги»: who owes the user and whom the user owes, with the dates agreed. A notebook apart from the
/// accounts: nothing here changes a balance, so it stays out of the summaries.
/// </summary>
public sealed partial class DebtsViewModel(IFinanceBackend backend, TimeProvider timeProvider) : ObservableObject
{
    private CurrencyCode _baseCurrency = CurrencyCode.Rub;
    private IReadOnlyList<CurrencyCode> _currencies = [CurrencyCode.Rub];

    public ObservableCollection<DebtItem> Open { get; } = [];

    public ObservableCollection<DebtItem> Closed { get; } = [];

    public bool IsEmpty => Open.Count == 0 && Closed.Count == 0;

    public bool HasClosed => Closed.Count > 0;

    [ObservableProperty]
    public partial bool ShowClosed { get; set; }

    /// <summary>«15 000 ₽ · 200 $»; empty when nobody owes the user.</summary>
    [ObservableProperty]
    public partial string LentText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string BorrowedText { get; private set; } = string.Empty;

    /// <summary>For the «Ещё» menu: «Мне должны 15 000 ₽ · я должен 3 000 ₽ · 1 просрочен».</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = Hint;

    [ObservableProperty]
    public partial bool HasOverdue { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>The open editor, or <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    public partial DebtEditor? Editor { get; private set; }

    public bool IsEditing => Editor is not null;

    private const string Hint = "Кто кому должен и до какого числа";

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var catalog = await backend.LoadCatalogAsync(cancellationToken);
            _baseCurrency = catalog.BaseCurrency;
            _currencies = catalog.Currencies;
            Apply(await backend.ListDebtsAsync(cancellationToken));
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось загрузить долги: {ex.Message}";
        }
    }

    [RelayCommand]
    private void New() => OpenEditor(new DebtEditor(null, _currencies, _baseCurrency, Today));

    [RelayCommand]
    private void Edit(DebtItem item) => OpenEditor(new DebtEditor(item.Debt, _currencies, _baseCurrency, Today));

    [RelayCommand]
    private void ToggleClosed() => ShowClosed = !ShowClosed;

    private void OpenEditor(DebtEditor editor)
    {
        editor.Closed += async (_, action) =>
        {
            var problem = await ApplyAsync(editor, action);
            if (problem is null)
            {
                Editor = null;
                await LoadAsync();
            }
            else
            {
                editor.Error = problem;
            }
        };
        Editor = editor;
    }

    /// <returns>What went wrong, to show in the editor; <c>null</c> when done.</returns>
    private async Task<string?> ApplyAsync(DebtEditor editor, DebtEditor.Action action)
    {
        try
        {
            switch (action)
            {
                case DebtEditor.Action.Save:
                    var input = editor.ToInput();
                    if (editor.Debt is { } existing)
                    {
                        await backend.UpdateDebtAsync(existing.Id, input);
                    }
                    else
                    {
                        await backend.AddDebtAsync(input);
                    }

                    break;
                case DebtEditor.Action.Repay when editor.Debt is { } debt:
                    Format.TryParseAmount(editor.RepayText, out var amount);
                    await backend.RepayDebtAsync(debt.Id, amount);
                    break;
                case DebtEditor.Action.RepayAll when editor.Debt is { } debt:
                    await backend.RepayDebtAsync(debt.Id, debt.Left);
                    break;
                case DebtEditor.Action.Delete when editor.Debt is { } debt:
                    await backend.DeleteDebtAsync(debt.Id);
                    break;
                default:
                    break;
            }

            return null;
        }
#pragma warning disable CA1031 // Shown in the editor.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return ex.Message;
        }
    }

    private void Apply(IReadOnlyList<Debt> debts)
    {
        var today = Today;
        Open.Clear();
        Closed.Clear();
        foreach (var debt in debts)
        {
            (debt.IsClosed ? Closed : Open).Add(Describe(debt, today));
        }

        var totals = DebtService.Totals(debts);
        LentText = Join(totals, DebtDirection.Lent);
        BorrowedText = Join(totals, DebtDirection.Borrowed);
        var overdue = debts.Count(d => d.IsOverdue(today));
        HasOverdue = overdue > 0;

        var parts = new List<string>();
        if (LentText.Length > 0)
        {
            parts.Add($"Мне должны {LentText}");
        }

        if (BorrowedText.Length > 0)
        {
            parts.Add($"{(parts.Count == 0 ? "Я" : "я")} должен {BorrowedText}");
        }

        if (overdue > 0)
        {
            parts.Add($"{overdue} {Overdue(overdue)}");
        }

        Summary = parts.Count == 0 ? Hint : string.Join(" · ", parts);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasClosed));
    }

    public static DebtItem Describe(Debt debt, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(debt);
        var lent = debt.Direction == DebtDirection.Lent;
        var amount = debt.IsClosed || debt.Repaid == 0
            ? Format.Money(debt.Amount, debt.Currency)
            : $"{Format.Money(debt.Left, debt.Currency)} из {Format.Money(debt.Amount, debt.Currency)}";
        var details = $"{(lent ? "дал" : "взял")} {RecurringText.When(debt.Date, today)}";
        if (debt.Note is { } note)
        {
            details += $" · {note}";
        }

        string due;
        if (debt.ClosedOn is { } closed)
        {
            due = $"{(lent ? "вернули" : "вернул")} {RecurringText.When(closed, today)}";
        }
        else if (debt.DueDate is not { } date)
        {
            due = "без срока";
        }
        else if (date < today)
        {
            var days = today.DayNumber - date.DayNumber;
            due = $"просрочен на {days} {Days(days)} — срок был {RecurringText.When(date, today)}";
        }
        else
        {
            due = $"вернуть до {RecurringText.When(date, today)}";
        }

        return new DebtItem(debt, debt.Person, amount, details, due, debt.IsOverdue(today));
    }

    private static string Join(IReadOnlyList<DebtTotal> totals, DebtDirection direction) =>
        string.Join(" · ", totals.Where(t => t.Direction == direction).Select(t => Format.Money(t.Left, t.Currency)));

    private static string Days(int days) => (days % 10, days % 100) switch
    {
        (1, not 11) => "день",
        (2 or 3 or 4, not (12 or 13 or 14)) => "дня",
        _ => "дней",
    };

    private static string Overdue(int count) => count % 10 == 1 && count % 100 != 11 ? "просрочен" : "просрочено";
}

/// <summary>A new debt, or an existing one: its details, and «вернули часть» / «вернули всё».</summary>
public sealed partial class DebtEditor : ObservableObject
{
    public DebtEditor(Debt? debt, IReadOnlyList<CurrencyCode> currencies, CurrencyCode baseCurrency, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(currencies);
        Debt = debt;
        Currencies = debt is null || currencies.Contains(debt.Currency) ? currencies : [.. currencies, debt.Currency];
        Direction = DebtDirectionChoice.All.First(d => d.Direction == (debt?.Direction ?? DebtDirection.Lent));
        Person = debt?.Person ?? string.Empty;
        AmountText = debt?.Amount.ToString("0.##", Format.Russian) ?? string.Empty;
        Currency = debt?.Currency ?? baseCurrency;
        DateText = Format.DateText(debt?.Date ?? today);
        DueText = debt?.DueDate is { } due ? Format.DateText(due) : string.Empty;
        Note = debt?.Note ?? string.Empty;
        RepayText = debt is { IsClosed: false } ? debt.Left.ToString("0.##", Format.Russian) : string.Empty;
    }

    public enum Action
    {
        Cancel,
        Save,
        Repay,
        RepayAll,
        Delete,
    }

    /// <summary><c>null</c> for a new one.</summary>
    public Debt? Debt { get; }

    public bool IsNew => Debt is null;

    public string Title => Debt is null ? "Новый долг" : Debt.Person;

    /// <summary>The repayment block: for an open debt only.</summary>
    public bool CanRepay => Debt is { IsClosed: false };

    /// <summary>«Вернули 3 000 ₽ из 5 000 ₽» / «Остаток 5 000 ₽».</summary>
    public string RepaidText => Debt is not { } debt ? string.Empty
        : debt.Repaid == 0 ? $"Остаток {Format.Money(debt.Left, debt.Currency)}"
        : $"Уже вернули {Format.Money(debt.Repaid, debt.Currency)} из {Format.Money(debt.Amount, debt.Currency)}";

    public string RepayAllTitle => Debt?.Direction == DebtDirection.Borrowed ? "Я вернул всё" : "Вернули всё";

    public IReadOnlyList<DebtDirectionChoice> Directions => DebtDirectionChoice.All;

    public IReadOnlyList<CurrencyCode> Currencies { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PersonLabel), nameof(DateLabel))]
    public partial DebtDirectionChoice Direction { get; set; }

    public string PersonLabel => Direction.Direction == DebtDirection.Lent ? "Кто должен" : "Кому должен";

    public string DateLabel => Direction.Direction == DebtDirection.Lent ? "Когда дал" : "Когда взял";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string Person { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string AmountText { get; set; }

    [ObservableProperty]
    public partial CurrencyCode Currency { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string DateText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string DueText { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string RepayText { get; set; }

    /// <summary>What the backend refused, shown in the editor.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    public string? Problem =>
        string.IsNullOrWhiteSpace(Person) ? "Укажите, кто должен или кому."
        : !Format.TryParseAmount(AmountText, out var amount) || amount <= 0 || decimal.Round(amount, 2) != amount ? "Сумма — больше нуля, не больше двух знаков после запятой."
        : !Format.TryParseDate(DateText, out var date) ? "Выберите день, когда дали или взяли деньги."
        : Format.TryParseDate(DueText, out var due) && due < date ? "Срок возврата раньше дня, когда дали деньги."
        : null;

    public event EventHandler<Action>? Closed;

    public DebtInput ToInput()
    {
        Format.TryParseAmount(AmountText, out var amount);
        Format.TryParseDate(DateText, out var date);
        DateOnly? due = Format.TryParseDate(DueText, out var day) ? day : null;
        return new DebtInput(Direction.Direction, Person.Trim(), amount, Currency, date, due, Note);
    }

    [RelayCommand]
    private void ChooseDirection(DebtDirectionChoice choice) => Direction = choice;

    [RelayCommand]
    private void ChooseCurrency(CurrencyCode currency) => Currency = currency;

    [RelayCommand]
    private void Save()
    {
        if (Problem is { } problem)
        {
            Error = problem;
            return;
        }

        Closed?.Invoke(this, Action.Save);
    }

    [RelayCommand]
    private void Repay()
    {
        if (!Format.TryParseAmount(RepayText, out var amount) || amount <= 0)
        {
            Error = "Сколько вернули — больше нуля.";
            return;
        }

        Closed?.Invoke(this, Action.Repay);
    }

    [RelayCommand]
    private void RepayAll() => Closed?.Invoke(this, Action.RepayAll);

    [RelayCommand]
    private void Delete() => Closed?.Invoke(this, Action.Delete);

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, Action.Cancel);
}
