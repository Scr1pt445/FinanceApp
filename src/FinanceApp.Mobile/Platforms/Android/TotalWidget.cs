using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace FinanceApp.Mobile;

/// <summary>
/// «Всего» on the home screen with buttons for a new expense, income or transfer. The layout follows the size the
/// widget is stretched to: the total and «+» when small, the three buttons in one wide row, and the month so far with
/// labelled buttons when two rows tall or more. The app writes the numbers whenever its home screen counts them
/// (<see cref="WidgetData"/>); with the PIN lock on they are hidden unless the owner allowed them — the buttons work
/// either way and open the form after the PIN.
/// </summary>
[BroadcastReceiver(Label = "FinanceApp — итого", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/total_widget_info")]
public sealed class TotalWidget : AppWidgetProvider
{
    /// <summary>Narrower than this (dp): only the total and «+».</summary>
    internal const int WideFrom = 200;

    /// <summary>Taller than this (dp): the month and labelled buttons.</summary>
    internal const int TallFrom = 100;

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null)
        {
            return;
        }

        foreach (var id in appWidgetIds)
        {
            Render(context, appWidgetManager, id);
        }
    }

    /// <summary>Stretched or shrunk on the home screen: a layout for the new size.</summary>
    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Bundle? newOptions)
    {
        base.OnAppWidgetOptionsChanged(context, appWidgetManager, appWidgetId, newOptions);
        if (context is not null && appWidgetManager is not null)
        {
            Render(context, appWidgetManager, appWidgetId);
        }
    }

    /// <summary>Redraws every placed widget with the latest numbers.</summary>
    public static void UpdateAll(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context);
        var ids = manager?.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(TotalWidget))));
        if (manager is null || ids is null)
        {
            return;
        }

        foreach (var id in ids)
        {
            Render(context, manager, id);
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

    private static void Render(Context context, AppWidgetManager manager, int id)
    {
        // In portrait the widget gets the minimum width and the maximum height of what the launcher reports.
        var options = manager.GetAppWidgetOptions(id);
        var width = options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0;
        var height = options?.GetInt(AppWidgetManager.OptionAppwidgetMaxHeight) ?? 0;
        var layout = WidgetData.LayoutFor(width, height);

        var data = WidgetData.Read();
        var views = new RemoteViews(context.PackageName, layout switch
        {
            WidgetLayout.Small => Resource.Layout.widget_small,
            WidgetLayout.Large => Resource.Layout.widget_large,
            _ => Resource.Layout.widget_wide,
        });
        views.SetTextViewText(Resource.Id.widget_total, data.Total);
        views.SetTextViewText(Resource.Id.widget_details, data.Details);
        if (layout == WidgetLayout.Large)
        {
            views.SetTextViewText(Resource.Id.widget_month, data.Month);
            views.SetViewVisibility(Resource.Id.widget_month, data.Month.Length > 0 ? ViewStates.Visible : ViewStates.Gone);
        }

        var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (open is not null)
        {
            views.SetOnClickPendingIntent(Resource.Id.widget_root, PendingIntent.GetActivity(context, 0, open, Flags));
        }

        Button(context, views, Resource.Id.widget_expense, PendingAction.Expense, 1);
        if (layout != WidgetLayout.Small)
        {
            Button(context, views, Resource.Id.widget_income, PendingAction.Income, 2);
            Button(context, views, Resource.Id.widget_transfer, PendingAction.Transfer, 3);
        }

        manager.UpdateAppWidget(id, views);
    }

    private static void Button(Context context, RemoteViews views, int view, string kind, int requestCode) =>
        views.SetOnClickPendingIntent(view, PendingIntent.GetActivity(context, requestCode, AppShortcuts.IntentFor(context, kind), Flags));
}
