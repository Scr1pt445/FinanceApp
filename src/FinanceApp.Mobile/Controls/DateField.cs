using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Controls;

/// <summary>Which quick choices the calendar offers under the month.</summary>
public enum DateFieldKind
{
    /// <summary>An operation's day: «Сегодня», «Вчера», «Позавчера».</summary>
    Operation,

    /// <summary>A goal's deadline: «Через 3 месяца», «Через полгода», «Через год», «К концу года».</summary>
    Goal,

    /// <summary>A start date: «Сегодня», «Завтра».</summary>
    Start,
}

/// <summary>
/// A date in a field plate: «Сегодня, 30 октября», «17 марта 2027, ср». A tap opens <see cref="CalendarSheet"/>.
/// <see cref="Text"/> is the dd.MM.yyyy text the view models read; empty when <see cref="AllowEmpty"/> and no date.
/// </summary>
public sealed class DateField : Field
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(DateField), string.Empty, BindingMode.TwoWay,
        propertyChanged: (field, _, _) => ((DateField)field).Refresh());

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(DateField), "Выбрать дату",
        propertyChanged: (field, _, _) => ((DateField)field).Refresh());

    private readonly Label _value = new() { FontSize = 17, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Button _clear;
    private bool _opening;

    public DateField()
    {
        _clear = new Button
        {
            Text = "✕",
            FontSize = 16,
            TextColor = Palette.MutedText,
            BackgroundColor = Colors.Transparent,
            Padding = 0,
            WidthRequest = 36,
            MinimumHeightRequest = 36,
            IsVisible = false,
        };
        _clear.Clicked += (_, _) => Text = string.Empty;

        var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10, MinimumHeightRequest = 50 };
        grid.Add(new Image { Source = "calendar.png", WidthRequest = 22, HeightRequest = 22, VerticalOptions = LayoutOptions.Center }, 0);
        grid.Add(_value, 1);
        grid.Add(_clear, 2);
        Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenAsync();
        GestureRecognizers.Add(tap);
        Refresh();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>The sheet's caption: «Дата операции», «Срок цели».</summary>
    public string Title { get; set; } = "Дата";

    public DateFieldKind Kind { get; set; } = DateFieldKind.Operation;

    /// <summary>The date may be removed: a ✕ in the field and «Без срока» under the calendar.</summary>
    public bool AllowEmpty
    {
        get;
        set
        {
            field = value;
            Refresh();
        }
    }

    /// <summary>Days before today cannot be picked (a deadline).</summary>
    public bool FutureOnly { get; set; }

    /// <summary>Overridable for tests; the phone's local day otherwise.</summary>
    internal Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.Now);

    internal string Shown => _value.Text;

    /// <summary>The choices under the month for a field of <paramref name="kind"/>.</summary>
    public static IReadOnlyList<DateShortcut> ShortcutsFor(DateFieldKind kind, DateOnly today, bool allowEmpty)
    {
        List<DateShortcut> list = kind switch
        {
            DateFieldKind.Goal =>
            [
                new("Через 3 месяца", today.AddMonths(3)),
                new("Через полгода", today.AddMonths(6)),
                new("Через год", today.AddYears(1)),
                new("К концу года", new DateOnly(today.Month == 12 && today.Day == 31 ? today.Year + 1 : today.Year, 12, 31)),
            ],
            DateFieldKind.Start => [new("Сегодня", today), new("Завтра", today.AddDays(1))],
            _ => [new("Сегодня", today), new("Вчера", today.AddDays(-1)), new("Позавчера", today.AddDays(-2))],
        };
        if (allowEmpty)
        {
            list.Add(new(kind == DateFieldKind.Goal ? "Без срока" : "Без даты", null));
        }

        return list;
    }

    /// <summary>«Сегодня, 30 октября», «Вчера, 29 октября», «17 марта 2027, ср».</summary>
    public static string Describe(DateOnly date, DateOnly today) =>
        (today.DayNumber - date.DayNumber) switch
        {
            0 => $"Сегодня, {CalendarMonth.Short(date, today)}",
            1 => $"Вчера, {CalendarMonth.Short(date, today)}",
            -1 => $"Завтра, {CalendarMonth.Short(date, today)}",
            _ => $"{CalendarMonth.Short(date, today)}, {date.ToString("ddd", Format.Russian)}",
        };

    private async Task OpenAsync()
    {
        if (_opening || !IsEnabled || Navigation is null)
        {
            return;
        }

        _opening = true;
        Stroke = Palette.Accent;
        try
        {
            var today = Today();
            DateOnly? current = Format.TryParseDate(Text, out var date) ? date : null;
            var request = new CalendarRequest(Title, current, today, ShortcutsFor(Kind, today, AllowEmpty), FutureOnly ? today : null);
            var (picked, chosen) = await CalendarSheet.ShowAsync(Navigation, request);
            if (picked)
            {
                Text = chosen is { } day ? Format.DateText(day) : string.Empty;
            }
        }
#pragma warning disable CA1031 // A tap handler: a calendar that cannot open must not take the app down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
        finally
        {
            Stroke = Palette.Border;
            _opening = false;
        }
    }

    private void Refresh()
    {
        var has = Format.TryParseDate(Text, out var date);
        _value.Text = has ? Describe(date, Today()) : Placeholder;
        _value.TextColor = has ? Palette.Text : Palette.MutedText;
        _clear.IsVisible = has && AllowEmpty;
    }
}
