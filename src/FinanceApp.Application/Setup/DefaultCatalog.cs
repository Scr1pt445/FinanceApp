using FinanceApp.Domain;

namespace FinanceApp.Application.Setup;

/// <summary>Starter categories and accounts for a fresh database (requirements, section 3.4).</summary>
public static class DefaultCatalog
{
    public static IReadOnlyList<(string Name, TransactionType Type)> Categories { get; } =
    [
        ("Зарплата", TransactionType.Income),
        ("Фриланс", TransactionType.Income),
        ("Репетиторство", TransactionType.Income),
        ("Пассивный доход", TransactionType.Income),
        ("Прочее", TransactionType.Income),

        ("Жильё", TransactionType.Expense),
        ("Еда", TransactionType.Expense),
        ("Транспорт", TransactionType.Expense),
        ("Здоровье", TransactionType.Expense),
        ("Развлечения", TransactionType.Expense),
        ("Одежда", TransactionType.Expense),
        ("Образование", TransactionType.Expense),
        ("Работа", TransactionType.Expense), // софт, подписки
        ("Связь", TransactionType.Expense),
        ("Прочее", TransactionType.Expense),

        ("Копилка", TransactionType.Saving),
        ("Инвестиции", TransactionType.Saving),
    ];

    public static IReadOnlyList<string> Accounts { get; } = ["Карта", "Наличные", "Вклад"];

    public static IEnumerable<Category> CreateCategories() =>
        Categories.Select((c, index) => new Category(Guid.CreateVersion7(), c.Name, c.Type, index));

    public static IEnumerable<Account> CreateAccounts(CurrencyCode baseCurrency) =>
        Accounts.Select((name, index) => new Account(Guid.CreateVersion7(), name, baseCurrency, index));
}
