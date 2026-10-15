using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Desktop;

/// <summary>Lays children out in a row, each as wide as its share of the total <c>Weight</c> (segmented bars).</summary>
internal sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(ProportionalPanel), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetWeight(DependencyObject element) => (double)element.GetValue(WeightProperty);

    public static void SetWeight(DependencyObject element, double value) => element.SetValue(WeightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var total = InternalChildren.Cast<UIElement>().Sum(c => Math.Max(GetWeight(c), 0));
        var x = 0d;
        foreach (UIElement child in InternalChildren)
        {
            var width = total <= 0 ? 0 : finalSize.Width * Math.Max(GetWeight(child), 0) / total;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}

/// <summary>
/// «Темп трат»: expense columns per day, week or month, and running totals of this period (solid) and the
/// previous one (dashed). Values come normalized to 0..1 from <see cref="PaceChartData"/>.
/// </summary>
internal sealed class PaceChart : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(PaceChartData), typeof(PaceChart), new FrameworkPropertyMetadata(PaceChartData.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(PaceChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(PaceChart), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PreviousBrushProperty = DependencyProperty.Register(
        nameof(PreviousBrush), typeof(Brush), typeof(PaceChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
        nameof(AxisBrush), typeof(Brush), typeof(PaceChart), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public PaceChartData Data
    {
        get => (PaceChartData)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush PreviousBrush
    {
        get => (Brush)GetValue(PreviousBrushProperty);
        set => SetValue(PreviousBrushProperty, value);
    }

    public Brush AxisBrush
    {
        get => (Brush)GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var data = Data;
        var (width, height) = (ActualWidth, ActualHeight);
        if (data.Slots == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        // Transparent background so the whole area takes the tooltip.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        drawingContext.DrawLine(new Pen(AxisBrush, 1), new Point(0, height - 0.5), new Point(width, height - 0.5));

        var slot = width / data.Slots;
        var barWidth = Math.Max(slot * 0.6, 1);
        for (var i = 0; i < data.Bars.Count; i++)
        {
            var barHeight = data.Bars[i] * height * 0.45;
            if (barHeight > 0)
            {
                barHeight = Math.Max(barHeight, 2);
                drawingContext.DrawRoundedRectangle(
                    BarBrush, null, new Rect((i * slot) + ((slot - barWidth) / 2), height - barHeight, barWidth, barHeight), 1.5, 1.5);
            }
        }

        Point At(int index, double value) => new((index * slot) + (slot / 2), height - 4 - (value * (height - 10)));

        if (data.Previous.Count > 1)
        {
            var pen = new Pen(PreviousBrush, 1.5) { DashStyle = new DashStyle([3, 3], 0) };
            drawingContext.DrawGeometry(null, pen, Line(data.Previous, At));
        }

        if (data.Current.Count > 0)
        {
            if (data.Current.Count > 1)
            {
                drawingContext.DrawGeometry(null, new Pen(LineBrush, 2.5) { LineJoin = PenLineJoin.Round }, Line(data.Current, At));
            }

            drawingContext.DrawEllipse(LineBrush, null, At(data.Current.Count - 1, data.Current[^1]), 4, 4);
        }
    }

    private static StreamGeometry Line(IReadOnlyList<double> values, Func<int, double, Point> at)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(at(0, values[0]), false, false);
            for (var i = 1; i < values.Count; i++)
            {
                context.LineTo(at(i, values[i]), true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
