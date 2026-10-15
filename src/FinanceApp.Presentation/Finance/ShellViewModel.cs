using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Accounts;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

public enum SidebarKind
{
    AllOperations,
    Summary,
    Categories,
    Account,
    Envelope,
}

/// <summary>An entry of the left column: «Все операции», «Итоги», an account or an envelope with its balance.</summary>
public sealed record SidebarItem(SidebarKind Kind, string Title, string? BalanceText, Account? Account = null, double? Progress = null, string? Hint = null)
{
    public bool IsNegative => BalanceText?.StartsWith('−') == true;

    public bool HasProgress => Progress is not null;

    public static SidebarItem From(AccountBalance balance, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(balance);
        var account = balance.Account;
        string? hint = null;
        if (account.GoalAmount is { } goal)
        {
            hint = $"цель {Format.Money(goal, account.Currency)}" + (account.GoalDate is { } date ? $" к {date.ToString("d MMMM yyyy", Format.Russian)}" : string.Empty);
            if (balance.MonthlyToGoal(today) is { } monthly)
            {
                hint += $" · по {Format.Money(monthly, account.Currency)} в месяц";
            }
        }

        return new SidebarItem(
            account.IsEnvelope ? SidebarKind.Envelope : SidebarKind.Account,
            account.Name,
            Format.Balance(balance.Balance, account.Currency),
            account,
            balance.GoalProgress is { } progress ? (double)progress : null,
            hint);
    }
}

/// <summary>
/// The main window (requirements, section 3.3): accounts and envelopes with balances on the left, the
/// operations of the selected one or the summary on the right, simple forms for new operations.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IFinanceBackend _backend;
    private readonly TimeProvider _timeProvider;
    private bool _refreshingTotals;

    public ShellViewModel(IFinanceBackend backend, TimeProvider timeProvider)
    {
        _backend = backend;
        _timeProvider = timeProvider;
        Operations = new OperationsViewModel(backend, timeProvider);
        Summary = new SummaryViewModel(backend, timeProvider);
        Categories = new CategoriesViewModel(backend);
        Operations.EditRequested += (_, transaction) => _ = OpenEditAsync(transaction);
        Operations.Changed += (_, _) => _ = RefreshBalancesAsync();
        Selected = AllOperations;
    }

    public SidebarItem AllOperations { get; } = new(SidebarKind.AllOperations, "Все операции", null);

    public SidebarItem SummaryItem { get; } = new(SidebarKind.Summary, "Итоги", null);

    public SidebarItem CategoriesItem { get; } = new(SidebarKind.Categories, "Категории", null);

    public CategoriesViewModel Categories { get; }

    public OperationsViewModel Operations { get; }

    public SummaryViewModel Summary { get; }

    public ObservableCollection<SidebarItem> Accounts { get; } = [];

    public ObservableCollection<SidebarItem> Envelopes { get; } = [];

    [ObservableProperty]
    public partial string TotalText { get; private set; } = string.Empty;

    /// <summary>«На счетах»: regular accounts, in the totals currency.</summary>
    [ObservableProperty]
    public partial string AccountsTotalText { get; private set; } = string.Empty;

    /// <summary>«В накоплениях»: envelopes, in the totals currency.</summary>
    [ObservableProperty]
    public partial string EnvelopesTotalText { get; private set; } = string.Empty;

    /// <summary>Currencies offered for the totals (base, usual ones, those of the accounts).</summary>
    public ObservableCollection<CurrencyCode> TotalsCurrencies { get; } = [];

    [ObservableProperty]
    public partial CurrencyCode? TotalsCurrency { get; set; }

    [ObservableProperty]
    public partial string Warning { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSummary), nameof(IsCategories), nameof(IsOperations), nameof(IsEnvelopeSelected), nameof(SelectedAccount))]
    public partial SidebarItem Selected { get; set; }

    public bool IsSummary => Selected.Kind == SidebarKind.Summary;

    public bool IsCategories => Selected.Kind == SidebarKind.Categories;

    public bool IsOperations => !IsSummary && !IsCategories;

    public bool IsEnvelopeSelected => Selected.Kind == SidebarKind.Envelope;

    public Account? SelectedAccount => Selected.Account;

    /// <summary>The open «Расход / Доход / Перевод» form, or <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasForm))]
    public partial TransactionFormViewModel? Form { get; private set; }

    /// <summary>The open account or envelope form, or <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasForm))]
    public partial AccountFormViewModel? AccountForm { get; private set; }

    public bool HasForm => Form is not null || AccountForm is not null;

    [ObservableProperty]
    public partial bool ShowHidden { get; set; }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    /// <summary>Called when the window is shown and after anything changed outside (quick entry).</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshBalancesAsync(cancellationToken);
        if (IsSummary)
        {
            await Summary.LoadAsync(cancellationToken);
        }
        else if (IsCategories)
        {
            await Categories.LoadAsync(cancellationToken);
        }
        else
        {
            await Operations.ShowAsync(Selected.Account, cancellationToken);
        }
    }

    [RelayCommand]
    private Task NewExpenseAsync() => OpenNewAsync(TransactionType.Expense);

    [RelayCommand]
    private Task NewIncomeAsync() => OpenNewAsync(TransactionType.Income);

    [RelayCommand]
    private Task NewTransferAsync() => OpenNewAsync(TransactionType.Transfer);

    /// <summary>«Пополнить» the selected envelope: a transfer into it from the default account.</summary>
    [RelayCommand]
    private Task FillEnvelopeAsync() => OpenNewAsync(TransactionType.Transfer, toAccount: SelectedAccount);

    /// <summary>«Взять» from the selected envelope: a transfer out of it to the default account.</summary>
    [RelayCommand]
    private async Task TakeFromEnvelopeAsync()
    {
        if (SelectedAccount is not { IsEnvelope: true } envelope)
        {
            return;
        }

        var catalog = await _backend.LoadCatalogAsync();
        ShowForm(TransactionFormViewModel.ForNew(_backend, catalog, Today, TransactionType.Transfer, envelope, catalog.DefaultAccount(TransactionType.Transfer)));
    }

    [RelayCommand]
    private Task NewAccountAsync() => OpenAccountFormAsync(AccountKind.Regular);

    [RelayCommand]
    private Task NewEnvelopeAsync() => OpenAccountFormAsync(AccountKind.Envelope);

    [RelayCommand]
    private void EditAccount(SidebarItem? item)
    {
        if ((item ?? Selected).Account is { } account)
        {
            ShowAccountForm(AccountFormViewModel.ForEdit(_backend, account));
        }
    }

    partial void OnSelectedChanged(SidebarItem value)
    {
        switch (value.Kind)
        {
            case SidebarKind.Summary:
                _ = Summary.LoadAsync();
                break;
            case SidebarKind.Categories:
                _ = Categories.LoadAsync();
                break;
            default:
                _ = Operations.ShowAsync(value.Account);
                break;
        }
    }

    partial void OnTotalsCurrencyChanged(CurrencyCode? value)
    {
        if (!_refreshingTotals && value is { } currency)
        {
            _ = ChangeTotalsCurrencyAsync(currency);
        }
    }

    private async Task ChangeTotalsCurrencyAsync(CurrencyCode currency)
    {
        await _backend.SetTotalsCurrencyAsync(currency);

        // The summary shows its amounts in the same currency.
        await RefreshAsync();
    }

    partial void OnShowHiddenChanged(bool value) => _ = RefreshBalancesAsync();

    private async Task OpenNewAsync(TransactionType kind, Account? toAccount = null)
    {
        var catalog = await _backend.LoadCatalogAsync();
        var from = kind == TransactionType.Transfer && toAccount is not null ? null : SelectedAccount;
        ShowForm(TransactionFormViewModel.ForNew(_backend, catalog, Today, kind, from, toAccount));
    }

    private async Task OpenEditAsync(Transaction transaction)
    {
        var catalog = await _backend.LoadCatalogAsync();
        ShowForm(TransactionFormViewModel.ForEdit(_backend, catalog, Today, transaction));
    }

    private async Task OpenAccountFormAsync(AccountKind kind)
    {
        var catalog = await _backend.LoadCatalogAsync();
        ShowAccountForm(AccountFormViewModel.ForNew(_backend, kind, catalog.BaseCurrency));
    }

    private void ShowForm(TransactionFormViewModel form)
    {
        AccountForm = null;
        form.Saved += async (_, _) =>
        {
            Form = null;
            await RefreshAsync();
        };
        form.Cancelled += (_, _) => Form = null;
        Form = form;
    }

    private void ShowAccountForm(AccountFormViewModel form)
    {
        Form = null;
        form.Saved += async (_, account) =>
        {
            AccountForm = null;
            await RefreshBalancesAsync();
            var item = Accounts.Concat(Envelopes).FirstOrDefault(i => i.Account?.Id == account.Id);
            if (item is not null && !account.IsHidden)
            {
                Selected = item;
            }
            else
            {
                await RefreshAsync();
            }
        };
        form.Cancelled += (_, _) => AccountForm = null;
        AccountForm = form;
    }

    private async Task RefreshBalancesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var balances = await _backend.LoadBalancesAsync(ShowHidden, cancellationToken);
            var display = balances.DisplayCurrency;
            TotalText = Format.Balance(balances.InDisplay(balances.Total), display);
            AccountsTotalText = Format.Balance(balances.InDisplay(balances.FreeTotal), display);
            EnvelopesTotalText = Format.Balance(balances.InDisplay(balances.EnvelopesTotal), display);
            Warning = balances.HasMissingRates ? "Для части валют нет курса — итог посчитан без них." : string.Empty;

            _refreshingTotals = true;
            try
            {
                Replace(TotalsCurrencies, balances.DisplayChoices);
                TotalsCurrency = display;
            }
            finally
            {
                _refreshingTotals = false;
            }

            var selectedId = Selected.Account?.Id;
            Replace(Accounts, balances.Accounts.Select(b => SidebarItem.From(b, Today)));
            Replace(Envelopes, balances.Envelopes.Select(b => SidebarItem.From(b, Today)));

            // Keep the selection on the same account after its balance changed.
            if (selectedId is { } id && Accounts.Concat(Envelopes).FirstOrDefault(i => i.Account?.Id == id) is { } same)
            {
                Selected = same;
            }
        }
#pragma warning disable CA1031 // Shown in the sidebar.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Warning = $"Не удалось посчитать остатки: {ex.Message}";
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
