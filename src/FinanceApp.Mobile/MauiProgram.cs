using FinanceApp.Infrastructure;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.Hosting;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Mobile;

/// <summary>Composition root of the Android app: the same core and service graph as the desktop app.</summary>
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

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services
            .AddFinanceApp(paths)
            .AddSingleton<AppStartup>()
            .AddSingleton<IQuickEntryBackend, ScopedQuickEntryBackend>()
            .AddSingleton<IFinanceBackend, ScopedFinanceBackend>()
            .AddSingleton(sp => new QuickEntryViewModel(sp.GetRequiredService<IQuickEntryBackend>())
            {
                // There is no Esc on a phone; the keyboard's action key and the button both save.
                ReadyMessage = "Готово — сохранить",
            })
            .AddSingleton<HomeViewModel>()
            .AddTransient<HomePage>();

        return builder.Build();
    }
}
