using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace XDiskInspector.App;

public sealed class CapacityRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(CapacityRing), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsScanningProperty = DependencyProperty.Register(nameof(IsScanning), typeof(bool), typeof(CapacityRing), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnScanningChanged));
    private readonly DispatcherTimer _timer;
    private double _scanAngle;

    public CapacityRing()
    {
        Width = 260; Height = 260;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _timer.Tick += (_, _) => { _scanAngle = (_scanAngle + 5) % 360; InvalidateVisual(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsScanning { get => (bool)GetValue(IsScanningProperty); set => SetValue(IsScanningProperty, value); }

    private static void OnScanningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (CapacityRing)d;
        if ((bool)e.NewValue && SystemParameters.ClientAreaAnimation) ring._timer.Start(); else ring._timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var center = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        var radius = Math.Max(20, Math.Min(RenderSize.Width, RenderSize.Height) / 2 - 18);
        var basePen = new Pen(new SolidColorBrush(Color.FromRgb(42, 53, 61)), 16) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawEllipse(null, basePen, center, radius, radius);

        var cyan = new SolidColorBrush(Color.FromRgb(53, 215, 255));
        var pen = new Pen(cyan, 16) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var sweep = IsScanning ? 68d : Math.Clamp(Value, 0, 100) * 3.6;
        var start = IsScanning ? _scanAngle : -90d;
        if (!IsScanning && sweep >= 359.9)
            dc.DrawEllipse(null, pen, center, radius, radius);
        else if (sweep > 0.1)
            dc.DrawGeometry(null, pen, CreateArc(center, radius, start, sweep));

        if (IsScanning)
        {
            var beam = new Pen(new SolidColorBrush(Color.FromArgb(80, 53, 215, 255)), 2);
            var angle = (_scanAngle + 68) * Math.PI / 180d;
            dc.DrawLine(beam, center, new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius));
        }
    }

    private static Geometry CreateArc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var start = PointAt(center, radius, startDegrees);
        var end = PointAt(center, radius, startDegrees + sweepDegrees);
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(start, false, false);
        ctx.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
    }
}
