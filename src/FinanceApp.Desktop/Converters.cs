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

/// <summary>A 0..1 value times the pixel size given as the parameter (chart column heights).</summary>
internal sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var size = double.Parse((string)parameter!, CultureInfo.InvariantCulture);
        var share = value is double d ? Math.Clamp(d, 0, 1) : 0;
        return Math.Max(share * size, share > 0 ? 2 : 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary><c>true</c>, a non-null object or a positive count → Visible; parameter «invert» flips it.</summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            bool b => b,
            int count => count > 0,
            null => false,
            _ => true,
        };
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

/// <summary>True when both values are equal — highlights the selected sidebar item.</summary>
internal sealed class EqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Negates a <c>bool</c> both ways (a pair of radio buttons over one flag).</summary>
internal sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
