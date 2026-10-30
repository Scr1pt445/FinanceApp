using FinanceApp.Application.Accounts;
using FinanceApp.Application.Backups;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.Backups;

/// <summary>A copy brought back: after a reinstall, on a new phone, or after a mistake.</summary>
public sealed class DatabaseRestoreTests : IAsyncLifetime
{
    private TestApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await TestApp.CreateAsync(exportEnabled: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task SaveAsync(string line)
    {
        await using var scope = _app.Scope();
        await scope.Get<QuickEntryService>().SaveAsync(line, Ct);
    }

    private async Task<int> CountAsync()
    {
        await using var scope = _app.Scope();
        return (await scope.Get<CatalogService>().LoadAsync(Ct)).CategoryUsage.Values.Sum();
    }

    [Fact]
    public async Task Restore_BringsTheCopyBack_AndKeepsWhatItReplaced()
    {
        await SaveAsync("350 еда кофе");
        await SaveAsync("1200 транспорт такси");
        var copy = await _app.Get<IDatabaseBackup>().CreateAsync(Ct);
        await SaveAsync("999 еда ошибка");
        (await CountAsync()).ShouldBe(3);

        var restore = _app.Get<IDatabaseRestore>();
        var contents = await restore.InspectAsync(copy.Path, Ct);
        contents.Transactions.ShouldBe(2);
        contents.LastOperation.ShouldBe(DateOnly.FromDateTime(DateTime.Now));

        var result = await restore.RestoreAsync(copy.Path, Ct);

        (await CountAsync()).ShouldBe(2);
        result.BaseCurrency.ShouldBe(CurrencyCode.Rub);
        var safety = result.SafetyCopy.ShouldNotBeNull();
        (await restore.InspectAsync(safety.Path, Ct)).Transactions.ShouldBe(3);
    }

    [Fact]
    public async Task NotADatabase_IsRefused_AndNothingChanges()
    {
        await SaveAsync("350 еда кофе");
        var junk = Path.Combine(_app.Paths.DataDirectory, "photo.jpg");
        await File.WriteAllTextAsync(junk, "not a database", Ct);

        await Should.ThrowAsync<InvalidDataException>(() => _app.Get<IDatabaseRestore>().RestoreAsync(junk, Ct));

        (await CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ACopyWithATrigger_IsRefused_AndNothingChanges()
    {
        await SaveAsync("350 еда кофе");
        var copy = await _app.Get<IDatabaseBackup>().CreateAsync(Ct);
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = copy.Path, Pooling = false };
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString()))
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER wipe AFTER INSERT ON Accounts BEGIN DELETE FROM Transactions; END";
            await command.ExecuteNonQueryAsync(Ct);
        }

        var error = await Should.ThrowAsync<InvalidDataException>(() => _app.Get<IDatabaseRestore>().RestoreAsync(copy.Path, Ct));

        error.Message.ShouldContain("небезопасно");
        (await CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task TheBaseCurrency_FollowsTheCopy()
    {
        await SaveAsync("350 еда кофе");
        var copy = await _app.Get<IDatabaseBackup>().CreateAsync(Ct);
        var settings = _app.Get<ISettingsStore>();
        await settings.SaveAsync(settings.Load() with { BaseCurrency = CurrencyCode.Usd }, Ct);

        var result = await _app.Get<IDatabaseRestore>().RestoreAsync(copy.Path, Ct);

        result.BaseCurrency.ShouldBe(CurrencyCode.Rub);
        settings.Load().BaseCurrency.ShouldBe(CurrencyCode.Rub);
    }
}
