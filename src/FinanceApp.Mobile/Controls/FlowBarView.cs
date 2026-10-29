using System.Collections.Specialized;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Controls;

/// <summary>«Куда ушли доходы» as one rounded bar split by the segments' amounts; the legend is laid out in XAML.</summary>
public sealed class FlowBarView : GraphicsView, IDrawable
{
    public static readonly BindableProperty SegmentsProperty = BindableProperty.Create(
        nameof(Segments), typeof(IEnumerable<FlowSegment>), typeof(FlowBarView), null,
        propertyChanged: (view, oldValue, newValue) => ((FlowBarView)view).Watch(oldValue, newValue));

    public FlowBarView()
    {
        Drawable = this;
        HeightRequest = 14;
    }

    public IEnumerable<FlowSegment>? Segments
    {
        get => (IEnumerable<FlowSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var segments = Segments?.Where(s => s.Weight > 0).ToList() ?? [];
        var total = segments.Sum(s => s.Weight);
        if (total <= 0 || dirtyRect.Width <= 0)
        {
            return;
        }

        // Clip to a pill so the first and last segments get the rounded ends.
        var pill = new PathF();
        pill.AppendRoundedRectangle(dirtyRect, dirtyRect.Height / 2);
        canvas.ClipPath(pill);

        var x = dirtyRect.Left;
        foreach (var segment in segments)
        {
            var width = (float)(segment.Weight / total * dirtyRect.Width);
            canvas.FillColor = FlowToneColorConverter.ColorOf(segment.Tone);
            canvas.FillRectangle(x, dirtyRect.Top, width + 0.5f, dirtyRect.Height);
            x += width;
        }
    }

    private void Watch(object? oldValue, object? newValue)
    {
        if (oldValue is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= OnSegmentsChanged;
        }

        if (newValue is INotifyCollectionChanged current)
        {
            current.CollectionChanged += OnSegmentsChanged;
        }

        Invalidate();
    }

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();
}
