using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Accounts;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>
/// Create or edit an account or an envelope. The goal is opt-in («Добавить цель») so it never gets in the
/// way; the opening balance is optional because accounts are a convenience, not a copy of a bank.
/// </summary>
public sealed partial class AccountFormViewModel : ObservableObject
{
    private readonly IFinanceBackend _backend;
    private readonly Account? _editing;

    private AccountFormViewModel(IFinanceBackend backend, AccountKind kind, CurrencyCode baseCurrency, Account? editing)
    {
        _backend = backend;
        _editing = editing;
        Kind = kind;
        CurrencyText = (editing?.Currency ?? baseCurrency).Value;
        if (editing is not null)
        {
            Name = editing.Name;
            OpeningBalanceText = editing.OpeningBalance == 0 ? string.Empty : editing.OpeningBalance.ToString("0.##", Format.Russian);
            HasGoal = editing.GoalAmount is not null;
            GoalAmountText = editing.GoalAmount?.ToString("0.##", Format.Russian) ?? string.Empty;
            GoalDateText = editing.GoalDate is { } date ? Format.DateText(date) : string.Empty;
            IsHidden = editing.IsHidden;
        }
    }

    public AccountKind Kind { get; }

    public bool IsEnvelope => Kind == AccountKind.Envelope;

    public bool IsNew => _editing is null;

    public string Title => (IsNew ? "Новый " : "Изменить ") + (IsEnvelope ? "конверт" : "счёт");

    public string NamePlaceholder => IsEnvelope ? "Машина, Отдых, Велосипед…" : "Карта, Наличные…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Only for a new account: the currency of an existing one cannot change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string CurrencyText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string OpeningBalanceText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial bool HasGoal { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string GoalAmountText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string GoalDateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsHidden { get; private set; }

    [ObservableProperty]
    public partial string SaveError { get; private set; } = string.Empty;

    public string? Problem =>
        string.IsNullOrWhiteSpace(Name) ? "Введите название."
        : !CurrencyCode.TryParse(CurrencyText, out _) ? "Валюта — трёхбуквенный код, например RUB или USD."
        : OpeningBalanceText.Trim().Length > 0 && !Format.TryParseAmount(OpeningBalanceText, out _) ? "Начальный остаток — число."
        : HasGoal && !(Format.TryParseAmount(GoalAmountText, out var goal) && goal > 0) ? "Цель — сумма больше нуля."
        : HasGoal && GoalDateText.Trim().Length > 0 && !Format.TryParseDate(GoalDateText, out _) ? "Срок — дата в формате дд.мм.гггг, можно оставить пустым."
        : null;

    public bool CanSave => Problem is null;

    public event EventHandler<Account>? Saved;

    public event EventHandler? Cancelled;

    public static AccountFormViewModel ForNew(IFinanceBackend backend, AccountKind kind, CurrencyCode baseCurrency) =>
        new(backend, kind, baseCurrency, null);

    public static AccountFormViewModel ForEdit(IFinanceBackend backend, Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new(backend, account.Kind, account.Currency, account);
    }

    [RelayCommand]
    private void AddGoal() => HasGoal = true;

    [RelayCommand]
    private void RemoveGoal()
    {
        HasGoal = false;
        GoalAmountText = string.Empty;
        GoalDateText = string.Empty;
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (Problem is { } problem)
        {
            SaveError = problem;
            return;
        }

        Format.TryParseAmount(OpeningBalanceText, out var opening);
        decimal? goal = HasGoal && Format.TryParseAmount(GoalAmountText, out var g) ? g : null;
        DateOnly? goalDate = HasGoal && Format.TryParseDate(GoalDateText, out var d) ? d : null;
        var input = new AccountInput(Name.Trim(), Kind, CurrencyCode.Parse(CurrencyText), opening, goal, goalDate);
        try
        {
            var saved = _editing is null
                ? await _backend.CreateAccountAsync(input, cancellationToken)
                : await _backend.UpdateAccountAsync(_editing.Id, input with { Currency = null }, cancellationToken);
            Saved?.Invoke(this, saved);
        }
#pragma warning disable CA1031 // Shown on the form.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            SaveError = ex.Message;
        }
    }

    /// <summary>Hide or show again; hidden accounts keep their history.</summary>
    [RelayCommand]
    private async Task ToggleHiddenAsync(CancellationToken cancellationToken)
    {
        if (_editing is null)
        {
            return;
        }

        try
        {
            await _backend.SetAccountHiddenAsync(_editing.Id, !IsHidden, cancellationToken);
            IsHidden = !IsHidden;
            Saved?.Invoke(this, _editing);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            SaveError = ex.Message;
        }
    }
}
