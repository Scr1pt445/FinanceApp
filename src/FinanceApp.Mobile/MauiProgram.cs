using FinanceApp.Infrastructure;
using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Data;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.Hosting;
using FinanceApp.Presentation.QuickEntry;
using FinanceApp.Presentation.Settings;
using FinanceApp.Presentation.Setup;

namespace FinanceApp.Mobile;

/// <summary>Composition root of the Android app: the same core and service graph as the desktop app, plus the phone screens.</summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // One local database per install, in the app's private storage (ADR 0013); no sync with the desktop.
        var paths = new FinancePaths(FileSystem.AppDataDirectory);
        paths.EnsureDataDirectory();

        if (CrashReport.TakeLast() is { } report)
        {
            var crashBuilder = MauiApp.CreateBuilder();
            crashBuilder.UseMauiApp(_ => new CrashApp(report));
            return crashBuilder.Build();
        }

#if ANDROID
        InputLook.Apply();
#endif

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.ConfigureFonts(fonts => fonts.AddFont("BynSign.ttf", "BynSign"));
#if ANDROID
        // Labels and buttons draw this private character as the Belarusian ruble sign (Resources/Fonts/BynSign.ttf).
        QuickEntryFormatter.BynSign = "\uE0B0";
#endif
        builder.Services.AddMobileApp(paths);
        return builder.Build();
    }

    /// <summary>Everything the app is made of, on top of the shared core; the tests build the same graph.</summary>
    public static IServiceCollection AddMobileApp(this IServiceCollection services, FinancePaths paths) =>
        services
            .AddFinanceApp(paths)
            .AddSingleton<AppStartup>()
            .AddSingleton<RatePrefetcher>()
            .AddSingleton<IQuickEntryBackend, ScopedQuickEntryBackend>()
            .AddSingleton<IFinanceBackend, ScopedFinanceBackend>()
            .AddSingleton<IDataBackend, ScopedDataBackend>()
            .AddSingleton<IFileSharer, MauiFileSharer>()
            .AddSingleton<ISetupBackend, SetupBackend>()
            .AddSingleton<IFileChooser, MauiFileChooser>()
#if ANDROID
            .AddSingleton<IBiometrics, AndroidBiometrics>()
#else
            .AddSingleton<IBiometrics, NoBiometrics>()
#endif
            .AddSingleton<LockViewModel>()
            .AddTransient<PinSetupViewModel>()
            .AddSingleton<RecurringViewModel>()
            .AddSingleton<DebtsViewModel>()
            .AddSingleton<CompareMonthsViewModel>()
            .AddSingleton<CurrenciesViewModel>()
            .AddSingleton<Screens>()
            .AddSingleton(sp => new QuickEntryViewModel(sp.GetRequiredService<IQuickEntryBackend>())
            {
                // There is no Esc on a phone; the keyboard's action key and the button both save.
                ReadyMessage = "Готово — сохранить",
            })
            .AddSingleton<HomeViewModel>()
            .AddSingleton<ShellViewModel>()
            .AddSingleton<DataViewModel>()
            .AddSingleton<PhoneSettingsViewModel>()
            .AddSingleton<FormPresenter>()
            .AddSingleton<AppShell>()
            .AddSingleton<HomePage>()
            .AddSingleton<OperationsPage>()
            .AddSingleton<AccountsPage>()
            .AddSingleton<SummaryPage>()
            .AddSingleton<MorePage>()
            .AddTransient<CategoriesPage>()
            .AddSingleton<Func<CategoriesPage>>(sp => sp.GetRequiredService<CategoriesPage>)
            .AddSingleton(sp =>
            {
                // A new or removed rate changes the totals on the home screen and the accounts.
                var rates = new ManualRatesViewModel(sp.GetRequiredService<IFinanceBackend>(), sp.GetRequiredService<TimeProvider>());
                rates.Changed += async (_, _) =>
                {
                    try
                    {
                        await sp.GetRequiredService<HomeViewModel>().RefreshAsync();
                        await sp.GetRequiredService<ShellViewModel>().RefreshAsync();
                    }
#pragma warning disable CA1031 // An event handler: the totals catch up on the next refresh anyway.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        CrashReport.Save(ex);
                    }
                };
                return rates;
            })
            .AddTransient<RatesPage>()
            .AddTransient<RecurringPage>()
            .AddSingleton<Func<RecurringPage>>(sp => sp.GetRequiredService<RecurringPage>)
            .AddSingleton<Func<RatesPage>>(sp => sp.GetRequiredService<RatesPage>)
            .AddTransient<DebtsPage>()
            .AddSingleton<Func<DebtsPage>>(sp => sp.GetRequiredService<DebtsPage>)
            .AddTransient<CompareMonthsPage>()
            .AddSingleton<Func<CompareMonthsPage>>(sp => sp.GetRequiredService<CompareMonthsPage>)
            .AddTransient<CurrenciesPage>()
            .AddSingleton<Func<CurrenciesPage>>(sp => sp.GetRequiredService<CurrenciesPage>);
}
