using FinanceApp.Application.Export;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceApp.Infrastructure.Tests;

/// <summary>The real service graph on a temp data directory, with a fake rate source.</summary>
internal sealed class TestApp : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private TestApp(FinancePaths paths, FakeRateSource rates, ServiceProvider provider)
    {
        Paths = paths;
        Rates = rates;
        _provider = provider;
    }

    public FinancePaths Paths { get; }

    public FakeRateSource Rates { get; }

    public string CsvPath => Path.Combine(Paths.DataDirectory, "obsidian", "transactions.csv");

    public CsvExportCoordinator Export => _provider.GetRequiredService<CsvExportCoordinator>();

    public static async Task<TestApp> CreateAsync(bool exportEnabled = true)
    {
        var paths = new FinancePaths(Path.Combine(Path.GetTempPath(), "FinanceApp.Tests", Guid.NewGuid().ToString("N")));
        paths.EnsureDataDirectory();

        var rates = new FakeRateSource();
        var services = new ServiceCollection()
            .AddFinanceApp(paths, new CsvExportOptions { Debounce = TimeSpan.FromMilliseconds(20), RetryDelay = TimeSpan.FromMilliseconds(20) });
        services.RemoveAll<IExchangeRateSource>();
        services.AddSingleton<IExchangeRateSource>(rates);

        var app = new TestApp(paths, rates, services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
        await using (var scope = app.Scope())
        {
            await scope.Get<DatabaseInitializer>().InitializeAsync(CurrencyCode.Rub, TestContext.Current.CancellationToken);
        }

        if (exportEnabled)
        {
            await app.Get<ISettingsStore>().SaveAsync(
                new AppSettings { CsvExport = new CsvExportSettings { Enabled = true, Path = app.CsvPath } },
                TestContext.Current.CancellationToken);
        }

        return app;
    }

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    public AppScope Scope() => new(_provider.CreateAsyncScope());

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        try
        {
            Directory.Delete(Paths.DataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal sealed class AppScope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public T Get<T>()
            where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    internal sealed class FakeRateSource : IExchangeRateSource
    {
        public decimal Usd { get; set; } = 90m;

        public int Calls { get; private set; }

        public Task<RubRates> GetRubRatesAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new RubRates(date, new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = Usd }));
        }
    }
}
