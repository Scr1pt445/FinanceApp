using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace FinanceApp.Mobile;

/// <summary>
/// «Всего» on the home screen with «+» for a new expense. The app writes the numbers whenever its home screen
/// counts them (<see cref="WidgetData"/>); the widget only shows the last ones. With the PIN lock on, the amount is
/// hidden — anyone can look at a home screen.
/// </summary>
[BroadcastReceiver(Label = "FinanceApp — итого", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/total_widget_info")]
public sealed class TotalWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is not null && appWidgetManager is not null && appWidgetIds is not null)
        {
            Render(context, appWidgetManager, appWidgetIds);
        }
    }

    /// <summary>Redraws every placed widget with the latest numbers.</summary>
    public static void UpdateAll(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context);
        var ids = manager?.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(TotalWidget))));
        if (manager is not null && ids is { Length: > 0 })
        {
            Render(context, manager, ids);
        }
    }

    /// <summary>Immutable where Android has it (6.0+, required from 12).</summary>
    private static PendingIntentFlags Flags
    {
        get
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                return PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;
            }

            return PendingIntentFlags.UpdateCurrent;
        }
    }

    private static void Render(Context context, AppWidgetManager manager, int[] ids)
    {
        var (total, details) = WidgetData.Read();
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_total);
        views.SetTextViewText(Resource.Id.widget_total, total);
        views.SetTextViewText(Resource.Id.widget_details, details);

        var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (open is not null)
        {
            views.SetOnClickPendingIntent(Resource.Id.widget_root, PendingIntent.GetActivity(context, 0, open, Flags));
        }

        var expense = AppShortcuts.IntentFor(context, PendingAction.Expense);
        views.SetOnClickPendingIntent(Resource.Id.widget_plus, PendingIntent.GetActivity(context, 1, expense, Flags));

        foreach (var id in ids)
        {
            manager.UpdateAppWidget(id, views);
        }
    }
}
