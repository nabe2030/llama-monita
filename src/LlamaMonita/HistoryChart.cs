using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LlamaMonita;

/// <summary>
/// History chart: gradient-filled line, newest sample at the right edge. Draws grid lines and scale
/// labels when there is room, and degrades to a bare sparkline when small.
/// </summary>
public sealed class HistoryChart : FrameworkElement
{
    public const int Capacity = 180;   // samples kept (one per second)

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(HistoryChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(HistoryChart),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowAxisProperty = DependencyProperty.Register(
        nameof(ShowAxis), typeof(bool), typeof(HistoryChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    /// <summary>Fixed scale maximum; 0 picks a round maximum from the data.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public bool ShowAxis { get => (bool)GetValue(ShowAxisProperty); set => SetValue(ShowAxisProperty, value); }

    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA1)));
    private static readonly Pen GridPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), 1));
    private static readonly Pen BasePen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), 1));
    private static readonly Typeface LabelFace = new("Segoe UI");

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var v = Values;
        double dataMax = v is { Count: > 0 } ? v.Max() : 0;
        double max = Maximum > 0 ? Maximum : NiceMax(dataMax);
        bool axis = ShowAxis && h >= 70 && w >= 170;

        // grid
        if (axis)
            foreach (double f in new[] { 0.25, 0.5, 0.75, 1.0 })
            {
                double y = Math.Round(h - 1 - f * (h - 2)) + 0.5;
                dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
            }
        dc.DrawLine(BasePen, new Point(0, h - 0.5), new Point(w, h - 0.5));

        if (v is { Count: >= 2 })
        {
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

            Color c = (Stroke as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            var gradient = new LinearGradientBrush(Color.FromArgb(96, c.R, c.G, c.B), Color.FromArgb(6, c.R, c.G, c.B), 90);
            gradient.Freeze();
            var pen = new Pen(Stroke, h >= 70 ? 2 : 1.5) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(gradient, null, fill);
            dc.DrawGeometry(null, pen, line);
            if (h >= 70) dc.DrawEllipse(Stroke, null, At(v.Count - 1), 3, 3);
        }

        if (axis)
        {
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            DrawLabel(dc, Format(max), 4, 2, ppd);
            DrawLabel(dc, Format(max / 2), 4, Math.Round(h / 2) + 1, ppd);
            DrawLabel(dc, $"{Capacity / 60} min", 4, h - 17, ppd);
        }
    }

    private string Format(double value) =>
        (value >= 10 || value == Math.Floor(value) ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture))
        + (Unit.Length > 0 ? (Unit == "%" ? "%" : " " + Unit) : "");

    private static void DrawLabel(DrawingContext dc, string text, double x, double y, double ppd)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 10.5, LabelBrush, ppd);
        dc.DrawText(ft, new Point(x, y));
    }

    /// <summary>Smallest of 1/2/5 x 10^n that leaves a little headroom above the data.</summary>
    private static double NiceMax(double m)
    {
        if (m <= 0) return 10;
        m *= 1.1;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(m)));
        double f = m / pow;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * pow;
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
}
