using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;

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
