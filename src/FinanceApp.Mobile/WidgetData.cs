namespace FinanceApp.Mobile;

/// <summary>The home-screen widget's layouts, by the size it is stretched to.</summary>
public enum WidgetLayout
{
    /// <summary>The total and «+».</summary>
    Small,

    /// <summary>One row: the total and three round buttons.</summary>
    Wide,

    /// <summary>Two rows or more: the total, the month so far and three labelled buttons.</summary>
    Large,
}

/// <summary>What the widget shows: the text of each line, already hidden when it must be.</summary>
public sealed record WidgetText(string Total, string Details, string Month)
{
    public const string Hidden = "•••••";

    /// <param name="hideAmounts">The PIN lock is on and the owner did not allow the amounts on the home screen.</param>
    public static WidgetText From(string total, string details, string month, bool hideAmounts) =>
        hideAmounts
            ? new WidgetText(Hidden, "Суммы скрыты — включён вход по PIN", string.Empty)
            : new WidgetText(total.Length > 0 ? total : "—", details, month);
}

/// <summary>
/// The numbers for the home-screen widget, kept in the app's preferences so the widget can draw them while the app
/// is closed. Only on the phone: off it (tests) there is no widget and nothing is written.
/// </summary>
public static class WidgetData
{
    private const string TotalKey = "widget_total";
    private const string DetailsKey = "widget_details";
    private const string MonthKey = "widget_month";

    /// <summary>The layout for a widget of <paramref name="widthDp"/> × <paramref name="heightDp"/>; 0 — not reported yet.</summary>
    public static WidgetLayout LayoutFor(int widthDp, int heightDp) =>
        widthDp > 0 && widthDp < 200 ? WidgetLayout.Small
        : heightDp >= 100 ? WidgetLayout.Large
        : WidgetLayout.Wide;

    public static void Publish(WidgetText text)
    {
        ArgumentNullException.ThrowIfNull(text);
#if ANDROID
        try
        {
            Preferences.Default.Set(TotalKey, text.Total);
            Preferences.Default.Set(DetailsKey, text.Details);
            Preferences.Default.Set(MonthKey, text.Month);
            TotalWidget.UpdateAll(Platform.AppContext);
        }
#pragma warning disable CA1031 // A widget that cannot be drawn must not disturb the app.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
#endif
    }

#if ANDROID
    public static WidgetText Read() => new(
        Preferences.Default.Get(TotalKey, "—"),
        Preferences.Default.Get(DetailsKey, "Откройте приложение"),
        Preferences.Default.Get(MonthKey, string.Empty));
#endif
}
