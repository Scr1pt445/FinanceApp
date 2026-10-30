using FinanceApp.Application.Accounts;
using FinanceApp.Application.Debts;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Setup;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Core.Tests.Presentation;

/// <summary>In-memory backend with the real domain rules and summary math; rates are 1 or 90 for USD.</summary>
public sealed class FakeFinanceBackend : IFinanceBackend
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    public FakeFinanceBackend()
    {
        Categories = DefaultCatalog.CreateCategories().ToList();
        Card = Add(new Account(Guid.NewGuid(), "Карта", CurrencyCode.Rub, 0));
        Cash = Add(new Account(Guid.NewGuid(), "Наличные", CurrencyCode.Rub, 1));
    }

    public List<Category> Categories { get; }

    public List<Account> Accounts { get; } = [];

    public List<Transaction> Transactions { get; } = [];

    public Account Card { get; }

    public Account Cash { get; }

    public Exception? Failure { get; set; }

    /// <summary>Makes loading balances fail, as an unreadable database would.</summary>
    public Exception? LoadFailure { get; set; }

    public CurrencyCode? TotalsCurrency { get; private set; }

    public int Calls { get; private set; }

    public Category Named(string name, TransactionType type = TransactionType.Expense) =>
        Categories.Single(c => c.Name == name && c.Type == type);

    public Account Add(Account account)
    {
        Accounts.Add(account);
        return account;
    }

    public Transaction Spend(string date, decimal amount, string category, Account? account = null, string? note = null) =>
        Record(Transaction.Create(D(date), TransactionType.Expense, amount, 1m, Named(category), account ?? Card, note, Stamp()));

    public Transaction Earn(string date, decimal amount, string category = "Зарплата", Account? account = null) =>
        Record(Transaction.Create(D(date), TransactionType.Income, amount, 1m, Named(category, TransactionType.Income), account ?? Card, null, Stamp()));

    public Transaction Move(string date, decimal amount, Account from, Account to, decimal? received = null, string? note = null) =>
        Record(Transaction.CreateTransfer(D(date), amount, 1m, from, to, received, note, Stamp()));

    public Task<BalancesOverview> LoadBalancesAsync(bool includeHidden = false, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (LoadFailure is not null)
        {
            return Task.FromException<BalancesOverview>(LoadFailure);
        }

        var balances = Accounts.Where(a => includeHidden || !a.IsHidden)
            .Select(a =>
            {
                var balance = a.OpeningBalance + Transactions.Sum(t => t.EffectOn(a.Id));
                return new AccountBalance(a, balance, a.Currency == CurrencyCode.Usd ? balance * 90 : balance);
            })
            .ToList();
        var regular = balances.Where(b => !b.Account.IsEnvelope).ToList();
        var envelopes = balances.Where(b => b.Account.IsEnvelope).ToList();
        var display = TotalsCurrency ?? CurrencyCode.Rub;
        return Task.FromResult(new BalancesOverview(
            regular, envelopes, regular.Sum(b => b.BaseBalance!.Value), envelopes.Sum(b => b.BaseBalance!.Value), CurrencyCode.Rub, false,
            display, display == CurrencyCode.Usd ? 1m / 90 : 1m, Currencies));
    }

    public Task<PeriodOverview> LoadPeriodAsync(Period period, CancellationToken cancellationToken = default)
    {
        Calls++;
        var (categories, accounts) = (Categories.ToDictionary(c => c.Id), Accounts.ToDictionary(a => a.Id));
        var summary = SummaryCalculator.For(Transactions, period, categories, accounts, Today);
        var insights = PeriodInsights.For(Transactions, period, categories, accounts, Today);
        var of = Transactions.Where(t => period.Contains(t.Date)).OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ToList();
        return Task.FromResult(new PeriodOverview(period, of, summary, Categories, Accounts, CurrencyCode.Rub, insights));
    }

    public Task<Catalog> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        var usage = Transactions.Where(t => t.CategoryId is not null).GroupBy(t => t.CategoryId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var last = Transactions.Where(t => Accounts.Single(a => a.Id == t.AccountId) is { IsEnvelope: false })
            .GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.MaxBy(t => t.CreatedAt)!.AccountId);
        return Task.FromResult(new Catalog(Categories, Accounts, usage, last, CurrencyCode.Rub, Currencies));
    }

    /// <summary>The last expense or income saved, as the form sent it (receipt currency and rate included).</summary>
    public TransactionInput? LastInput { get; private set; }

    public Task<SavedTransaction> AddAsync(TransactionInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        LastInput = input;
        var t = Transaction.Create(input.Date, input.Type, input.Amount, 1m, Categories.Single(c => c.Id == input.CategoryId), Find(input.AccountId), input.Note, Stamp());
        return Task.FromResult(new SavedTransaction(Record(t), false));
    }

    public Task<SavedTransaction> UpdateAsync(Guid id, TransactionInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var t = Transactions.Single(x => x.Id == id);
        t.Update(input.Date, input.Type, input.Amount, 1m, Categories.Single(c => c.Id == input.CategoryId), Find(input.AccountId), input.Note, Stamp());
        return Task.FromResult(new SavedTransaction(t, false));
    }

    public Task<SavedTransaction> AddTransferAsync(TransferInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var (from, to) = (Find(input.FromAccountId), Find(input.ToAccountId));
        var received = input.ToAmount ?? (from.Currency == to.Currency ? null : input.Amount / 90);
        var t = Transaction.CreateTransfer(input.Date, input.Amount, 1m, from, to, received, input.Note, Stamp());
        return Task.FromResult(new SavedTransaction(Record(t), false));
    }

    public Task<SavedTransaction> UpdateTransferAsync(Guid id, TransferInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var t = Transactions.Single(x => x.Id == id);
        t.UpdateTransfer(input.Date, input.Amount, 1m, Find(input.FromAccountId), Find(input.ToAccountId), input.ToAmount, input.Note, Stamp());
        return Task.FromResult(new SavedTransaction(t, false));
    }

    public Task<Transaction> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var t = Transactions.Single(x => x.Id == id);
        Transactions.Remove(t);
        return Task.FromResult(t);
    }

    public Task RestoreAsync(Transaction deleted, CancellationToken cancellationToken = default)
    {
        Transactions.Add(deleted);
        return Task.CompletedTask;
    }

    public Task<Account> CreateAccountAsync(AccountInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var account = new Account(Guid.NewGuid(), input.Name, input.Currency ?? CurrencyCode.Rub, Accounts.Count, input.Kind, input.OpeningBalance);
        if (input.GoalAmount is { } goal)
        {
            account.SetGoal(goal, input.GoalDate);
        }

        return Task.FromResult(Add(account));
    }

    public Task<Account> UpdateAccountAsync(Guid id, AccountInput input, CancellationToken cancellationToken = default)
    {
        var account = Find(id);
        account.Rename(input.Name);
        account.SetOpeningBalance(input.OpeningBalance);
        if (input.GoalAmount is { } goal)
        {
            account.SetGoal(goal, input.GoalDate);
        }
        else
        {
            account.ClearGoal();
        }

        return Task.FromResult(account);
    }

    public Task SetAccountHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default)
    {
        if (hidden)
        {
            Find(id).Hide();
        }
        else
        {
            Find(id).Show();
        }

        return Task.CompletedTask;
    }

    public Task<Category> CreateCategoryAsync(string name, TransactionType type, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        if (Categories.Any(c => c.Type == type && string.Equals(c.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new DomainException($"Категория «{name}» уже есть.");
        }

        var category = new Category(Guid.NewGuid(), name, type, Categories.Count);
        Categories.Add(category);
        return Task.FromResult(category);
    }

    public Task<Category> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var category = Categories.Single(c => c.Id == id);
        category.Rename(name);
        return Task.FromResult(category);
    }

    public Task SetCategoryHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default)
    {
        var category = Categories.Single(c => c.Id == id);
        if (hidden)
        {
            category.Hide();
        }
        else
        {
            category.Show();
        }

        return Task.CompletedTask;
    }

    /// <summary>Rubles per unit; a currency missing here has no rate, as offline with an empty cache.</summary>
    public Dictionary<CurrencyCode, decimal> RubRates { get; } = new() { [CurrencyCode.Rub] = 1m, [CurrencyCode.Usd] = 90m, [CurrencyCode.Eur] = 100m };

    public Task<ExchangeRateQuote> GetRateAsync(CurrencyCode from, CurrencyCode to, DateOnly date, CancellationToken cancellationToken = default) =>
        RubRates.TryGetValue(from, out var fromRub) && RubRates.TryGetValue(to, out var toRub)
            ? Task.FromResult(new ExchangeRateQuote(from, to, date, fromRub / toRub, false))
            : Task.FromException<ExchangeRateQuote>(new ExchangeRateUnavailableException($"Нет курса {from}."));

    public List<ManualRate> ManualRates { get; } = [];

    public Task<IReadOnlyList<ManualRate>> ListManualRatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ManualRate>>([.. ManualRates]);

    public Task<ManualRate> AddManualRateAsync(
        CurrencyCode from, CurrencyCode to, decimal rate, DateOnly validFrom, ManualRatePeriod period, CancellationToken cancellationToken = default)
    {
        var added = new ManualRate(from, to, rate, validFrom, ManualRate.EndOf(validFrom, period));
        ManualRates.Add(added);
        return Task.FromResult(added);
    }

    public Task RemoveManualRateAsync(ManualRate rate, CancellationToken cancellationToken = default)
    {
        ManualRates.Remove(rate);
        return Task.CompletedTask;
    }

    public List<RecurringOperation> Recurring { get; } = [];

    public Task<IReadOnlyList<RecurringOperation>> ListRecurringAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecurringOperation>>([.. Recurring.OrderBy(r => r.NextDate)]);

    public Task<RecurringOperation> AddRecurringAsync(RecurringInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var added = RecurringOperation.Create(input.Type, input.Amount, input.CategoryId, input.AccountId, input.ToAccountId, input.Note, input.Frequency, input.NextDate, Stamp());
        if (input.StartAfter)
        {
            added.Advance();
        }

        Recurring.Add(added);
        return Task.FromResult(added);
    }

    public Task<RecurringOperation> UpdateRecurringAsync(Guid id, RecurringInput input, CancellationToken cancellationToken = default)
    {
        var operation = Recurring.Single(r => r.Id == id);
        operation.Update(input.Type, input.Amount, input.CategoryId, input.AccountId, input.ToAccountId, input.Note, input.Frequency, input.NextDate);
        return Task.FromResult(operation);
    }

    public Task DeleteRecurringAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Recurring.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public async Task<SavedTransaction> RecordRecurringAsync(Guid id, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        var operation = Recurring.Single(r => r.Id == id);
        var sum = amount ?? operation.Amount;
        var saved = operation.Type == TransactionType.Transfer
            ? await AddTransferAsync(new TransferInput(operation.NextDate, sum, operation.AccountId, operation.ToAccountId!.Value, operation.Note), cancellationToken)
            : await AddAsync(new TransactionInput(operation.NextDate, operation.Type, sum, operation.CategoryId!.Value, operation.AccountId, operation.Note), cancellationToken);
        operation.Advance();
        return saved;
    }

    public Task SkipRecurringAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Recurring.Single(r => r.Id == id).Advance();
        return Task.CompletedTask;
    }

    public Task<MonthLimits> LoadLimitsAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var lines = Categories.Where(c => c.MonthlyLimit is not null && !c.IsHidden)
            .Select(c => new LimitLine(c, c.MonthlyLimit!.Value, Transactions
                .Where(t => t.CategoryId == c.Id && t.Type == TransactionType.Expense && YearMonth.Of(t.Date) == month)
                .Sum(t => t.BaseAmount)))
            .OrderByDescending(l => l.Share)
            .ToList();
        return Task.FromResult(new MonthLimits(month, lines, CurrencyCode.Rub));
    }

    public Task SetCategoryLimitAsync(Guid id, decimal? limit, CancellationToken cancellationToken = default)
    {
        Categories.Single(c => c.Id == id).SetLimit(limit);
        return Task.CompletedTask;
    }

    public async Task<MonthComparison> LoadComparisonAsync(YearMonth month, YearMonth other, bool samePoint, CancellationToken cancellationToken = default)
    {
        var limits = await LoadLimitsAsync(month, cancellationToken);
        return MonthComparison.For(
            Transactions, month, other, samePoint, Categories.ToDictionary(c => c.Id), Accounts.ToDictionary(a => a.Id),
            Recurring, limits, currency => currency == CurrencyCode.Usd ? 90m : 1m, CurrencyCode.Rub, Today);
    }

    public List<Debt> Debts { get; } = [];

    public Task<IReadOnlyList<Debt>> ListDebtsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Debt>>([.. Debts.OrderBy(d => d.IsClosed).ThenBy(d => d.DueDate ?? DateOnly.MaxValue)]);

    public Task<Debt> AddDebtAsync(DebtInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var debt = Debt.Create(input.Direction, input.Person, input.Amount, input.Currency, input.Date, input.DueDate, input.Note, Stamp());
        Debts.Add(debt);
        return Task.FromResult(debt);
    }

    public Task<Debt> UpdateDebtAsync(Guid id, DebtInput input, CancellationToken cancellationToken = default)
    {
        var debt = Debts.Single(d => d.Id == id);
        debt.Update(input.Direction, input.Person, input.Amount, input.Currency, input.Date, input.DueDate, input.Note);
        return Task.FromResult(debt);
    }

    public Task<Debt> RepayDebtAsync(Guid id, decimal amount, CancellationToken cancellationToken = default)
    {
        var debt = Debts.Single(d => d.Id == id);
        debt.Repay(amount, Today);
        return Task.FromResult(debt);
    }

    public Task DeleteDebtAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Debts.RemoveAll(d => d.Id == id);
        return Task.CompletedTask;
    }

    public IReadOnlyList<CurrencyCode>? Currencies { get; set; }

    public Task SetCurrenciesAsync(IReadOnlyList<CurrencyCode> chosen, CancellationToken cancellationToken = default)
    {
        Currencies = [.. chosen.Distinct()];
        return Task.CompletedTask;
    }

    public Task SetTotalsCurrencyAsync(CurrencyCode? currency, CancellationToken cancellationToken = default)
    {
        TotalsCurrency = currency;
        return Task.CompletedTask;
    }

    private static DateOnly D(string date) => DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

    private DateTime Stamp() => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Transactions.Count);

    private Account Find(Guid id) => Accounts.Single(a => a.Id == id);

    private Transaction Record(Transaction t)
    {
        Transactions.Add(t);
        return t;
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
