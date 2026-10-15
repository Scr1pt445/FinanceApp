using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Csv;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Application.Import;

/// <param name="DefaultAccountName">Account for rows with an empty <c>account</c> (the 2025 archive has none).</param>
/// <param name="DryRun">Validate and report, change nothing.</param>
/// <param name="AccountsCsvText">Optional <c>accounts.csv</c> from the same export: missing accounts and envelopes
/// are created with their kind, currency and goal before the transactions are imported.</param>
public sealed record ImportOptions(string DefaultAccountName = "Карта", bool DryRun = false, string? AccountsCsvText = null);

public sealed record ImportReport(
    int RowsInFile,
    int Added,
    int Updated,
    int SkippedDuplicates,
    IReadOnlyList<string> CreatedCategories,
    IReadOnlyList<string> CreatedAccounts,
    IReadOnlyList<CsvRowError> Errors,
    IReadOnlyDictionary<YearMonth, PeriodTotals> FileTotals,
    bool DryRun)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Imports the transactions CSV (the 2025 archive or our own export). All or nothing: any bad row
/// cancels the whole import. Safe to repeat: rows with an <c>id</c> are upserted, rows without one are
/// matched against existing transactions by content, counting duplicates (two identical coffees stay two).
/// Legacy <c>saving</c> rows become transfers into an envelope named after their category (ADR 0010).
/// </summary>
public sealed class CsvImportService(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    ISettingsStore settings,
    TimeProvider timeProvider,
    DataChangedNotifier notifier)
{
    /// <summary>Precision of rates derived from <c>amount / original_amount</c>; keeps the base amount exact.</summary>
    private const int ImportedRateDecimals = 10;

    public async Task<ImportReport> ImportAsync(string csvText, ImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        ArgumentNullException.ThrowIfNull(options);

        var read = TransactionCsvReader.ReadText(csvText);
        var accountsFile = options.AccountsCsvText is { } accountsText ? AccountsCsv.Read(accountsText) : [];
        var envelopeNames = (await accounts.ListAsync(cancellationToken))
            .Where(a => a.IsEnvelope)
            .Select(a => a.Name)
            .Concat(accountsFile.Where(a => a.Kind == AccountKind.Envelope).Select(a => a.Name))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var fileTotals = SummaryCalculator.ByMonth(read.Rows, name => name is not null && envelopeNames.Contains(name));
        if (read.HasErrors)
        {
            return Failed(read.Rows.Count, read.Errors, fileTotals, options);
        }

        var plan = await PlanAsync(read.Rows, accountsFile, options, cancellationToken);
        if (plan.Errors.Count > 0)
        {
            return Failed(read.Rows.Count, plan.Errors, fileTotals, options);
        }

        if (!options.DryRun)
        {
            await unitOfWork.InTransactionAsync(
                _ =>
                {
                    plan.NewCategories.ForEach(categories.Add);
                    plan.NewAccounts.ForEach(accounts.Add);
                    plan.Apply.ForEach(apply => apply());
                    return Task.FromResult(true);
                },
                cancellationToken);

            if (plan.Added + plan.Updated > 0 || plan.NewCategories.Count + plan.NewAccounts.Count > 0)
            {
                notifier.NotifyChanged();
            }
        }

        return new ImportReport(
            read.Rows.Count,
            plan.Added,
            plan.Updated,
            plan.Skipped,
            plan.NewCategories.Select(c => c.ToString()).ToList(),
            plan.NewAccounts.Select(a => a.ToString()).ToList(),
            [],
            fileTotals,
            options.DryRun);
    }

    private static ImportReport Failed(int rows, IReadOnlyList<CsvRowError> errors, IReadOnlyDictionary<YearMonth, PeriodTotals> totals, ImportOptions options) =>
        new(rows, 0, 0, 0, [], [], errors, totals, options.DryRun);

    private async Task<ImportPlan> PlanAsync(
        IReadOnlyList<TransactionCsvRow> rows,
        IReadOnlyList<AccountCsvRow> accountsFile,
        ImportOptions options,
        CancellationToken cancellationToken)
    {
        var baseCurrency = settings.Load().BaseCurrency;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var plan = new ImportPlan();

        var allCategories = (await categories.ListAsync(cancellationToken)).ToList();
        var allAccounts = (await accounts.ListAsync(cancellationToken)).ToList();
        var existing = await transactions.ListAsync(cancellationToken: cancellationToken);
        var existingById = existing.ToDictionary(t => t.Id);

        // How many identical transactions are already stored, per content fingerprint.
        var categoryNames = allCategories.ToDictionary(c => c.Id, c => c.Name);
        var accountNames = allAccounts.ToDictionary(a => a.Id, a => a.Name);
        var stored = existing
            .GroupBy(t => Fingerprint.Of(
                t.Date,
                t.Type,
                t.BaseAmount,
                t.ToAccountId is { } toId ? "→" + accountNames.GetValueOrDefault(toId)
                    : t.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
                accountNames.GetValueOrDefault(t.AccountId),
                t.Note))
            .ToDictionary(g => g.Key, g => g.Count());

        var nextCategoryOrder = allCategories.Count == 0 ? 0 : allCategories.Max(c => c.SortOrder) + 1;
        var nextAccountOrder = allAccounts.Count == 0 ? 0 : allAccounts.Max(a => a.SortOrder) + 1;
        var idsInFile = new HashSet<Guid>();

        Account GetOrCreateAccount(string name, AccountKind kind, CurrencyCode currency)
        {
            var account = allAccounts.FirstOrDefault(a => NamesEqual(a.Name, name));
            if (account is null)
            {
                account = new Account(Guid.CreateVersion7(), name, currency, nextAccountOrder++, kind);
                allAccounts.Add(account);
                plan.NewAccounts.Add(account);
            }

            return account;
        }

        // accounts.csv first, so transfers land in envelopes of the right kind, currency and goal.
        foreach (var described in accountsFile)
        {
            if (allAccounts.Any(a => NamesEqual(a.Name, described.Name)))
            {
                continue;
            }

            var account = GetOrCreateAccount(described.Name, described.Kind, described.Currency);
            if (described.GoalAmount is { } goal && account.IsEnvelope)
            {
                account.SetGoal(goal, described.GoalDate);
            }

            if (described.Hidden)
            {
                account.Hide();
            }
        }

        foreach (var row in rows)
        {
            var rowCurrency = row.Currency ?? baseCurrency;
            var account = GetOrCreateAccount(row.Account ?? options.DefaultAccountName, AccountKind.Regular, rowCurrency);
            if (account.Currency != rowCurrency)
            {
                plan.Errors.Add(new CsvRowError(row.LineNumber, $"валюта операции {rowCurrency} не совпадает с валютой счёта «{account.Name}» ({account.Currency})"));
                continue;
            }

            if (!TryGetAmountAndRate(row, rowCurrency, baseCurrency, out var amount, out var rate, out var amountError))
            {
                plan.Errors.Add(new CsvRowError(row.LineNumber, amountError));
                continue;
            }

            // Income and expense need a category; transfers (and legacy savings) need a destination.
            Category? category = null;
            Account? destination = null;
            var type = row.Type;
            if (type is TransactionType.Saving)
            {
                type = TransactionType.Transfer;
                destination = GetOrCreateAccount(row.Category ?? "Копилка", AccountKind.Envelope, rowCurrency);
            }
            else if (type is TransactionType.Transfer)
            {
                destination = GetOrCreateAccount(row.ToAccount!, AccountKind.Regular, rowCurrency);
            }
            else
            {
                var categoryName = row.Category ?? "Прочее";
                category = allCategories.FirstOrDefault(c => c.Type == type && NamesEqual(c.Name, categoryName));
                if (category is null)
                {
                    category = new Category(Guid.CreateVersion7(), categoryName, type, nextCategoryOrder++);
                    allCategories.Add(category);
                    plan.NewCategories.Add(category);
                }
            }

            var toAmount = destination is null ? null : row.ToAmount ?? (destination.Currency == account.Currency ? amount : null);
            if (destination is not null && toAmount is null)
            {
                plan.Errors.Add(new CsvRowError(row.LineNumber, $"для перевода в {destination.Currency} нужна колонка to_amount"));
                continue;
            }

            if (row.Id is { } id)
            {
                if (!idsInFile.Add(id))
                {
                    plan.Errors.Add(new CsvRowError(row.LineNumber, $"id {id} встречается в файле повторно"));
                    continue;
                }

                if (existingById.TryGetValue(id, out var current))
                {
                    if (SameContent(current, row, type, amount, category, account, destination, toAmount))
                    {
                        plan.Skipped++;
                        continue;
                    }

                    var (c, a, d) = (category, account, destination);
                    plan.Apply.Add(() =>
                    {
                        if (d is null)
                        {
                            current.Update(row.Date, type, amount, rate, c!, a, row.Note, now);
                        }
                        else
                        {
                            current.UpdateTransfer(row.Date, amount, rate, a, d, toAmount, row.Note, now);
                        }
                    });
                    plan.Updated++;
                    continue;
                }
            }
            else
            {
                var what = destination is null ? category!.Name : "→" + destination.Name;
                var fingerprint = Fingerprint.Of(row.Date, type, row.Amount, what, account.Name, row.Note);
                if (stored.TryGetValue(fingerprint, out var count) && count > 0)
                {
                    stored[fingerprint] = count - 1;
                    plan.Skipped++;
                    continue;
                }
            }

            try
            {
                var transaction = destination is null
                    ? Transaction.Create(row.Date, type, amount, rate, category!, account, row.Note, now, row.Id)
                    : Transaction.CreateTransfer(row.Date, amount, rate, account, destination, toAmount, row.Note, now, row.Id);
                plan.Apply.Add(() => transactions.Add(transaction));
                plan.Added++;
            }
            catch (DomainException ex)
            {
                plan.Errors.Add(new CsvRowError(row.LineNumber, ex.Message));
            }
        }

        return plan;
    }

    private static bool TryGetAmountAndRate(
        TransactionCsvRow row,
        CurrencyCode rowCurrency,
        CurrencyCode baseCurrency,
        out decimal amount,
        out decimal rate,
        out string error)
    {
        error = string.Empty;
        if (rowCurrency == baseCurrency)
        {
            amount = row.Amount;
            rate = 1m;
            if (row.OriginalAmount is { } original && original != row.Amount)
            {
                error = $"для базовой валюты {baseCurrency} original_amount должна совпадать с amount";
                return false;
            }

            return true;
        }

        if (row.OriginalAmount is not { } originalAmount)
        {
            (amount, rate) = (0, 0);
            error = $"для валюты {rowCurrency} нужна колонка original_amount";
            return false;
        }

        amount = originalAmount;
        rate = decimal.Round(row.Amount / originalAmount, ImportedRateDecimals, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Same visible content; a rate that differs only beyond kopecks of the base amount is not a change.</summary>
    private static bool SameContent(
        Transaction t, TransactionCsvRow row, TransactionType type, decimal amount, Category? category, Account account, Account? destination, decimal? toAmount) =>
        t.Date == row.Date && t.Type == type && t.Amount == amount && t.BaseAmount == row.Amount
        && t.CategoryId == category?.Id && t.AccountId == account.Id && t.ToAccountId == destination?.Id
        && t.ToAmount == toAmount && t.Note == row.Note;

    private static bool NamesEqual(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private readonly record struct Fingerprint(DateOnly Date, TransactionType Type, decimal BaseAmount, string Category, string Account, string Note)
    {
        public static Fingerprint Of(DateOnly date, TransactionType type, decimal baseAmount, string? category, string? account, string? note) =>
            new(date, type, decimal.Round(baseAmount, 2), Normalize(category), Normalize(account), note?.Trim() ?? string.Empty);

        private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    }

    private sealed class ImportPlan
    {
        public List<Category> NewCategories { get; } = [];

        public List<Account> NewAccounts { get; } = [];

        public List<Action> Apply { get; } = [];

        public List<CsvRowError> Errors { get; } = [];

        public int Added { get; set; }

        public int Updated { get; set; }

        public int Skipped { get; set; }
    }
}
