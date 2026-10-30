using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using FinanceApp.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Mobile;

// AdjustResize keeps the quick-entry line above the keyboard instead of under it.
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TakeRequest(Intent);
        AppShortcuts.Install(this);
    }

    /// <summary>
    /// With the PIN on, the app's last screen does not show in the recent apps (Android 13+): the amounts would be there
    /// for anyone to see without the PIN. Screenshots still work.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var locked = IPlatformApplication.Current?.Services.GetService<ISettingsStore>()?.Load().Lock.IsOn == true;
                SetRecentsScreenshotEnabled(!locked);
            }
#pragma warning disable CA1031 // Unreadable settings are reported by the app itself; the preview is not worth a crash.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                CrashReport.Save(ex);
            }
        }
    }

    /// <summary>A shortcut or the widget's «+» while the app is already running.</summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        TakeRequest(intent);
    }

    private static void TakeRequest(Intent? intent)
    {
        if (intent?.Action == AppShortcuts.Action)
        {
            PendingAction.Request(intent.GetStringExtra(AppShortcuts.KindExtra));
            intent.SetAction(null);
        }
    }
}
