using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;
using FinanceApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceApp.Mobile.Tests;

/// <summary>
/// The app's real service graph (MauiProgram.AddMobileApp) on a temp folder and the real App with its theme,
/// as on the phone — minus the platform: pages are created and bound, but nothing is drawn.
/// </summary>
internal sealed class MobileApp : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private MobileApp(FinancePaths paths, ServiceProvider services, App app)
    {
        Paths = paths;
        _services = services;
        App = app;
    }

    public FinancePaths Paths { get; }

    public App App { get; }

    /// <param name="ready">Prepare the database, as on every start after the first; <c>false</c> — a fresh install.</param>
    public static async Task<MobileApp> StartAsync(bool ready = true)
    {
        ImmediateDispatcher.Install();
        var paths = new FinancePaths(Path.Combine(Path.GetTempPath(), "FinanceApp.Mobile.Tests", Guid.NewGuid().ToString("N")));
        paths.EnsureDataDirectory();
        var services = new ServiceCollection().AddMobileApp(paths);

        // No network in tests: rates for USD accounts come from here.
        services.RemoveAll<IExchangeRateSource>();
        services.AddSingleton<IExchangeRateSource, FixedRates>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // As MAUI does (UseMauiApp): from DI, so whatever App asks for is built before its theme is loaded.
        var app = ActivatorUtilities.CreateInstance<App>(provider);
        Microsoft.Maui.Controls.Application.Current = app;
        if (ready)
        {
            await provider.GetRequiredService<AppStartup>().EnsureReadyAsync();
        }

        return new MobileApp(paths, provider, app);
    }

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        // Application.Current stays: work a test started may still finish, and it needs the theme.
        await _services.DisposeAsync();
        try
        {
            Directory.Delete(Paths.DataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FixedRates : IExchangeRateSource
    {
        public Task<RubRates> GetRubRatesAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RubRates(date, new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = 90m }));
    }
}
