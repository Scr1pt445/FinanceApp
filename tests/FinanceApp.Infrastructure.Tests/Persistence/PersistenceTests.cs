using FinanceApp.Application.Setup;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure.Tests.Persistence;

public class PersistenceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_SeedsStarterCatalogOnce()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var db = database.CreateContext())
        {
            (await new DatabaseInitializer(db).InitializeAsync(CurrencyCode.Rub, Ct)).ShouldBeFalse();
        }

        await using var check = database.CreateContext();
        (await check.Categories.CountAsync(Ct)).ShouldBe(DefaultCatalog.Categories.Count);
        (await check.Accounts.Select(a => a.Name).ToListAsync(Ct)).ShouldBe(["Карта", "Наличные", "Вклад"], ignoreOrder: true);
        (await check.Accounts.AllAsync(a => a.Currency == CurrencyCode.Rub, Ct)).ShouldBeTrue();
        (await check.Categories.CountAsync(c => c.Name == "Прочее", Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Transaction_RoundTripsAllFieldsExactly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var createdAt = new DateTime(2026, 10, 15, 9, 30, 15, 123, DateTimeKind.Utc);
        Guid id;

        await using (var db = database.CreateContext())
        {
            var category = await db.Categories.FirstAsync(c => c.Name == "Еда", Ct);
            var account = new Account(Guid.NewGuid(), "Карта USD", CurrencyCode.Usd);
            db.Accounts.Add(account);
            var transaction = Transaction.Create(
                new DateOnly(2026, 10, 7), TransactionType.Expense, 1234567.89m, 92.456789m, category, account, "ужин, \"Ромашка\"", createdAt);
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(Ct);
            id = transaction.Id;
        }

        await using var check = database.CreateContext();
        var loaded = await check.Transactions.SingleAsync(t => t.Id == id, Ct);
        loaded.Date.ShouldBe(new DateOnly(2026, 10, 7));
        loaded.Type.ShouldBe(TransactionType.Expense);
        loaded.Amount.ShouldBe(1234567.89m);
        loaded.ExchangeRate.ShouldBe(92.456789m);
        loaded.Currency.ShouldBe(CurrencyCode.Usd);
        loaded.Note.ShouldBe("ужин, \"Ромашка\"");
        loaded.CreatedAt.ShouldBe(createdAt);
        loaded.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task ListAsync_FiltersByInclusiveDateRange_OrderedByDate()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var db = database.CreateContext())
        {
            var food = await db.Categories.FirstAsync(c => c.Name == "Еда", Ct);
            var card = await db.Accounts.FirstAsync(a => a.Name == "Карта", Ct);
            foreach (var day in new[] { "2026-11-01", "2026-10-31", "2026-09-30", "2026-10-01" })
            {
                db.Transactions.Add(Transaction.Create(
                    DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), TransactionType.Expense, 1m, 1m, food, card, day, DateTime.UtcNow));
            }

            await db.SaveChangesAsync(Ct);
        }

        await using var check = database.CreateContext();
        var repository = new EfTransactionRepository(check);
        var october = await repository.ListAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Ct);

        october.Select(t => t.Note).ShouldBe(["2026-10-01", "2026-10-31"]);
        (await repository.ListAsync(cancellationToken: Ct)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task CategoryNameAndType_AreUnique()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = database.CreateContext();

        db.Categories.Add(new Category(Guid.NewGuid(), "Еда", TransactionType.Expense));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task InTransactionAsync_RollsBackEverythingOnError()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var db = database.CreateContext())
        {
            var unitOfWork = new EfUnitOfWork(db);
            await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.InTransactionAsync<int>(
                async ct =>
                {
                    db.Categories.Add(new Category(Guid.NewGuid(), "Новая", TransactionType.Expense));
                    await db.SaveChangesAsync(ct);
                    throw new InvalidOperationException("boom");
                },
                Ct));
        }

        await using var check = database.CreateContext();
        (await check.Categories.AnyAsync(c => c.Name == "Новая", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task ExistingIdsAsync_ReturnsOnlyStoredIds()
    {
        await using var database = await TestDatabase.CreateAsync();
        Guid storedId;
        await using (var db = database.CreateContext())
        {
            var food = await db.Categories.FirstAsync(c => c.Name == "Еда", Ct);
            var card = await db.Accounts.FirstAsync(a => a.Name == "Карта", Ct);
            var transaction = Transaction.Create(new DateOnly(2026, 10, 7), TransactionType.Expense, 1m, 1m, food, card, null, DateTime.UtcNow);
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(Ct);
            storedId = transaction.Id;
        }

        await using var check = database.CreateContext();
        var existing = await new EfTransactionRepository(check).ExistingIdsAsync([storedId, Guid.NewGuid()], Ct);

        existing.ShouldBe([storedId]);
    }
}
