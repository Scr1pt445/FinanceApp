using FinanceApp.Domain;
using FinanceApp.Infrastructure;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure.Tests;

/// <summary>A real SQLite file in a temp directory, migrated and seeded; deleted on dispose.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(FinancePaths paths) => Paths = paths;

    public FinancePaths Paths { get; }

    /// <param name="migrate">false — an empty file, for tests that apply migrations themselves.</param>
    public static async Task<TestDatabase> CreateAsync(bool seed = true, bool migrate = true)
    {
        var paths = new FinancePaths(Path.Combine(Path.GetTempPath(), "FinanceApp.Tests", Guid.NewGuid().ToString("N")));
        paths.EnsureDataDirectory();
        var database = new TestDatabase(paths);
        if (!migrate)
        {
            return database;
        }

        await using var db = database.CreateContext();
        if (seed)
        {
            await new DatabaseInitializer(db).InitializeAsync(CurrencyCode.Rub, TestContext.Current.CancellationToken);
        }
        else
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return database;
    }

    /// <summary>A fresh context — like a new unit of work in the app.</summary>
    public FinanceDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(Paths.ConnectionString).Options);

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Paths.DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail a test.
        }

        return ValueTask.CompletedTask;
    }
}
