using FinanceApp.Application.Backups;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Presentation.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Infrastructure.Tests.Hosting;

public sealed class ScopedDataBackendTests : IAsyncLifetime
{
    private TestApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await TestApp.CreateAsync(exportEnabled: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private ScopedDataBackend Backend => new(_app.Get<IServiceScopeFactory>(), _app.Get<IDatabaseBackup>(), _app.Get<IDatabaseRestore>());

    [Fact]
    public async Task ExportCsv_WritesBothFilesIntoTheFolder()
    {
        await using (var scope = _app.Scope())
        {
            await scope.Get<QuickEntryService>().SaveAsync("350 еда кофе", Ct);
        }

        var folder = Path.Combine(_app.Paths.DataDirectory, "share");
        var exported = await Backend.ExportCsvAsync(folder, Ct);

        exported.Rows.ShouldBe(1);
        exported.Paths.Select(Path.GetFileName).ShouldBe(["transactions.csv", "accounts.csv"]);
        exported.Paths.ShouldAllBe(p => File.Exists(p));
        (await File.ReadAllTextAsync(exported.Paths[0], Ct)).ShouldContain("кофе");
    }

    [Fact]
    public async Task Backup_AddsACopyToTheList()
    {
        var copy = await Backend.BackupAsync(Ct);

        File.Exists(copy.Path).ShouldBeTrue();
        Backend.ListBackups().ShouldContain(c => c.Path == copy.Path);
    }
}
