using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>A category button of the form; the most used ones come first.</summary>
public sealed partial class CategoryTile(Category category) : ObservableObject
{
    public Category Category { get; } = category;

    public string Name => Category.Name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// The «Расход / Доход / Перевод» form — the no-syntax way to record a transaction, used both for new
/// operations and for editing. Category tiles instead of a dropdown, date shortcuts, a received amount
/// only when the two accounts of a transfer have different currencies.
/// </summary>
public sealed partial class TransactionFormViewModel : ObservableObject
{
    private readonly IFinanceBackend _backend;
    private readonly Catalog _catalog;
    private readonly DateOnly _today;
    private readonly Transaction? _editing;
    private readonly List<Category> _created = [];
    private int _conversion;

    private TransactionFormViewModel(IFinanceBackend backend, Catalog catalog, DateOnly today, Transaction? editing)
    {
        _backend = backend;
        _catalog = catalog;
        _today = today;
        _editing = editing;
        var visible = catalog.Accounts.Where(a => !a.IsHidden || a.Id == editing?.AccountId || a.Id == editing?.ToAccountId).ToList();
        Accounts = visible;
        DateText = Format.DateText(today);
    }

    public static IReadOnlyList<TransactionType> Kinds { get; } = [TransactionType.Expense, TransactionType.Income, TransactionType.Transfer];

    public bool IsNew => _editing is null;

    public string Title =>
        (IsNew ? "Новый " : "Изменить ") + Kind switch
        {
            TransactionType.Income => "доход",
            TransactionType.Transfer => "перевод",
            _ => "расход",
        };

    /// <summary>All visible accounts and envelopes (plus the ones of the edited operation).</summary>
    public IReadOnlyList<Account> Accounts { get; }

    public ObservableCollection<CategoryTile> Tiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsTransfer), nameof(Problem), nameof(CanSave))]
    public partial TransactionType Kind { get; set; } = TransactionType.Expense;

    public bool IsTransfer => Kind == TransactionType.Transfer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave), nameof(AmountCurrency), nameof(ReceivedPreview))]
    public partial string AmountText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial Category? Category { get; set; }

    /// <summary>The account paid from or into; the source of a transfer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave), nameof(NeedsReceivedAmount), nameof(AmountCurrency), nameof(ReceivedCurrency), nameof(RateLabel), nameof(ReceivedPreview))]
    public partial Account? Account { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave), nameof(NeedsReceivedAmount), nameof(ReceivedCurrency), nameof(RateLabel), nameof(ReceivedPreview))]
    public partial Account? ToAccount { get; set; }

    /// <summary>Different currencies: enter the rate instead of the received amount.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave), nameof(ReceivedPreview))]
    public partial bool UseRate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave), nameof(ReceivedPreview))]
    public partial string RateText { get; set; } = string.Empty;

    /// <summary>
    /// <c>false</c>: «1 {from} = rate {to}», received = amount × rate; <c>true</c>: «1 {to} = rate {from}»,
    /// received = amount ÷ rate. Starts so that the rate reads as usual: «1 USD = 90 RUB» both ways.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLabel), nameof(ReceivedPreview))]
    public partial bool RateInverted { get; set; }

    public string RateLabel =>
        Account is null || ToAccount is null ? string.Empty
        : RateInverted ? $"1 {ToAccount.Currency.Value} =" : $"1 {Account.Currency.Value} =";

    public string RateUnit => Account is null || ToAccount is null ? string.Empty : (RateInverted ? Account.Currency : ToAccount.Currency).Value;

    /// <summary>«Зачислится 100 USD» under the rate.</summary>
    public string ReceivedPreview =>
        NeedsReceivedAmount && UseRate && ReceivedByRate() is { } received ? $"Зачислится {Format.Money(received, ToAccount!.Currency)}" : string.Empty;

    [ObservableProperty]
    public partial bool IsAddingCategory { get; set; }

    [ObservableProperty]
    public partial string NewCategoryName { get; set; } = string.Empty;

    /// <summary>Optional for different currencies: empty — converted at the CBR rate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string ReceivedText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string DateText { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>An error from saving (offline rates, …).</summary>
    [ObservableProperty]
    public partial string SaveError { get; private set; } = string.Empty;

    public bool NeedsReceivedAmount => IsTransfer && Account is not null && ToAccount is not null && Account.Currency != ToAccount.Currency;

    public string AmountCurrency => (IsForeign ? PaidCurrency!.Value : Account?.Currency ?? _catalog.BaseCurrency).Value;

    /// <summary>
    /// Currencies a receipt may be in: the account's first, then the base one, the usual ones and those of the
    /// accounts. Another than the account's is converted on saving (TransactionInput.PaidCurrency).
    /// </summary>
    public ObservableCollection<CurrencyCode> PaidCurrencies { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForeign), nameof(AmountCurrency), nameof(ChargedLabel), nameof(Problem), nameof(CanSave))]
    public partial CurrencyCode? PaidCurrency { get; set; }

    /// <summary>The receipt is in another currency than the account: the amount is converted on saving.</summary>
    public bool IsForeign => !IsTransfer && Account is not null && PaidCurrency is { } paid && paid != Account.Currency;

    /// <summary>What the bank charged, in the account currency, if known (from its SMS); empty — the CBR rate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string ChargedText { get; set; } = string.Empty;

    public string ChargedLabel => Account is null ? string.Empty : $"Списано со счёта, {Account.Currency.Value} — если знаете";

    /// <summary>«≈ 1 850 ₽ по курсу ЦБ на 20.10.2026» or why there is no rate.</summary>
    [ObservableProperty]
    public partial string ConversionText { get; private set; } = string.Empty;

    /// <summary>No CBR rate (offline, nothing cached): what was charged has to be typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial bool RateMissing { get; private set; }

    public string ReceivedCurrency => ToAccount?.Currency.Value ?? string.Empty;

    public string? Problem =>
        !TryAmount(AmountText, out _) ? "Введите сумму больше нуля, не больше двух знаков после запятой."
        : !Format.TryParseDate(DateText, out _) ? "Дата — в формате дд.мм.гггг."
        : Account is null ? (IsTransfer ? "Выберите, откуда перевести." : "Выберите счёт.")
        : IsTransfer && ToAccount is null ? "Выберите, куда перевести."
        : IsTransfer && ToAccount!.Id == Account.Id ? "Счета «откуда» и «куда» должны различаться."
        : NeedsReceivedAmount && !UseRate && ReceivedText.Trim().Length > 0 && !TryAmount(ReceivedText, out _) ? "Зачислено — положительное число."
        : NeedsReceivedAmount && UseRate && ReceivedByRate() is null ? "Курс — положительное число."
        : !IsTransfer && Category is null ? "Выберите категорию."
        : IsForeign && ChargedText.Trim().Length > 0 && !TryAmount(ChargedText, out _) ? "Списано — сумма больше нуля."
        : IsForeign && RateMissing && ChargedText.Trim().Length == 0 ? $"Нет курса {PaidCurrency} — укажите, сколько списано со счёта."
        : null;

    public bool CanSave => Problem is null && !IsBusy;

    /// <summary>Raised after saving; the form closes.</summary>
    public event EventHandler<SavedTransaction>? Saved;

    public event EventHandler? Cancelled;

    /// <param name="account">Preselected account (the one being viewed), if any.</param>
    /// <param name="toAccount">Preselected destination of a transfer (e.g. «put into this envelope»).</param>
    public static TransactionFormViewModel ForNew(
        IFinanceBackend backend, Catalog catalog, DateOnly today, TransactionType kind, Account? account = null, Account? toAccount = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var form = new TransactionFormViewModel(backend, catalog, today, null) { Kind = kind };

        // Presets may come from another load (the sidebar's balances): match by id, so the dropdowns show them.
        Account? FromList(Account? a) => a is null ? null : form.Accounts.FirstOrDefault(x => x.Id == a.Id);
        form.Account = FromList(account) ?? FromList(catalog.DefaultAccount(kind));
        form.ToAccount = FromList(toAccount);
        if (kind == TransactionType.Transfer && form.ToAccount?.Id == form.Account?.Id)
        {
            form.Account = form.Accounts.FirstOrDefault(a => !a.IsHidden && !a.IsEnvelope && a.Id != form.ToAccount?.Id);
        }

        form.RebuildTiles();
        return form;
    }

    public static TransactionFormViewModel ForEdit(IFinanceBackend backend, Catalog catalog, DateOnly today, Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(transaction);
        var form = new TransactionFormViewModel(backend, catalog, today, transaction) { Kind = transaction.Type };
        form.AmountText = transaction.Amount.ToString("0.##", Format.Russian);
        form.DateText = Format.DateText(transaction.Date);
        form.Note = transaction.Note ?? string.Empty;
        form.Account = form.Accounts.FirstOrDefault(a => a.Id == transaction.AccountId);
        form.ToAccount = form.Accounts.FirstOrDefault(a => a.Id == transaction.ToAccountId);
        if (transaction is { IsTransfer: true, ToAmount: { } received } && form.NeedsReceivedAmount)
        {
            form.ReceivedText = received.ToString("0.##", Format.Russian);
        }

        form.RebuildTiles(transaction.CategoryId);
        return form;
    }

    [RelayCommand]
    private void ChooseKind(TransactionType kind) => Kind = kind;

    [RelayCommand]
    private void ChooseCategory(CategoryTile tile)
    {
        foreach (var t in Tiles)
        {
            t.IsSelected = ReferenceEquals(t, tile);
        }

        Category = tile.Category;
    }

    [RelayCommand]
    private void SwapRate()
    {
        if (Format.TryParseAmount(RateText, out var rate) && rate > 0)
        {
            RateText = decimal.Round(1m / rate, 6).ToString("0.######", Format.Russian);
        }

        RateInverted = !RateInverted;
    }

    [RelayCommand]
    private void StartAddCategory()
    {
        NewCategoryName = string.Empty;
        IsAddingCategory = true;
    }

    [RelayCommand]
    private void CancelAddCategory() => IsAddingCategory = false;

    /// <summary>«+ Своя»: creates a category of the current kind and selects it.</summary>
    [RelayCommand]
    private async Task AddCategoryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName) || IsTransfer)
        {
            IsAddingCategory = false;
            return;
        }

        try
        {
            var category = await _backend.CreateCategoryAsync(NewCategoryName, Kind, cancellationToken);
            _created.RemoveAll(c => c.Id == category.Id);
            _created.Add(category);
            IsAddingCategory = false;
            SaveError = string.Empty;
            RebuildTiles(category.Id);
        }
#pragma warning disable CA1031 // A duplicate name is shown on the form.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            SaveError = ex.Message;
        }
    }

    [RelayCommand]
    private void Today() => DateText = Format.DateText(_today);

    [RelayCommand]
    private void Yesterday() => DateText = Format.DateText(_today.AddDays(-1));

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!CanSave)
        {
            SaveError = Problem ?? string.Empty;
            return;
        }

        TryAmount(AmountText, out var amount);
        Format.TryParseDate(DateText, out var date);
        IsBusy = true;
        SaveError = string.Empty;
        try
        {
            SavedTransaction saved;
            if (IsTransfer)
            {
                decimal? received = !NeedsReceivedAmount ? null
                    : UseRate ? ReceivedByRate()
                    : TryAmount(ReceivedText, out var r) ? r : null;
                var input = new TransferInput(date, amount, Account!.Id, ToAccount!.Id, Note, received);
                saved = _editing is null
                    ? await _backend.AddTransferAsync(input, cancellationToken)
                    : await _backend.UpdateTransferAsync(_editing.Id, input, cancellationToken);
            }
            else
            {
                var input = new TransactionInput(date, Kind, amount, Category!.Id, Account!.Id, Note,
                    PaidCurrency: IsForeign ? PaidCurrency : null,
                    PaidRate: IsForeign && TryAmount(ChargedText, out var charged) ? decimal.Round(charged / amount, 6) : null);
                saved = _editing is null
                    ? await _backend.AddAsync(input, cancellationToken)
                    : await _backend.UpdateAsync(_editing.Id, input, cancellationToken);
            }

            Saved?.Invoke(this, saved);
        }
#pragma warning disable CA1031 // Shown on the form; the user can fix the input and try again.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            SaveError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnAccountChanged(Account? value)
    {
        UpdateRateDirection();
        RebuildPaidCurrencies();
        _ = RefreshConversionAsync();
    }

    partial void OnPaidCurrencyChanged(CurrencyCode? value) => _ = RefreshConversionAsync();

    partial void OnAmountTextChanged(string value) => _ = RefreshConversionAsync();

    partial void OnDateTextChanged(string value) => _ = RefreshConversionAsync();

    [RelayCommand]
    private void ChoosePaidCurrency(CurrencyCode currency) => PaidCurrency = currency;

    /// <summary>The receipt keeps its currency when the account changes; a new account's own comes first.</summary>
    private void RebuildPaidCurrencies()
    {
        var choices = new[] { Account?.Currency ?? _catalog.BaseCurrency, _catalog.BaseCurrency, CurrencyCode.Usd, CurrencyCode.Eur }
            .Concat(Accounts.Select(a => a.Currency))
            .Concat(PaidCurrency is { } chosen ? [chosen] : [])
            .Distinct()
            .ToList();
        if (!PaidCurrencies.SequenceEqual(choices))
        {
            PaidCurrencies.Clear();
            foreach (var currency in choices)
            {
                PaidCurrencies.Add(currency);
            }
        }

        PaidCurrency ??= Account?.Currency;
        OnPropertyChanged(nameof(IsForeign));
        OnPropertyChanged(nameof(AmountCurrency));
        OnPropertyChanged(nameof(ChargedLabel));
    }

    /// <summary>The CBR estimate under the amount, for a receipt in another currency; a stale answer is dropped.</summary>
    private async Task RefreshConversionAsync()
    {
        var version = ++_conversion;
        if (!IsForeign || !Format.TryParseDate(DateText, out var date))
        {
            ConversionText = string.Empty;
            RateMissing = false;
            return;
        }

        var (paid, account) = (PaidCurrency!.Value, Account!.Currency);
        try
        {
            var quote = await _backend.GetRateAsync(paid, account, date);
            if (version != _conversion)
            {
                return;
            }

            RateMissing = false;
            var when = quote.IsFallback ? "по последнему известному курсу ЦБ" : $"по курсу ЦБ на {Format.DateText(date)}";
            ConversionText = TryAmount(AmountText, out var amount)
                ? $"≈ {Format.Money(decimal.Round(amount * quote.Rate, 2, MidpointRounding.AwayFromZero), account)} {when}"
                : $"1 {paid} = {quote.Rate.ToString("0.####", Format.Russian)} {account} {when}";
        }
        catch (ExchangeRateUnavailableException)
        {
            if (version == _conversion)
            {
                RateMissing = true;
                ConversionText = $"Курса {paid} пока нет: нет связи с ЦБ.";
            }
        }
    }

    partial void OnToAccountChanged(Account? value) => UpdateRateDirection();

    /// <summary>From the base currency, the rate is quoted for the other one: RUB → USD reads «1 USD = 90 RUB».</summary>
    private void UpdateRateDirection()
    {
        RateInverted = Account is not null && Account.Currency == _catalog.BaseCurrency;
        OnPropertyChanged(nameof(RateUnit));
    }

    private decimal? ReceivedByRate()
    {
        if (!TryAmount(AmountText, out var amount) || !Format.TryParseAmount(RateText, out var rate) || rate <= 0)
        {
            return null;
        }

        var received = decimal.Round(RateInverted ? amount / rate : amount * rate, 2, MidpointRounding.AwayFromZero);
        return received > 0 && received <= Domain.Transaction.MaxAmount ? received : null;
    }

    partial void OnKindChanged(TransactionType value)
    {
        IsAddingCategory = false;
        RebuildTiles();

        // A transfer has no receipt currency: its received amount is asked separately.
        OnPropertyChanged(nameof(IsForeign));
        OnPropertyChanged(nameof(AmountCurrency));
        _ = RefreshConversionAsync();
        if (value == TransactionType.Transfer)
        {
            Category = null;
        }
    }

    private void RebuildTiles(Guid? selected = null)
    {
        Tiles.Clear();
        if (IsTransfer)
        {
            return;
        }

        var categories = _catalog.CategoriesFor(Kind)
            .Where(c => _created.All(n => n.Id != c.Id))
            .Concat(_created.Where(c => c.Type == Kind))
            .ToList();
        if (selected is { } id && categories.All(c => c.Id != id) && _catalog.Categories.FirstOrDefault(c => c.Id == id) is { } hidden)
        {
            categories.Insert(0, hidden);
        }

        foreach (var category in categories)
        {
            Tiles.Add(new CategoryTile(category) { IsSelected = category.Id == (selected ?? Category?.Id) });
        }

        Category = Tiles.FirstOrDefault(t => t.IsSelected)?.Category;
    }

    private static bool TryAmount(string text, out decimal amount) =>
        Format.TryParseAmount(text, out amount) && amount > 0 && amount <= Domain.Transaction.MaxAmount && decimal.Round(amount, 2) == amount;
}
