using System.Windows;
using System.Windows.Media;

namespace LlamaMonita;

/// <summary>Minimal history chart: a line with a translucent fill, newest sample at the right edge.</summary>
public sealed class Sparkline : FrameworkElement
{
    public const int Capacity = 120;

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    /// <summary>Fixed scale maximum; 0 scales to the data.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var grid = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), 1);
        grid.Freeze();
        dc.DrawLine(grid, new Point(0, h - 0.5), new Point(w, h - 0.5));
        dc.DrawLine(grid, new Point(0, Math.Round(h / 2) + 0.5), new Point(w, Math.Round(h / 2) + 0.5));

        var v = Values;
        if (v is null || v.Count < 2) return;
        double max = Maximum > 0 ? Maximum : Math.Max(1e-9, v.Max() * 1.15);
        double step = w / (Capacity - 1);
        int offset = Capacity - v.Count;

        Point At(int i) => new((offset + i) * step, h - 1 - Math.Clamp(v[i] / max, 0, 1) * (h - 2));

        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            lc.BeginFigure(At(0), false, false);
            fc.BeginFigure(new Point(At(0).X, h), true, true);
            fc.LineTo(At(0), false, false);
            for (int i = 1; i < v.Count; i++)
            {
                lc.LineTo(At(i), true, true);
                fc.LineTo(At(i), false, false);
            }
            fc.LineTo(new Point(At(v.Count - 1).X, h), false, false);
        }
        line.Freeze();
        fill.Freeze();

        var fillBrush = Stroke.Clone();
        fillBrush.Opacity = 0.16;
        fillBrush.Freeze();
        var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(fillBrush, null, fill);
        dc.DrawGeometry(null, pen, line);
    }
}
