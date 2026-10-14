using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.Domain;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);

    public static Category Food { get; } = new(Guid.NewGuid(), "Еда", TransactionType.Expense);

    public static Category Salary { get; } = new(Guid.NewGuid(), "Зарплата", TransactionType.Income);

    public static Category PiggyBank { get; } = new(Guid.NewGuid(), "Копилка", TransactionType.Saving);

    public static Account Card { get; } = new(Guid.NewGuid(), "Карта", CurrencyCode.Rub);

    public static Account UsdCard { get; } = new(Guid.NewGuid(), "Карта USD", CurrencyCode.Usd);

    public static Transaction Expense(
        decimal amount = 350m,
        string date = "2026-10-07",
        string? note = "кофе",
        Category? category = null,
        Account? account = null,
        decimal rate = 1m) =>
        Transaction.Create(
            DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            TransactionType.Expense,
            amount,
            rate,
            category ?? Food,
            account ?? Card,
            note,
            Now);
}
