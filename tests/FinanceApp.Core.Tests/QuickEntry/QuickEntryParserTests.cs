using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Setup;
using FinanceApp.Domain;

namespace FinanceApp.Core.Tests.QuickEntry;

public class QuickEntryParserTests
{
    // Thursday.
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static readonly List<Category> Categories = DefaultCatalog.CreateCategories().ToList();

    private static readonly Account Card = new(Guid.NewGuid(), "Карта", CurrencyCode.Rub, 0);
    private static readonly Account Cash = new(Guid.NewGuid(), "Наличные", CurrencyCode.Rub, 1);
    private static readonly Account Deposit = new(Guid.NewGuid(), "Вклад", CurrencyCode.Rub, 2);
    private static readonly Account Car = new(Guid.NewGuid(), "Машина", CurrencyCode.Rub, 4, AccountKind.Envelope);
    private static readonly Account Vacation = new(Guid.NewGuid(), "Отдых", CurrencyCode.Rub, 5, AccountKind.Envelope);
    private static readonly Account Bike = new(Guid.NewGuid(), "Велосипед", CurrencyCode.Rub, 6, AccountKind.Envelope);
    private static readonly Account CardUsd = new(Guid.NewGuid(), "Карта USD", CurrencyCode.Usd, 3);

    private static Category Named(string name, TransactionType type) => Categories.Single(c => c.Name == name && c.Type == type);

    /// <summary>By default the last expense was paid in cash and the last saving went to the deposit.</summary>
    private static QuickEntryDraft Parse(string input, Dictionary<TransactionType, Guid>? lastAccounts = null, CategoryHints? hints = null) =>
        QuickEntryParser.Parse(input, Context(
            [Card, Cash, Deposit, CardUsd, Car, Vacation, Bike],
            lastAccounts ?? new() { [TransactionType.Expense] = Cash.Id, [TransactionType.Transfer] = Deposit.Id },
            hints));

    private static QuickEntryContext Context(
        IReadOnlyList<Account> accounts,
        Dictionary<TransactionType, Guid>? lastAccounts = null,
        CategoryHints? hints = null,
        IReadOnlyList<Category>? categories = null) =>
        new(categories ?? Categories, accounts, lastAccounts ?? [], CurrencyCode.Rub, hints ?? CategoryHints.Empty, Today);

    [Fact]
    public void BasicExpense()
    {
        var draft = Parse("350 еда кофе");

        draft.IsComplete.ShouldBeTrue();
        draft.Type.ShouldBe(TransactionType.Expense);
        draft.Amount.ShouldBe(350m);
        draft.Category.ShouldBe(Named("Еда", TransactionType.Expense));
        draft.CategoryFromHint.ShouldBeFalse();
        draft.Note.ShouldBe("кофе");
        draft.Date.ShouldBe(Today);
        draft.Account.ShouldBe(Cash);
        draft.AccountIsDefault.ShouldBeTrue();
    }

    [Theory]
    [InlineData("+50000 зарплата", TransactionType.Income, "Зарплата")]
    [InlineData("+ 50000 зарплата", TransactionType.Income, "Зарплата")]
    public void TypePrefix(string input, TransactionType type, string category)
    {
        var draft = Parse(input);

        draft.Type.ShouldBe(type);
        draft.Category.ShouldBe(Named(category, type));
        draft.IsComplete.ShouldBeTrue();
    }

    [Theory]
    [InlineData(">10000 машина", "Машина", null)]
    [InlineData("> 10000 отд на море", "Отдых", "на море")]
    [InlineData(">500 вел", "Велосипед", null)]
    public void GreaterThan_PutsMoneyIntoAnEnvelope(string input, string envelope, string? note)
    {
        var draft = Parse(input);

        draft.Type.ShouldBe(TransactionType.Transfer);
        draft.ToAccount.ShouldNotBeNull().Name.ShouldBe(envelope);
        draft.Category.ShouldBeNull();
        draft.Account.ShouldBe(Deposit); // the last regular account used for transfers
        draft.Note.ShouldBe(note);
        draft.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void GreaterThan_WithoutEnvelope_AsksForOne()
    {
        Parse(">10000").Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.MissingEnvelope);
        Parse(">10000 холодильник").Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.MissingEnvelope);
    }

    [Fact]
    public void GreaterThan_AmbiguousEnvelope_IsReported()
    {
        var draft = QuickEntryParser.Parse(">100 м", Context([Card, Car, new Account(Guid.NewGuid(), "Мебель", CurrencyCode.Rub, kind: AccountKind.Envelope)]));

        draft.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.AmbiguousEnvelope);
        draft.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void GreaterThan_WithoutAnyEnvelopes_SaysHowToCreateOne() =>
        QuickEntryParser.Parse(">100 машина", Context([Card])).Issues
            .ShouldContain(i => i.Kind == QuickEntryIssueKind.MissingEnvelope && i.Message.Contains("создайте"));

    [Fact]
    public void Envelopes_AreNeverTheDefaultAccount_ButCanBeSpentFromExplicitly()
    {
        Parse("350 еда", new() { [TransactionType.Expense] = Bike.Id }).Account.ShouldBe(Card);

        var bought = Parse("30000 транспорт велосипед @велосипед");
        bought.Account.ShouldBe(Bike);
        bought.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void TypeIsNeverInferredFromCategory()
    {
        var draft = Parse("50000 зарплата");

        draft.Type.ShouldBe(TransactionType.Expense);
        draft.Category.ShouldBeNull();
        draft.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.MissingCategory);
    }

    [Theory]
    [InlineData("350,50 еда", 350.50)]
    [InlineData("350.5 еда", 350.5)]
    [InlineData("0,01 еда", 0.01)]
    [InlineData("1 200 еда", 1200)]
    [InlineData("1 200 000,50 еда", 1200000.50)]
    [InlineData("12 345.6 еда", 12345.6)]
    public void Amount_CommaOrDot_ThousandsWithSpaces(string input, decimal expected)
    {
        var draft = Parse(input);

        draft.Amount.ShouldBe(expected);
        draft.Category.ShouldBe(Named("Еда", TransactionType.Expense));
    }

    [Fact]
    public void Amount_ThreeDigitWordAfterLongNumber_IsNotMerged() =>
        Parse("1200 еда 100").Note.ShouldBe("100");

    [Theory]
    [InlineData("еда 350")]
    [InlineData("-350 еда")]
    [InlineData("0 еда")]
    [InlineData("350,505 еда")]
    [InlineData("35о еда")]
    public void Amount_Invalid(string input)
    {
        var draft = Parse(input);

        draft.IsComplete.ShouldBeFalse();
        draft.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.InvalidAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    public void EmptyInput(string input) =>
        Parse(input).Issues.ShouldHaveSingleItem().Kind.ShouldBe(QuickEntryIssueKind.EmptyInput);

    [Theory]
    [InlineData("1200 тр такси", "Транспорт", "такси")]
    [InlineData("1200 ТРАНСПОРТ такси", "Транспорт", "такси")]
    [InlineData("500 раб подписка", "Работа", "подписка")]
    [InlineData("500 прочее", "Прочее", null)]
    public void Category_ByPrefix_OfTheTypeOnly(string input, string category, string? note)
    {
        var draft = Parse(input);

        draft.Category.ShouldBe(Named(category, TransactionType.Expense));
        draft.Note.ShouldBe(note);
    }

    [Fact]
    public void Category_MultiWordName()
    {
        var draft = Parse("+120 пассивный доход вклад");

        draft.Category.ShouldBe(Named("Пассивный доход", TransactionType.Income));
        draft.Note.ShouldBe("вклад");
    }

    [Fact]
    public void Category_AmbiguousPrefix_IsReported()
    {
        var draft = Parse("500 р кино");

        draft.Category.ShouldBeNull();
        var issue = draft.Issues.ShouldHaveSingleItem();
        issue.Kind.ShouldBe(QuickEntryIssueKind.AmbiguousCategory);
        issue.Message.ShouldContain("Развлечения");
        issue.Message.ShouldContain("Работа");
    }

    [Fact]
    public void Category_FromNoteHistory_WhenNotNamed()
    {
        var food = Named("Еда", TransactionType.Expense);
        var hints = CategoryHints.Build([History("кофе с собой", food)]);

        var draft = Parse("350 кофе", hints: hints);

        draft.Category.ShouldBe(food);
        draft.CategoryFromHint.ShouldBeTrue();
        draft.Note.ShouldBe("кофе");
        draft.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void Category_HiddenIsNotOffered()
    {
        var hidden = new Category(Guid.NewGuid(), "Хобби", TransactionType.Expense);
        hidden.Hide();
        var context = Context([Card], categories: [.. Categories, hidden]);

        QuickEntryParser.Parse("100 хобби", context).Category.ShouldBeNull();
    }

    [Theory]
    [InlineData("1200 тр такси вчера", "2026-10-14")]
    [InlineData("1200 тр позавчера такси", "2026-10-13")]
    [InlineData("1200 тр такси сегодня", "2026-10-15")]
    [InlineData("1200 тр такси пн", "2026-10-12")]
    [InlineData("1200 тр такси чт", "2026-10-15")]
    [InlineData("1200 тр такси пятницу", "2026-10-09")]
    [InlineData("1200 тр такси 12.10", "2026-10-12")]
    [InlineData("1200 тр такси 1.3", "2026-03-01")]
    [InlineData("1200 тр такси 20.12", "2025-12-20")]
    [InlineData("1200 тр такси 20.12.2024", "2024-12-20")]
    [InlineData("1200 тр такси 20.12.24", "2024-12-20")]
    public void Date(string input, string expected)
    {
        var draft = Parse(input);

        draft.Date.ShouldBe(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        draft.Note.ShouldBe("такси");
    }

    [Fact]
    public void Date_InvalidLooksLikeNote()
    {
        var draft = Parse("1200 тр такси 31.02");

        draft.Date.ShouldBe(Today);
        draft.Note.ShouldBe("такси 31.02");
    }

    [Theory]
    [InlineData("350 еда кофе @нал", "Наличные", "кофе")]
    [InlineData("350 еда @вклад кофе", "Вклад", "кофе")]
    [InlineData("350 еда кофе @карта", "Карта", "кофе")]
    [InlineData("20 еда ужин @карта usd", "Карта USD", "ужин")]
    [InlineData("20 еда @карта usd ужин", "Карта USD", "ужин")]
    [InlineData("350 еда @КАРТА", "Карта", null)]
    public void Account_ByPrefix_ExactWins(string input, string account, string? note)
    {
        var draft = Parse(input);

        draft.Account.ShouldNotBeNull().Name.ShouldBe(account);
        draft.AccountIsDefault.ShouldBeFalse();
        draft.Note.ShouldBe(note);
        draft.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void Account_Ambiguous_IsReported()
    {
        var context = Context([Card, CardUsd, new Account(Guid.NewGuid(), "Кошелёк", CurrencyCode.Rub)]);

        var draft = QuickEntryParser.Parse("350 еда @к", context);

        draft.Account.ShouldBeNull();
        draft.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.AmbiguousAccount);
    }

    [Theory]
    [InlineData("350 еда @банк")]
    [InlineData("350 еда @")]
    public void Account_Unknown_IsReported(string input)
    {
        var draft = Parse(input);

        draft.Account.ShouldBeNull();
        draft.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.UnknownAccount);
    }

    [Fact]
    public void DefaultAccount_IsLastUsedForTheSameType()
    {
        Parse("350 еда").Account.ShouldBe(Cash);
        Parse(">1000 машина").Account.ShouldBe(Deposit);
    }

    [Fact]
    public void DefaultAccount_TypeWithoutHistory_IsFirstBaseCurrencyAccount()
    {
        var draft = Parse("+50000 зарплата");

        draft.Account.ShouldBe(Card);
        draft.AccountIsDefault.ShouldBeTrue();
    }

    [Fact]
    public void DefaultAccount_IsNeverForeignCurrency()
    {
        var draft = Parse("500 развл кино", new() { [TransactionType.Expense] = CardUsd.Id });

        draft.Account.ShouldBe(Card);
        draft.Account!.Currency.ShouldBe(CurrencyCode.Rub);
    }

    [Fact]
    public void DefaultAccount_SkipsHiddenLastAccount()
    {
        var hiddenCash = new Account(Guid.NewGuid(), "Старая карта", CurrencyCode.Rub);
        hiddenCash.Hide();

        var draft = QuickEntryParser.Parse("350 еда", Context([hiddenCash, Card], new() { [TransactionType.Expense] = hiddenCash.Id }));

        draft.Account.ShouldBe(Card);
    }

    [Fact]
    public void ForeignAccount_WorksExplicitly_EvenWithoutBaseCurrencyAccounts()
    {
        var context = Context([CardUsd]);

        QuickEntryParser.Parse("20 еда @карта", context).Account.ShouldBe(CardUsd);
        QuickEntryParser.Parse("20 еда", context).Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.NoAccount);
    }

    [Fact]
    public void NoAccounts_IsReported()
    {
        var draft = QuickEntryParser.Parse("350 еда", Context([]));

        draft.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.NoAccount);
    }

    [Fact]
    public void Everything_Together()
    {
        var draft = Parse(">5000,5 отдых на море 01.10 @нал");

        draft.Type.ShouldBe(TransactionType.Transfer);
        draft.Amount.ShouldBe(5000.5m);
        draft.ToAccount.ShouldBe(Vacation);
        draft.Note.ShouldBe("на море");
        draft.Date.ShouldBe(new DateOnly(2026, 10, 1));
        draft.Account.ShouldBe(Cash);
        draft.IsComplete.ShouldBeTrue();
    }

    private static Transaction History(string note, Category category, int minutesAgo = 0) =>
        Transaction.Create(Today, category.Type, 1m, 1m, category, Card, note, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo));

    [Theory]
    [InlineData("20 eur еда ужин", "EUR")]
    [InlineData("20 EUR еда ужин", "EUR")]
    [InlineData("20€ еда ужин", "EUR")]
    [InlineData("€20 еда ужин", "EUR")]
    [InlineData("20eur еда ужин", "EUR")]
    [InlineData("20 евро еда ужин", "EUR")]
    [InlineData("20 $ еда ужин", "USD")]
    [InlineData("20 gel еда ужин", "GEL")]
    public void ReceiptCurrency_AfterTheAmount_IsKept_ForAnAccountInAnotherCurrency(string line, string currency)
    {
        var draft = Parse(line);

        draft.Amount.ShouldBe(20m);
        draft.PaidCurrency.ShouldBe(CurrencyCode.Parse(currency));
        draft.Account!.Currency.ShouldBe(CurrencyCode.Rub);
        draft.Category.ShouldBe(Named("Еда", TransactionType.Expense));
        draft.Note.ShouldBe("ужин");
        draft.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void ReceiptCurrency_SameAsTheAccount_NeedsNoConversion()
    {
        Parse("350 руб еда").PaidCurrency.ShouldBeNull();
        QuickEntryParser.Parse("20 usd еда @карта usd", Context([Card, CardUsd])).PaidCurrency.ShouldBeNull();
    }

    [Fact]
    public void ThreeLettersThatAreNotACurrency_StayInTheNote()
    {
        var draft = Parse("350 еда bar");

        draft.PaidCurrency.ShouldBeNull();
        draft.Note.ShouldBe("bar");
    }

    [Fact]
    public void ReceiptCurrency_WorksWithThousands()
    {
        var draft = Parse("1 200 thb еда");

        draft.Amount.ShouldBe(1200m);
        draft.PaidCurrency.ShouldBe(CurrencyCode.Parse("THB"));
    }

    [Fact]
    public void EnvelopeInAnotherCurrency_IsAnIssue()
    {
        var draft = Parse(">50 usd машина");

        draft.Issues.ShouldContain(i => i.Kind == QuickEntryIssueKind.ForeignTransfer);
        draft.IsComplete.ShouldBeFalse();
    }
}
