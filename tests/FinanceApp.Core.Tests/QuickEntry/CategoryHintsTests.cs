using FinanceApp.Application.QuickEntry;
using FinanceApp.Core.Tests.Domain;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.QuickEntry;

public class CategoryHintsTests
{
    private static readonly Category Transport = new(Guid.NewGuid(), "Транспорт", TransactionType.Expense);

    private static Transaction Spent(string? note, Category category, int minute) =>
        Transaction.Create(new DateOnly(2026, 10, 1), category.Type, 1m, 1m, category, TestData.Card, note, TestData.Now.AddMinutes(minute));

    [Fact]
    public void MostFrequentCategoryWins()
    {
        var hints = CategoryHints.Build(
        [
            Spent("кофе", TestData.Food, 1),
            Spent("кофе", TestData.Food, 2),
            Spent("кофе на заправке", Transport, 3),
        ]);

        hints.Suggest(TransactionType.Expense, ["кофе"]).ShouldBe(TestData.Food.Id);
    }

    [Fact]
    public void OnTie_MostRecentWins()
    {
        var hints = CategoryHints.Build([Spent("кофе", TestData.Food, 1), Spent("кофе", Transport, 2)]);

        hints.Suggest(TransactionType.Expense, ["кофе"]).ShouldBe(Transport.Id);
    }

    [Fact]
    public void WordsAreNormalized_AndShortWordsIgnored()
    {
        var hints = CategoryHints.Build([Spent("Кофе, круассан! в", TestData.Food, 1)]);

        hints.Suggest(TransactionType.Expense, ["КРУАССАН."]).ShouldBe(TestData.Food.Id);
        hints.Suggest(TransactionType.Expense, ["в"]).ShouldBeNull();
    }

    [Fact]
    public void HintsArePerType()
    {
        var hints = CategoryHints.Build([Spent("кофе", TestData.Food, 1)]);

        hints.Suggest(TransactionType.Income, ["кофе"]).ShouldBeNull();
    }

    [Fact]
    public void FirstHintedWordWins()
    {
        var hints = CategoryHints.Build([Spent("такси", Transport, 1), Spent("кофе", TestData.Food, 2)]);

        hints.Suggest(TransactionType.Expense, ["вкусный", "кофе", "такси"]).ShouldBe(TestData.Food.Id);
    }

    [Fact]
    public void NoNotes_NoHints() =>
        CategoryHints.Build([Spent(null, TestData.Food, 1)]).Suggest(TransactionType.Expense, ["кофе"]).ShouldBeNull();
}
