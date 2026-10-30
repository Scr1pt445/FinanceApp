using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;

namespace FinanceApp.Mobile;

/// <summary>
/// A long press on the app icon: «Расход», «Доход», «Перевод» — the form opens at once (after the PIN, if set).
/// The same intents serve the widget's «+».
/// </summary>
public static class AppShortcuts
{
    public const string Action = "io.github.scr1pt445.financeapp.NEW_OPERATION";
    public const string KindExtra = "kind";

    public static Intent IntentFor(Context context, string kind)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetAction(Action);
        intent.PutExtra(KindExtra, kind);
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        return intent;
    }

    public static void Install(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(25) || context.GetSystemService(Java.Lang.Class.FromType(typeof(ShortcutManager))) is not ShortcutManager manager)
        {
            return;
        }

        try
        {
            manager.SetDynamicShortcuts(
            [
                Shortcut(context, PendingAction.Expense, "Расход", "Новый расход", Resource.Drawable.shortcut_expense),
                Shortcut(context, PendingAction.Income, "Доход", "Новый доход", Resource.Drawable.shortcut_income),
                Shortcut(context, PendingAction.Transfer, "Перевод", "Новый перевод", Resource.Drawable.shortcut_transfer),
            ]);
        }
#pragma warning disable CA1031 // Some launchers refuse shortcuts; the app works without them.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
    }

    private static ShortcutInfo Shortcut(Context context, string kind, string shortLabel, string longLabel, int icon)
    {
#pragma warning disable CA1416 // Install checks for Android 7.1.
        return new ShortcutInfo.Builder(context, kind)
            .SetShortLabel(shortLabel)
            .SetLongLabel(longLabel)
            .SetIcon(Icon.CreateWithResource(context, icon))
            .SetIntent(IntentFor(context, kind))
            .Build();
#pragma warning restore CA1416
    }
}
