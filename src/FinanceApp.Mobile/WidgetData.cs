namespace FinanceApp.Mobile;

/// <summary>
/// The numbers for the home-screen widget, kept in the app's preferences so the widget can draw them while the app
/// is closed. Only on the phone: off it (tests) there is no widget and nothing is written.
/// </summary>
public static class WidgetData
{
    private const string TotalKey = "widget_total";
    private const string DetailsKey = "widget_details";

    public const string Hidden = "•••••";

    /// <param name="locked">The PIN lock is on: the amount stays off the home screen.</param>
    public static void Publish(string total, string details, bool locked)
    {
#if ANDROID
        try
        {
            Preferences.Default.Set(TotalKey, locked ? Hidden : total);
            Preferences.Default.Set(DetailsKey, locked ? "Скрыто — включён вход по PIN" : details);
            TotalWidget.UpdateAll(Platform.AppContext);
        }
#pragma warning disable CA1031 // A widget that cannot be drawn must not disturb the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
#else
        _ = total;
        _ = details;
        _ = locked;
#endif
    }

#if ANDROID
    public static (string Total, string Details) Read() =>
        (Preferences.Default.Get(TotalKey, "—"), Preferences.Default.Get(DetailsKey, "Откройте приложение"));
#endif
}
