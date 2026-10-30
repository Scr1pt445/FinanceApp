using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.Files;
using FinanceApp.Infrastructure.Settings;

namespace FinanceApp.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly FinancePaths _paths = new(Path.Combine(Path.GetTempPath(), "FinanceApp.Tests", Guid.NewGuid().ToString("N")));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_paths.DataDirectory))
        {
            Directory.Delete(_paths.DataDirectory, recursive: true);
        }
    }

    [Fact]
    public void MissingFile_GivesDefaults_ExportOff()
    {
        var settings = new JsonSettingsStore(_paths, new AtomicFileWriter()).Load();

        settings.BaseCurrency.ShouldBe(CurrencyCode.Rub);
        settings.CsvExport.Enabled.ShouldBeFalse();
        settings.CsvExport.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveThenLoadInNewStore_RoundTrips()
    {
        var saved = new AppSettings
        {
            BaseCurrency = CurrencyCode.Byn,
            CsvExport = new CsvExportSettings { Enabled = true, Path = @"C:\Notes\Финансы\transactions.csv" },
            ManualRates =
            [
                new ManualRate(CurrencyCode.Eur, CurrencyCode.Byn, 3.45m, new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 5)),
                new ManualRate(CurrencyCode.Usd, CurrencyCode.Byn, 3.1m, new DateOnly(2026, 10, 30), null),
            ],
            Currencies = [CurrencyCode.Usd, CurrencyCode.Parse("KZT")],
        };

        await new JsonSettingsStore(_paths, new AtomicFileWriter()).SaveAsync(saved, Ct);
        var loaded = new JsonSettingsStore(_paths, new AtomicFileWriter()).Load();

        loaded.ShouldBe(saved);
        (await File.ReadAllTextAsync(_paths.SettingsPath, Ct)).ShouldContain("\"baseCurrency\": \"BYN\"");
    }

    [Fact]
    public async Task CorruptFile_ThrowsWithPath()
    {
        _paths.EnsureDataDirectory();
        await File.WriteAllTextAsync(_paths.SettingsPath, "{ not json", Ct);

        Should.Throw<InvalidOperationException>(() => new JsonSettingsStore(_paths, new AtomicFileWriter()).Load())
            .Message.ShouldContain(_paths.SettingsPath);
    }
}
