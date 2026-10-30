using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.Backups;
using FinanceApp.Application.Export;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Import;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Infrastructure.Backups;
using FinanceApp.Infrastructure.ExchangeRates;
using FinanceApp.Infrastructure.Files;
using FinanceApp.Infrastructure.Persistence;
using FinanceApp.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Everything except UI: database, repositories, rates, settings, use cases and the CSV export integration.
    /// Shared by the CLI and the desktop app. Use cases are scoped — one scope per user action.
    /// </summary>
    public static IServiceCollection AddFinanceApp(this IServiceCollection services, FinancePaths paths, CsvExportOptions? exportOptions = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileWriter, AtomicFileWriter>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IDatabaseBackup, SqliteDatabaseBackup>();

        services.AddDbContext<FinanceDbContext>(options => options.UseSqlite(paths.ConnectionString));
        services.AddScoped<ITransactionRepository, EfTransactionRepository>();
        services.AddScoped<ICategoryRepository, EfCategoryRepository>();
        services.AddScoped<IAccountRepository, EfAccountRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<DatabaseInitializer>();

        services.AddHttpClient<CbrExchangeRateSource>(client =>
        {
            client.BaseAddress = CbrExchangeRateSource.DefaultBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<RateSourceCooldown>();
        services.AddTransient<IExchangeRateSource, CoolingExchangeRateSource>();
        services.AddScoped<IExchangeRateCache, EfExchangeRateCache>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddSingleton<ManualRateService>();

        services.AddScoped<DataChangedNotifier>();
        services.AddScoped<TransactionService>();
        services.AddScoped<CsvImportService>();
        services.AddScoped<QuickEntryService>();
        services.AddScoped<PeriodOverviewService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AccountBalancesService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<CsvExportService>();

        services.AddSingleton(exportOptions ?? new CsvExportOptions());
        services.AddSingleton<ICsvExportJob, ScopedCsvExportJob>();
        services.AddSingleton<CsvExportCoordinator>();
        services.AddSingleton<IDataChangedListener>(sp => sp.GetRequiredService<CsvExportCoordinator>());

        return services;
    }

    /// <summary>Background exports must not share the caller's DbContext, so each run gets its own scope.</summary>
    private sealed class ScopedCsvExportJob(IServiceScopeFactory scopeFactory) : ICsvExportJob
    {
        public async Task<int> RunAsync(string path, CancellationToken cancellationToken)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CsvExportService>().ExportAsync(path, cancellationToken);
        }
    }
}
