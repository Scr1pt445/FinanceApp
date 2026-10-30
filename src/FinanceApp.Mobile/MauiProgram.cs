using FinanceApp.Infrastructure;
using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Data;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.Hosting;
using FinanceApp.Presentation.QuickEntry;
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
        builder.Services.AddMobileApp(paths);
        return builder.Build();
    }

    /// <summary>Everything the app is made of, on top of the shared core; the tests build the same graph.</summary>
    public static IServiceCollection AddMobileApp(this IServiceCollection services, FinancePaths paths) =>
        services
            .AddFinanceApp(paths)
            .AddSingleton<AppStartup>()
            .AddSingleton<IQuickEntryBackend, ScopedQuickEntryBackend>()
            .AddSingleton<IFinanceBackend, ScopedFinanceBackend>()
            .AddSingleton<IDataBackend, ScopedDataBackend>()
            .AddSingleton<IFileSharer, MauiFileSharer>()
            .AddSingleton<ISetupBackend, SetupBackend>()
            .AddSingleton(sp => new QuickEntryViewModel(sp.GetRequiredService<IQuickEntryBackend>())
            {
                // There is no Esc on a phone; the keyboard's action key and the button both save.
                ReadyMessage = "Готово — сохранить",
            })
            .AddSingleton<HomeViewModel>()
            .AddSingleton<ShellViewModel>()
            .AddSingleton<DataViewModel>()
            .AddSingleton<FormPresenter>()
            .AddSingleton<AppShell>()
            .AddSingleton<HomePage>()
            .AddSingleton<OperationsPage>()
            .AddSingleton<AccountsPage>()
            .AddSingleton<SummaryPage>()
            .AddSingleton<MorePage>()
            .AddTransient<CategoriesPage>()
            .AddSingleton<Func<CategoriesPage>>(sp => sp.GetRequiredService<CategoriesPage>);
}
