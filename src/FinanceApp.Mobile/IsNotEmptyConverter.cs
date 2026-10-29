using System.Globalization;

namespace FinanceApp.Mobile;

/// <summary>Hides optional lines (warnings, notes, a second amount) instead of leaving an empty gap.</summary>
public sealed class IsNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && text.Length > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
