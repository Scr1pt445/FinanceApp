using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Settings;
using FinanceApp.Infrastructure.Backups;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Infrastructure.Tests.Backups;

public class DatabaseBackupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (SqliteDatabaseBackup Backup, FakeTimeProvider Time) Create(TestApp app)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return (new SqliteDatabaseBackup(app.Paths, app.Get<ISettingsStore>(), time), time);
    }

    private static long CountTransactions(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Transactions";
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task Copy_IsAWorkingDatabase_WithTheSameData()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        await using (var scope = app.Scope())
        {
            await scope.Get<QuickEntryService>().SaveAsync("350 еда кофе", Ct);
            await scope.Get<QuickEntryService>().SaveAsync("+50000 зарплата", Ct);
        }

        var (backup, _) = Create(app);
        var copy = await backup.CreateAsync(Ct);

        Path.GetFileName(copy.Path).ShouldBe("finance-2026-10-16_093000.db");
        copy.SizeBytes.ShouldBeGreaterThan(0);
        CountTransactions(copy.Path).ShouldBe(2);
        Directory.GetFiles(backup.Folder, "*.partial").ShouldBeEmpty();
    }

    [Fact]
    public async Task Daily_OnlyOncePerDay()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var (backup, time) = Create(app);

        (await backup.EnsureDailyAsync(Ct)).ShouldNotBeNull();
        time.Advance(TimeSpan.FromHours(5));
        (await backup.EnsureDailyAsync(Ct)).ShouldBeNull();
        time.Advance(TimeSpan.FromDays(1));
        (await backup.EnsureDailyAsync(Ct)).ShouldNotBeNull();

        backup.List().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Disabled_MakesNoDailyCopy_ButManualStillWorks()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var store = app.Get<ISettingsStore>();
        await store.SaveAsync(store.Load() with { Backups = new BackupSettings { Enabled = false } }, Ct);
        var (backup, _) = Create(app);

        (await backup.EnsureDailyAsync(Ct)).ShouldBeNull();
        await backup.CreateAsync(Ct);

        backup.List().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task OldCopies_ArePruned_NewestFirst()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var store = app.Get<ISettingsStore>();
        await store.SaveAsync(store.Load() with { Backups = new BackupSettings { Keep = 3 } }, Ct);
        var (backup, time) = Create(app);

        for (var i = 0; i < 5; i++)
        {
            await backup.CreateAsync(Ct);
            time.Advance(TimeSpan.FromDays(1));
        }

        backup.List().Select(b => b.CreatedLocal.Day).ShouldBe([20, 19, 18]);
    }

    [Fact]
    public async Task ForeignFiles_InTheFolder_AreIgnored()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var (backup, _) = Create(app);
        Directory.CreateDirectory(backup.Folder);
        await File.WriteAllTextAsync(Path.Combine(backup.Folder, "finance-mine.db"), "x", Ct);
        await File.WriteAllTextAsync(Path.Combine(backup.Folder, "notes.txt"), "x", Ct);

        await backup.CreateAsync(Ct);

        backup.List().ShouldHaveSingleItem();
        File.Exists(Path.Combine(backup.Folder, "finance-mine.db")).ShouldBeTrue();
    }
}
