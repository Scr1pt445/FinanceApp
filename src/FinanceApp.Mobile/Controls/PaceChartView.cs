using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Controls;

/// <summary>
/// The spending chart of «Итоги», as on the desktop (Charts.cs): expense columns, the running total of this
/// period (solid) and of the previous one (dashed). Values come normalized to 0..1 from <see cref="PaceChartData"/>.
/// </summary>
public sealed class PaceChartView : GraphicsView, IDrawable
{
    public static readonly BindableProperty DataProperty = BindableProperty.Create(
        nameof(Data), typeof(PaceChartData), typeof(PaceChartView), PaceChartData.Empty,
        propertyChanged: (view, _, _) => ((PaceChartView)view).Invalidate());

    public PaceChartView() => Drawable = this;

    public PaceChartData Data
    {
        get => (PaceChartData)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var data = Data;
        var (width, height) = (dirtyRect.Width, dirtyRect.Height);
        if (data.Slots == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        canvas.StrokeColor = Palette.Border;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, height - 0.5f, width, height - 0.5f);

        var slot = width / data.Slots;
        var barWidth = Math.Max(slot * 0.6f, 1f);
        canvas.FillColor = Palette.AccentDeep;
        for (var i = 0; i < data.Bars.Count; i++)
        {
            var barHeight = (float)(data.Bars[i] * height * 0.45);
            if (barHeight > 0)
            {
                barHeight = Math.Max(barHeight, 2f);
                canvas.FillRoundedRectangle((i * slot) + ((slot - barWidth) / 2), height - barHeight, barWidth, barHeight, 1.5f);
            }
        }

        PointF At(int index, double value) => new((index * slot) + (slot / 2), (float)(height - 4 - (value * (height - 10))));

        if (data.Previous.Count > 1)
        {
            canvas.StrokeColor = Palette.MutedText;
            canvas.StrokeSize = 1.5f;
            canvas.StrokeDashPattern = [3, 3];
            canvas.DrawPath(Line(data.Previous, At));
            canvas.StrokeDashPattern = null;
        }

        if (data.Current.Count > 0)
        {
            canvas.StrokeColor = Palette.Accent;
            canvas.StrokeSize = 2.5f;
            canvas.StrokeLineJoin = LineJoin.Round;
            if (data.Current.Count > 1)
            {
                canvas.DrawPath(Line(data.Current, At));
            }

            var last = At(data.Current.Count - 1, data.Current[^1]);
            canvas.FillColor = Palette.Accent;
            canvas.FillCircle(last, 4);
        }
    }

    private static PathF Line(IReadOnlyList<double> values, Func<int, double, PointF> at)
    {
        var path = new PathF(at(0, values[0]));
        for (var i = 1; i < values.Count; i++)
        {
            path.LineTo(at(i, values[i]));
        }

        return path;
    }
}
