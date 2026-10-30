using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Controls;

/// <summary>
/// A category against its monthly limit: the name and «18 400 ₽ из 30 000 ₽» over a bar, «осталось …» under it.
/// The bar is the accent while there is room, orange from 80 % and the warning color over the limit.
/// Bound to a <see cref="LimitRow"/>.
/// </summary>
public sealed class LimitBarView : ContentView
{
    private readonly Label _title = new() { FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label _spent = new() { FontSize = 13, TextColor = Palette.MutedText, HorizontalTextAlignment = TextAlignment.End };
    private readonly Label _left = new() { FontSize = 12, TextColor = Palette.MutedText };
    private readonly BoxView _fill = new() { CornerRadius = 4, HeightRequest = 8, HorizontalOptions = LayoutOptions.Start };
    private readonly Grid _track;

    public LimitBarView()
    {
        _track = new Grid { HeightRequest = 8, Children = { new BoxView { Color = Palette.Control, CornerRadius = 4 }, _fill } };
        _track.SizeChanged += (_, _) => Render();
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        top.Add(_title, 0);
        top.Add(_spent, 1);
        Content = new VerticalStackLayout { Spacing = 5, Children = { top, _track, _left } };
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Render();
    }

    private void Render()
    {
        if (BindingContext is not LimitRow row)
        {
            return;
        }

        _title.Text = row.Title;
        _spent.Text = row.SpentText;
        _left.Text = row.LeftText;
        var color = row.IsOver ? Color.FromArgb("#F2665C") : row.IsNear ? Palette.Warning : Palette.Accent;
        _fill.Color = color;
        _left.TextColor = row.IsOver || row.IsNear ? color : Palette.MutedText;
        _fill.WidthRequest = _track.Width > 0 ? Math.Max(row.Progress * _track.Width, row.Progress > 0 ? 8 : 0) : 0;
    }
}
