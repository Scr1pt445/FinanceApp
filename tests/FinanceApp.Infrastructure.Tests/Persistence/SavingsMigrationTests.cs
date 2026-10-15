using FinanceApp.Domain;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinanceApp.Infrastructure.Tests.Persistence;

/// <summary>ADR 0010: savings recorded before envelopes existed become transfers into envelopes.</summary>
public class SavingsMigrationTests
{
    private const string BeforeEnvelopes = "20261014232802_InitialCreate";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavingTransactions_BecomeTransfersIntoEnvelopesNamedAfterTheirCategory()
    {
        await using var database = await TestDatabase.CreateAsync(seed: false, migrate: false);
        await using (var db = database.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(BeforeEnvelopes, Ct);

            // Old schema: written with SQL, the current model can no longer express it.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Accounts (Id, Name, Currency, IsHidden, SortOrder) VALUES
                    ('11111111-1111-1111-1111-111111111111', 'Карта', 'RUB', 0, 0);
                INSERT INTO Categories (Id, Name, Type, IsHidden, SortOrder) VALUES
                    ('22222222-2222-2222-2222-222222222221', 'Копилка', 'Saving', 0, 0),
                    ('22222222-2222-2222-2222-222222222222', 'Инвестиции', 'Saving', 0, 1),
                    ('22222222-2222-2222-2222-222222222223', 'Неиспользованная', 'Saving', 0, 2),
                    ('22222222-2222-2222-2222-222222222224', 'Еда', 'Expense', 0, 3);
                INSERT INTO Transactions (Id, Date, Type, Amount, Currency, ExchangeRate, CategoryId, AccountId, Note, CreatedAt, UpdatedAt) VALUES
                    ('33333333-3333-3333-3333-333333333331', '2025-10-19', 'Saving', '56638.0', 'RUB', '1.0', '22222222-2222-2222-2222-222222222221', '11111111-1111-1111-1111-111111111111', NULL, '2025-10-19 10:00:00', '2025-10-19 10:00:00'),
                    ('33333333-3333-3333-3333-333333333332', '2025-10-20', 'Saving', '31829.0', 'RUB', '1.0', '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', NULL, '2025-10-20 10:00:00', '2025-10-20 10:00:00'),
                    ('33333333-3333-3333-3333-333333333333', '2025-10-20', 'Saving', '6.04', 'RUB', '1.0', '22222222-2222-2222-2222-222222222221', '11111111-1111-1111-1111-111111111111', NULL, '2025-10-20 11:00:00', '2025-10-20 11:00:00'),
                    ('33333333-3333-3333-3333-333333333334', '2025-10-24', 'Expense', '981.0', 'RUB', '1.0', '22222222-2222-2222-2222-222222222224', '11111111-1111-1111-1111-111111111111', NULL, '2025-10-24 10:00:00', '2025-10-24 10:00:00');
                """, Ct);

            await db.Database.MigrateAsync(Ct);
        }

        await using var check = database.CreateContext();
        var envelopes = await check.Accounts.Where(a => a.Kind == AccountKind.Envelope).OrderBy(a => a.Name).ToListAsync(Ct);
        envelopes.Select(a => a.Name).ShouldBe(["Инвестиции", "Копилка"]);
        envelopes.ShouldAllBe(a => a.Currency == CurrencyCode.Rub && a.OpeningBalance == 0m);

        var piggyBank = envelopes.Single(a => a.Name == "Копилка");
        var transfers = await check.Transactions.Where(t => t.Type == TransactionType.Transfer).ToListAsync(Ct);
        transfers.Count.ShouldBe(3);
        transfers.ShouldAllBe(t => t.CategoryId == null && t.ToAmount == t.Amount);
        transfers.Where(t => t.ToAccountId == piggyBank.Id).Sum(t => t.Amount).ShouldBe(56644.04m);

        (await check.Transactions.SingleAsync(t => t.Type == TransactionType.Expense, Ct)).Amount.ShouldBe(981m);
        (await check.Categories.AnyAsync(c => c.Type == TransactionType.Saving, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task EmptyDatabase_MigratesCleanly()
    {
        await using var database = await TestDatabase.CreateAsync(seed: false);
        await using var db = database.CreateContext();

        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
    }
}
