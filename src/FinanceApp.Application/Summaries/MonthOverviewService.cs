using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Application.Summaries;

/// <summary>Everything the main window shows for one month.</summary>
/// <param name="Transactions">Transactions of the month, newest first.</param>
public sealed record MonthOverview(
    YearMonth Month,
    IReadOnlyList<Transaction> Transactions,
    MonthSummary Summary,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Account> Accounts,
    CurrencyCode BaseCurrency);

/// <summary>Loads a month together with the previous one (for comparison) in one go.</summary>
public sealed class MonthOverviewService(
    ITransactionRepository transactions,
    ICategoryRepository categories,
    IAccountRepository accounts,
    ISettingsStore settings)
{
    public async Task<MonthOverview> LoadAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var twoMonths = await transactions.ListAsync(month.Previous.FirstDay, month.LastDay, cancellationToken);
        var allCategories = await categories.ListAsync(cancellationToken);

        var summary = SummaryCalculator.ForMonth(twoMonths, month, allCategories.ToDictionary(c => c.Id));
        var ofMonth = twoMonths
            .Where(t => month.Contains(t.Date))
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        return new MonthOverview(
            month,
            ofMonth,
            summary,
            allCategories,
            await accounts.ListAsync(cancellationToken),
            settings.Load().BaseCurrency);
    }
}
