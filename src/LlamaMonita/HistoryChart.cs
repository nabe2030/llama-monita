using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LlamaMonita;

/// <summary>One line of a <see cref="HistoryChart"/>. Right-axis series are scaled independently of left-axis ones.</summary>
public sealed record ChartSeries(IReadOnlyList<double> Values, Brush Stroke, bool RightAxis = false, bool Fill = true);

/// <summary>
/// History chart, newest sample at the right edge. Plots either a single series (Values / Stroke) or
/// several (Series) against a left axis and an optional right axis, nvtop style. Draws grid lines and
/// scale labels when there is room, and degrades to a bare sparkline when small.
/// </summary>
public sealed class HistoryChart : FrameworkElement
{
    public const int Capacity = 180;   // samples kept (one per second)

    public static readonly DependencyProperty ValuesProperty = Register<IReadOnlyList<double>?>(nameof(Values), null);
    public static readonly DependencyProperty SeriesProperty = Register<IReadOnlyList<ChartSeries>?>(nameof(Series), null);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 0.0);
    public static readonly DependencyProperty StrokeProperty = Register<Brush>(nameof(Stroke), Brushes.DodgerBlue);
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit), "");
    public static readonly DependencyProperty RightUnitProperty = Register(nameof(RightUnit), "");
    public static readonly DependencyProperty ShowAxisProperty = Register(nameof(ShowAxis), true);

    private static DependencyProperty Register<T>(string name, T defaultValue) => DependencyProperty.Register(
        name, typeof(T), typeof(HistoryChart), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }

    /// <summary>Fixed maximum of the left axis; 0 picks a round maximum from the data.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string RightUnit { get => (string)GetValue(RightUnitProperty); set => SetValue(RightUnitProperty, value); }
    public bool ShowAxis { get => (bool)GetValue(ShowAxisProperty); set => SetValue(ShowAxisProperty, value); }

    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA1)));
    private static readonly Pen GridPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), 1));
    private static readonly Pen BasePen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), 1));
    private static readonly Typeface LabelFace = new("Segoe UI");

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        IReadOnlyList<ChartSeries> series = Series
            ?? (Values is { } single ? new[] { new ChartSeries(single, Stroke) } : Array.Empty<ChartSeries>());
        var left = series.Where(s => !s.RightAxis).ToList();
        var right = series.Where(s => s.RightAxis).ToList();
        double leftMax = Maximum > 0 ? Maximum : NiceMax(MaxOf(left));
        double rightMax = NiceMax(MaxOf(right));
        bool axis = ShowAxis && h >= 70 && w >= 170;
        bool large = h >= 70;

        // With an axis, the scale labels get their own gutters so they never sit under the newest data.
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double x0 = 0, x1 = w, y0 = 1, y1 = h - 1;
        if (axis)
        {
            x0 = Math.Ceiling(Measure(Format(leftMax, Unit), ppd)) + 8;
            if (right.Count > 0) x1 = w - Math.Ceiling(Measure(Format(rightMax, RightUnit), ppd)) - 8;
            y0 = 8;
            foreach (double f in new[] { 0.25, 0.5, 0.75, 1.0 })
            {
                double y = Math.Round(y1 - f * (y1 - y0)) + 0.5;
                dc.DrawLine(GridPen, new Point(x0, y), new Point(x1, y));
            }
        }
        dc.DrawLine(BasePen, new Point(x0, h - 0.5), new Point(x1, h - 0.5));

        double step = (x1 - x0) / (Capacity - 1);
        foreach (var s in series)
        {
            var v = s.Values;
            if (v.Count < 2) continue;
            double max = s.RightAxis ? rightMax : leftMax;
            int offset = Capacity - v.Count;
            Point At(int i) => new(x0 + (offset + i) * step, y1 - Math.Clamp(v[i] / max, 0, 1) * (y1 - y0));

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

            if (s.Fill)
            {
                fill.Freeze();
                Color c = (s.Stroke as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
                var gradient = new LinearGradientBrush(Color.FromArgb(88, c.R, c.G, c.B), Color.FromArgb(6, c.R, c.G, c.B), 90);
                gradient.Freeze();
                dc.DrawGeometry(gradient, null, fill);
            }
            var pen = new Pen(s.Stroke, large ? (s.Fill ? 2 : 2.4) : 1.5) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, line);
            if (large) dc.DrawEllipse(s.Stroke, null, At(v.Count - 1), 3, 3);
        }

        if (axis)
        {
            // With two axes, each scale is labelled in the colour of the series it belongs to.
            Brush leftBrush = right.Count > 0 && left.Count > 0 ? left[0].Stroke : MutedBrush;
            double yTop = y0 - 7, yMid = Math.Round(y1 - 0.5 * (y1 - y0)) - 7, yBase = y1 - 13;
            DrawLabel(dc, Format(leftMax, Unit), leftBrush, x0 - 6, yTop, true, ppd);
            DrawLabel(dc, Format(leftMax / 2, Unit), leftBrush, x0 - 6, yMid, true, ppd);
            DrawLabel(dc, "0", leftBrush, x0 - 6, yBase, true, ppd);
            DrawLabel(dc, $"{Capacity / 60} min ago", MutedBrush, x0 + 6, yBase - 2, false, ppd);
            if (right.Count > 0)
            {
                DrawLabel(dc, Format(rightMax, RightUnit), right[0].Stroke, x1 + 6, yTop, false, ppd);
                DrawLabel(dc, Format(rightMax / 2, RightUnit), right[0].Stroke, x1 + 6, yMid, false, ppd);
                DrawLabel(dc, "0", right[0].Stroke, x1 + 6, yBase, false, ppd);
            }
        }
    }

    private static double Measure(string text, double ppd) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 10.5, MutedBrush, ppd).Width;
    private static double MaxOf(List<ChartSeries> list)
    {
        double m = 0;
        foreach (var s in list)
            foreach (double v in s.Values)
                if (v > m) m = v;
        return m;
    }

    private static string Format(double value, string unit) =>
        (value >= 10 || value == Math.Floor(value) ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture))
        + (unit.Length > 0 ? (unit == "%" ? "%" : " " + unit) : "");

    private static void DrawLabel(DrawingContext dc, string text, Brush brush, double x, double y, bool alignRight, double ppd)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 10.5, brush, ppd);
        dc.PushOpacity(ReferenceEquals(brush, MutedBrush) ? 1 : 0.85);
        dc.DrawText(ft, new Point(alignRight ? x - ft.Width : x, y));
        dc.Pop();
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
