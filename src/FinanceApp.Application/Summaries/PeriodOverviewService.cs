using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Summaries;

/// <summary>Everything a summary screen shows for one period.</summary>
/// <param name="Transactions">Transactions of the period, newest first.</param>
public sealed record PeriodOverview(
    Period Period,
    IReadOnlyList<Transaction> Transactions,
    PeriodSummary Summary,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Account> Accounts,
    CurrencyCode BaseCurrency,
    PeriodInsights Insights);

/// <summary>Loads a period together with the previous one (for comparison) in one go.</summary>
public sealed class PeriodOverviewService(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IAccountRepository accounts,
    ISettingsStore settings,
    TimeProvider timeProvider)
{
    public async Task<PeriodOverview> LoadAsync(Period period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        var from = period.Previous?.Start ?? period.Start;
        var loaded = period.Kind == PeriodKind.AllTime
            ? await transactions.ListAsync(cancellationToken: cancellationToken)
            : await transactions.ListAsync(from, period.End, cancellationToken);
        var allCategories = await categories.ListAsync(cancellationToken);
        var allAccounts = await accounts.ListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        var categoryMap = allCategories.ToDictionary(c => c.Id);
        var accountMap = allAccounts.ToDictionary(a => a.Id);
        var summary = SummaryCalculator.For(loaded, period, categoryMap, accountMap, today);
        var insights = PeriodInsights.For(loaded, period, categoryMap, accountMap, today);
        var ofPeriod = loaded
            .Where(t => period.Contains(t.Date))
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        return new PeriodOverview(period, ofPeriod, summary, allCategories, allAccounts, settings.Load().BaseCurrency, insights);
    }
}
