using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Infrastructure;
using FinanceApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Cli;

/// <summary>
/// Composition root of the CLI: one service provider per process, a migrated database,
/// and a final flush of the CSV export before exit (the process does not live long enough
/// for the background export).
/// </summary>
internal sealed class CliHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private CliHost(FinancePaths paths, ServiceProvider provider)
    {
        Paths = paths;
        _provider = provider;
    }

    public FinancePaths Paths { get; }

    public AppSettings Settings => _provider.GetRequiredService<ISettingsStore>().Load();

    /// <param name="baseCurrency">Base currency for a brand-new data directory; ignored once the database exists.</param>
    public static async Task<CliHost> StartAsync(string? dataDirectory, CurrencyCode? baseCurrency, CancellationToken cancellationToken)
    {
        var paths = FinancePaths.Resolve(dataDirectory);
        var isNew = !File.Exists(paths.DatabasePath);
        paths.EnsureDataDirectory();

        var provider = new ServiceCollection().AddFinanceApp(paths).BuildServiceProvider(validateScopes: true);
        var host = new CliHost(paths, provider);

        if (isNew && baseCurrency is { } currency && currency != host.Settings.BaseCurrency)
        {
            var store = provider.GetRequiredService<ISettingsStore>();
            await store.SaveAsync(store.Load() with { BaseCurrency = currency }, cancellationToken);
        }

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(host.Settings.BaseCurrency, cancellationToken);
        return host;
    }

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Runs <paramref name="action"/> in its own scope — one unit of work.</summary>
    public async Task<TResult> InScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        var export = _provider.GetRequiredService<CsvExportCoordinator>();
        await export.FlushAsync();
        if (export.Status.State == CsvExportState.Failed)
        {
            await Console.Error.WriteLineAsync($"⚠ {export.Status.Error}");
        }

        await _provider.DisposeAsync();
    }
}
