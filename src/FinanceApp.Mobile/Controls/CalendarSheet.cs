using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace FinanceApp.Mobile.Controls;

/// <summary>A quick choice under the calendar: «Вчера», «Через полгода», «Без срока» (<see cref="Date"/> = <c>null</c>).</summary>
public sealed record DateShortcut(string Title, DateOnly? Date);

/// <summary>What the calendar opens with.</summary>
/// <param name="Min">Days before it are shown dimmed and cannot be picked.</param>
public sealed record CalendarRequest(string Title, DateOnly? Selected, DateOnly Today, IReadOnlyList<DateShortcut> Shortcuts, DateOnly? Min = null);

/// <summary>The days of a month as the calendar shows them: weeks from Monday, six rows so the sheet keeps its height.</summary>
public static class CalendarMonth
{
    public const int Cells = 42;

    /// <summary>Monday-based column of the first day: 0 for a month that starts on Monday.</summary>
    public static int Offset(DateOnly month) => ((int)new DateOnly(month.Year, month.Month, 1).DayOfWeek + 6) % 7;

    /// <summary>The day in each of the 42 cells, or <c>null</c> for a cell outside the month.</summary>
    public static DateOnly?[] Layout(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var offset = Offset(first);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        var cells = new DateOnly?[Cells];
        for (var day = 0; day < days; day++)
        {
            cells[offset + day] = first.AddDays(day);
        }

        return cells;
    }

    public static string[] MonthNames { get; } =
        ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];

    public static string[] MonthShort { get; } = ["янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    private static readonly string[] Genitive =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    private static readonly string[] WeekDays = ["понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье"];

    /// <summary>«Вторник, 17 марта 2026».</summary>
    public static string Long(DateOnly date)
    {
        var weekDay = WeekDays[((int)date.DayOfWeek + 6) % 7];
        return $"{char.ToUpperInvariant(weekDay[0])}{weekDay[1..]}, {date.Day} {Genitive[date.Month - 1]} {date.Year}";
    }

    /// <summary>«17 марта» or, in another year, «17 марта 2027».</summary>
    public static string Short(DateOnly date, DateOnly today) =>
        date.Year == today.Year ? $"{date.Day} {Genitive[date.Month - 1]}" : $"{date.Day} {Genitive[date.Month - 1]} {date.Year}";
}

/// <summary>
/// The app's own date picker: a sheet from the bottom with the month as a grid, swipes and arrows between months,
/// a tap on the month name for months and years, and shortcuts under it. A tap on a day picks it and closes the sheet.
/// </summary>
public sealed class CalendarSheet : ContentPage
{
    private const double CellSize = 44;

    private readonly CalendarRequest _request;
    private readonly TaskCompletionSource<(bool Picked, DateOnly? Date)> _result = new();
    private readonly Grid _backdrop = [];
    private readonly Border _card;
    private readonly Label _selectedLabel;
    private readonly Label _title;
    private readonly Grid _days;
    private readonly Grid _months;
    private readonly List<(Border Cell, Label Text)> _cells = [];
    private readonly List<Button> _monthButtons = [];
    private DateOnly?[] _layout = new DateOnly?[CalendarMonth.Cells];
    private DateOnly _month;
    private DateOnly? _selected;
    private int _year;
    private bool _monthsMode;
    private bool _closing;
    private INavigation? _navigation;

    private CalendarSheet(CalendarRequest request)
    {
        _request = request;
        _selected = request.Selected;
        _month = First(request.Selected ?? (request.Min is { } min && min > request.Today ? min : request.Today));
        _year = _month.Year;
        BackgroundColor = Colors.Transparent;
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);

        _backdrop.BackgroundColor = Color.FromArgb("#B3000000");
        _backdrop.Opacity = 0;
        _backdrop.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _ = CloseAsync(false, null)) });

        _selectedLabel = new Label { FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Palette.Text };
        _title = new Label
        {
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            TextColor = Palette.Text,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var titleTap = new TapGestureRecognizer();
        titleTap.Tapped += (_, _) => ToggleMonths();
        var titleBox = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Palette.Control,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(16, 0),
            HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    _title,
                    new Label { Text = "▾", TextColor = Palette.AccentLight, FontSize = 14, VerticalTextAlignment = TextAlignment.Center },
                },
            },
        };
        titleBox.GestureRecognizers.Add(titleTap);

        var header = new Grid { ColumnDefinitions = Columns("Auto,*,Auto"), Margin = new Thickness(0, 8, 0, 4) };
        header.Add(Arrow("‹", () => Step(-1)), 0);
        header.Add(titleBox, 1);
        header.Add(Arrow("›", () => Step(1)), 2);

        _days = BuildDays();
        _months = BuildMonths();

        var shortcuts = new HorizontalStackLayout { Spacing = 8 };
        foreach (var shortcut in request.Shortcuts)
        {
            var button = new Button
            {
                Text = shortcut.Title,
                Style = (Style)Microsoft.Maui.Controls.Application.Current!.Resources["Chip"],
                HeightRequest = 36,
            };
            if (shortcut.Date is { } date && date == _selected)
            {
                button.BackgroundColor = Palette.AccentSoft;
            }

            button.Clicked += (_, _) => _ = CloseAsync(true, shortcut.Date);
            shortcuts.Add(button);
        }

        var cancel = new Button
        {
            Text = "Отмена",
            Style = (Style)Microsoft.Maui.Controls.Application.Current!.Resources["Soft"],
            Margin = new Thickness(0, 8, 0, 0),
        };
        cancel.Clicked += (_, _) => _ = CloseAsync(false, null);

        var content = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new BoxView { WidthRequest = 40, HeightRequest = 4, CornerRadius = 2, Color = Palette.Border, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 8) },
                new Label { Text = request.Title.ToUpperInvariant(), FontSize = 12, CharacterSpacing = 1, TextColor = Palette.MutedText },
                _selectedLabel,
                header,
                new Grid { Children = { _days, _months } },
                new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = shortcuts, Margin = new Thickness(0, 6, 0, 0) },
                cancel,
            },
        };

        _card = new Border
        {
            BackgroundColor = Palette.Surface,
            Stroke = Palette.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) },
            Padding = new Thickness(20, 12, 20, 24),
            VerticalOptions = LayoutOptions.End,
            Content = content,
            TranslationY = 600,
        };

        // A swipe down on the card closes it, like a system sheet.
        var down = new SwipeGestureRecognizer { Direction = SwipeDirection.Down };
        down.Swiped += (_, _) => _ = CloseAsync(false, null);
        _card.GestureRecognizers.Add(down);

        Content = new Grid { Children = { _backdrop, _card } };
        Render();
    }

    /// <summary>Shows the calendar over the current screen; <c>Picked</c> is false when it was dismissed.</summary>
    public static async Task<(bool Picked, DateOnly? Date)> ShowAsync(INavigation navigation, CalendarRequest request)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(request);
        var sheet = new CalendarSheet(request) { _navigation = navigation };
        await navigation.PushModalAsync(sheet, animated: false);
        return await sheet._result.Task;
    }

    /// <summary>For tests: what the sheet shows now.</summary>
    internal string MonthTitle => _title.Text;

    internal void Pick(DateOnly day) => _ = CloseAsync(true, day);

    internal void Step(int months)
    {
        if (_monthsMode)
        {
            _year += months;
        }
        else
        {
            _month = _month.AddMonths(months);
        }

        Render();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.WhenAll(
            _backdrop.FadeToAsync(1, 180),
            _card.TranslateToAsync(0, 0, 240, Easing.CubicOut));
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(false, null);
        return true;
    }

    private static DateOnly First(DateOnly date) => new(date.Year, date.Month, 1);

    private static ColumnDefinitionCollection Columns(string spec) =>
        (ColumnDefinitionCollection)new ColumnDefinitionCollectionTypeConverter().ConvertFromInvariantString(spec)!;

    private static Button Arrow(string text, Action step)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 26,
            TextColor = Palette.Text,
            BackgroundColor = Palette.Control,
            CornerRadius = 20,
            WidthRequest = 40,
            HeightRequest = 40,
            MinimumHeightRequest = 40,
            Padding = 0,
        };
        button.Clicked += (_, _) => step();
        return button;
    }

    private Grid BuildDays()
    {
        var grid = new Grid { RowSpacing = 2, ColumnSpacing = 2 };
        for (var c = 0; c < 7; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        grid.RowDefinitions.Add(new RowDefinition(28));
        for (var r = 0; r < 6; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(CellSize));
        }

        string[] names = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];
        for (var c = 0; c < 7; c++)
        {
            grid.Add(new Label
            {
                Text = names[c],
                FontSize = 12,
                TextColor = c >= 5 ? Palette.AccentLight : Palette.MutedText,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            }, c, 0);
        }

        for (var i = 0; i < CalendarMonth.Cells; i++)
        {
            var text = new Label { FontSize = 16, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
            var cell = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = CellSize / 2 },
                WidthRequest = CellSize,
                HeightRequest = CellSize,
                HorizontalOptions = LayoutOptions.Center,
                Content = text,
            };
            var index = i;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OnDay(index);
            cell.GestureRecognizers.Add(tap);
            grid.Add(cell, i % 7, (i / 7) + 1);
            _cells.Add((cell, text));
        }

        var left = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        left.Swiped += (_, _) => Step(1);
        var right = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        right.Swiped += (_, _) => Step(-1);
        grid.GestureRecognizers.Add(left);
        grid.GestureRecognizers.Add(right);
        return grid;
    }

    private Grid BuildMonths()
    {
        var grid = new Grid { RowSpacing = 10, ColumnSpacing = 10, IsVisible = false, Margin = new Thickness(0, 12, 0, 0), VerticalOptions = LayoutOptions.Start };
        for (var c = 0; c < 3; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var r = 0; r < 4; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(52));
        }

        for (var m = 0; m < 12; m++)
        {
            var month = m + 1;
            var button = new Button
            {
                Text = CalendarMonth.MonthShort[m],
                Style = (Style)Microsoft.Maui.Controls.Application.Current!.Resources["Chip"],
                HeightRequest = 48,
                CornerRadius = 14,
            };
            button.Clicked += (_, _) =>
            {
                _month = new DateOnly(_year, month, 1);
                ToggleMonths();
            };
            grid.Add(button, m % 3, m / 3);
            _monthButtons.Add(button);
        }

        return grid;
    }

    private void ToggleMonths()
    {
        _monthsMode = !_monthsMode;
        _year = _month.Year;
        Render();
    }

    private void OnDay(int index)
    {
        if (_layout[index] is { } day && !(_request.Min is { } min && day < min))
        {
            _selected = day;
            Render();
            _ = CloseAsync(true, day);
        }
    }

    private void Render()
    {
        _selectedLabel.Text = _selected is { } selected ? CalendarMonth.Long(selected) : "Дата не выбрана";
        _selectedLabel.TextColor = _selected is null ? Palette.MutedText : Palette.Text;
        _days.IsVisible = !_monthsMode;
        _months.IsVisible = _monthsMode;

        if (_monthsMode)
        {
            _title.Text = _year.ToString(CultureInfo.InvariantCulture);
            for (var m = 0; m < 12; m++)
            {
                var current = _month.Year == _year && _month.Month == m + 1;
                _monthButtons[m].BackgroundColor = current ? Palette.Accent : Palette.Control;
                _monthButtons[m].TextColor = current ? Colors.White : Palette.Text;
            }

            return;
        }

        _title.Text = $"{CalendarMonth.MonthNames[_month.Month - 1]} {_month.Year}";
        _layout = CalendarMonth.Layout(_month);
        for (var i = 0; i < CalendarMonth.Cells; i++)
        {
            var (cell, text) = _cells[i];
            if (_layout[i] is not { } day)
            {
                cell.IsVisible = false;
                continue;
            }

            var isSelected = day == _selected;
            var isToday = day == _request.Today;
            var disabled = _request.Min is { } min && day < min;
            cell.IsVisible = true;
            text.Text = day.Day.ToString(CultureInfo.InvariantCulture);
            cell.BackgroundColor = isSelected ? Palette.Accent : Colors.Transparent;
            cell.Stroke = isToday && !isSelected ? Palette.Accent : Colors.Transparent;
            cell.StrokeThickness = isToday && !isSelected ? 1.5 : 0;
            text.TextColor = isSelected ? Colors.White : disabled ? Palette.Border : isToday ? Palette.AccentLight : Palette.Text;
            text.FontAttributes = isSelected || isToday ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    private async Task CloseAsync(bool picked, DateOnly? date)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            // The picked day lights up for a moment before the sheet goes.
            if (picked)
            {
                await Task.Delay(120);
            }

            await Task.WhenAll(
                _backdrop.FadeToAsync(0, 160),
                _card.TranslateToAsync(0, _card.Height > 0 ? _card.Height : 600, 200, Easing.CubicIn));
        }
#pragma warning disable CA1031 // Without the animation the sheet still has to go and answer.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        try
        {
            // The navigation that showed the sheet: a modal page's own may not know the modal stack.
            var navigation = _navigation ?? Navigation;
            if (navigation.ModalStack.Count > 0 && ReferenceEquals(navigation.ModalStack[^1], this))
            {
                await navigation.PopModalAsync(animated: false);
            }
        }
#pragma warning disable CA1031 // The answer must reach the field even if the navigation fails.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
        finally
        {
            _result.TrySetResult((picked, date));
        }
    }
}
