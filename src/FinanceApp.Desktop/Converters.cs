using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FinanceApp.Desktop;

/// <summary>Bar width as a star column: <c>Share</c> for the bar, <c>1 − Share</c> with parameter «rest».</summary>
internal sealed class ShareToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var share = Math.Clamp(value is double d ? d : 0, 0, 1);
        var star = Equals(parameter, "rest") ? 1 - share : share;
        return new GridLength(Math.Max(star, 0.0001), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary><c>true</c> or a positive count → Visible, otherwise Collapsed; parameter «invert» flips it.</summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true || value is int count && count > 0;
        if (Equals(parameter, "invert"))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible; parameter «invert» flips it (placeholders).</summary>
internal sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) == !Equals(parameter, "invert") ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Transaction type → its Russian name for the type dropdown of the edit panel.</summary>
internal sealed class TransactionTypeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Domain.TransactionType type ? Presentation.QuickEntry.QuickEntryFormatter.Type(type) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
