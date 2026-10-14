using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Csv;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;

namespace FinanceApp.Application.Import;

/// <param name="DefaultAccountName">Account for rows with an empty <c>account</c> (the 2025 archive has none).</param>
/// <param name="DryRun">Validate and report, change nothing.</param>
public sealed record ImportOptions(string DefaultAccountName = "Карта", bool DryRun = false);

public sealed record ImportReport(
    int RowsInFile,
    int Added,
    int Updated,
    int SkippedDuplicates,
    IReadOnlyList<string> CreatedCategories,
    IReadOnlyList<string> CreatedAccounts,
    IReadOnlyList<CsvRowError> Errors,
    IReadOnlyDictionary<YearMonth, MonthTotals> FileTotals,
    bool DryRun)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Imports the transactions CSV (the 2025 archive or our own export). All or nothing: any bad row
/// cancels the whole import. Safe to repeat: rows with an <c>id</c> are upserted, rows without one are
/// matched against existing transactions by content, counting duplicates (two identical coffees stay two).
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
        var fileTotals = SummaryCalculator.ByMonth(read.Rows);
        if (read.HasErrors)
        {
            return Failed(read.Rows.Count, read.Errors, fileTotals, options);
        }

        var plan = await PlanAsync(read.Rows, options, cancellationToken);
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

    private static ImportReport Failed(int rows, IReadOnlyList<CsvRowError> errors, IReadOnlyDictionary<YearMonth, MonthTotals> totals, ImportOptions options) =>
        new(rows, 0, 0, 0, [], [], errors, totals, options.DryRun);

    private async Task<ImportPlan> PlanAsync(IReadOnlyList<TransactionCsvRow> rows, ImportOptions options, CancellationToken cancellationToken)
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
            .GroupBy(t => Fingerprint.Of(t.Date, t.Type, t.BaseAmount, categoryNames.GetValueOrDefault(t.CategoryId), accountNames.GetValueOrDefault(t.AccountId), t.Note))
            .ToDictionary(g => g.Key, g => g.Count());

        var nextCategoryOrder = allCategories.Count == 0 ? 0 : allCategories.Max(c => c.SortOrder) + 1;
        var nextAccountOrder = allAccounts.Count == 0 ? 0 : allAccounts.Max(a => a.SortOrder) + 1;
        var idsInFile = new HashSet<Guid>();

        foreach (var row in rows)
        {
            var categoryName = row.Category ?? "Прочее";
            var category = allCategories.FirstOrDefault(c => c.Type == row.Type && NamesEqual(c.Name, categoryName));
            if (category is null)
            {
                category = new Category(Guid.CreateVersion7(), categoryName, row.Type, nextCategoryOrder++);
                allCategories.Add(category);
                plan.NewCategories.Add(category);
            }

            var rowCurrency = row.Currency ?? baseCurrency;
            var accountName = row.Account ?? options.DefaultAccountName;
            var account = allAccounts.FirstOrDefault(a => NamesEqual(a.Name, accountName));
            if (account is null)
            {
                account = new Account(Guid.CreateVersion7(), accountName, rowCurrency, nextAccountOrder++);
                allAccounts.Add(account);
                plan.NewAccounts.Add(account);
            }
            else if (account.Currency != rowCurrency)
            {
                plan.Errors.Add(new CsvRowError(row.LineNumber, $"валюта операции {rowCurrency} не совпадает с валютой счёта «{account.Name}» ({account.Currency})"));
                continue;
            }

            if (!TryGetAmountAndRate(row, rowCurrency, baseCurrency, out var amount, out var rate, out var amountError))
            {
                plan.Errors.Add(new CsvRowError(row.LineNumber, amountError));
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
                    if (!SameContent(current, row, amount, category, account))
                    {
                        var (c, a) = (category, account);
                        plan.Apply.Add(() => current.Update(row.Date, row.Type, amount, rate, c, a, row.Note, now));
                        plan.Updated++;
                    }
                    else
                    {
                        plan.Skipped++;
                    }

                    continue;
                }
            }
            else
            {
                var fingerprint = Fingerprint.Of(row.Date, row.Type, row.Amount, category.Name, account.Name, row.Note);
                if (stored.TryGetValue(fingerprint, out var count) && count > 0)
                {
                    stored[fingerprint] = count - 1;
                    plan.Skipped++;
                    continue;
                }
            }

            try
            {
                var transaction = Transaction.Create(row.Date, row.Type, amount, rate, category, account, row.Note, now, row.Id);
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
    private static bool SameContent(Transaction t, TransactionCsvRow row, decimal amount, Category category, Account account) =>
        t.Date == row.Date && t.Type == row.Type && t.Amount == amount && t.BaseAmount == row.Amount
        && t.CategoryId == category.Id && t.AccountId == account.Id && t.Note == row.Note;

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
