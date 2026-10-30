using System.Collections;
using System.Globalization;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile;

/// <summary>
/// Hides optional lines (warnings, notes, a second amount) instead of leaving an empty gap. For lists, bind
/// <c>Count</c>: a binding to the collection itself is not re-evaluated when items are added.
/// </summary>
public sealed class IsNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => false,
        string text => text.Length > 0,
        int count => count > 0,
        ICollection collection => collection.Count > 0,
        _ => true,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The opposite of <see cref="IsNotEmptyConverter"/>: for «nothing here yet» hints.</summary>
public sealed class IsEmptyConverter : IValueConverter
{
    private static readonly IsNotEmptyConverter NotEmpty = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(bool)NotEmpty.Convert(value, targetType, parameter, culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Amounts as on the desktop: income green, envelopes accent, transfers muted, expenses plain.</summary>
public sealed class AmountToneColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AmountTone.Income => Palette.Income,
        AmountTone.Envelope => Palette.Accent,
        AmountTone.Neutral => Palette.MutedText,
        _ => Palette.Text,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Notes of the summary: green for good news, orange for bad, gray otherwise.</summary>
public sealed class NoteToneColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        NoteTone.Good => Palette.Income,
        NoteTone.Bad => Palette.Warning,
        _ => Palette.MutedText,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Segments of «where income went», in the desktop's colors.</summary>
public sealed class FlowToneColorConverter : IValueConverter
{
    public static Color ColorOf(FlowTone tone) => tone switch
    {
        FlowTone.Second => Palette.AccentLight,
        FlowTone.Other => Palette.AccentDeep,
        FlowTone.Envelopes => Palette.Saving,
        FlowTone.Free => Palette.Income,
        _ => Palette.Accent,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FlowTone tone ? ColorOf(tone) : Palette.Accent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The view models keep dates as «дд.мм.гггг» text; the phone picks them with a calendar.</summary>
public sealed class DateTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Format.TryParseDate(value as string, out var date) ? date.ToDateTime(TimeOnly.MinValue) : DateTime.Today;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTime date => Format.DateText(DateOnly.FromDateTime(date)),
        _ => string.Empty,
    };
}
