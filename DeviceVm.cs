using System.Windows;
using System.Windows.Media;

namespace BatteryTray;

/// <summary>Display model for one device card. Rebuilt on every refresh, so no change notification is needed.</summary>
public sealed class DeviceVm
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string SubTitle { get; init; } = "";
    public string PercentText { get; init; } = "";
    public double PercentFontSize { get; init; } = 22;
    public double BarValue { get; init; }
    public Brush BarBrush { get; init; } = Brushes.Gray;
    public string Detail { get; init; } = "";
    public PointCollection? Spark { get; init; }
    public bool CanSetManual { get; init; }

    public Visibility SubTitleVisibility => string.IsNullOrEmpty(SubTitle) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility BarVisibility => PercentText.EndsWith('%') ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SparkVisibility => Spark == null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ManualVisibility => CanSetManual ? Visibility.Visible : Visibility.Collapsed;

    static readonly Brush Red = Frozen("#D63031"), Amber = Frozen("#E69114"), Green = Frozen("#2EA043");
    static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    public static DeviceVm Build(DeviceReading d, AppSettings s, HistoryStore history, DateTime scanTime)
    {
        int? pct = d.Percent;
        bool manual = false;
        DateTime manualAt = default;
        if (pct == null && s.Manual.TryGetValue(d.Key, out var m)) { pct = m.Percent; manual = true; manualAt = m.At; }

        string status = d.Status ?? (d.Connected switch { true => "Connected", false => "Not connected", _ => "Status unknown" });
        var sub = new List<string>();
        if (s.ShowConnectionStatus) sub.Add(status);
        if (s.ShowTransport && !string.IsNullOrEmpty(d.Transport)) sub.Add(d.Transport);

        var det = new List<string>();
        if (manual) det.Add($"Manual entry, set {Ago(manualAt)}. Not read from the device.");
        else if (pct != null)
        {
            if (s.ShowSource && d.Source != null) det.Add($"Source: {d.Source}");
            if (s.ShowTimeRemaining)
            {
                var est = history.EstimateRemaining(d.Key);
                det.Add(est is TimeSpan ts ? $"Estimated time remaining: {Fmt(ts)}" : "Estimated time remaining: not enough data yet");
            }
        }
        else if (d.Note != null) det.Add(d.Note);
        if (s.ShowLastUpdated) det.Add($"Scanned {scanTime:HH:mm:ss}");

        PointCollection? spark = null;
        if (!manual && s.ShowHistory)
        {
            var pts = history.Get(d.Key, DateTime.Now.AddHours(-24));
            if (pts.Count >= 2)
            {
                double t0 = pts[0].T, span = Math.Max(1, pts[^1].T - t0);
                spark = new PointCollection(pts.Select(p => new Point((p.T - t0) / span * 300, 28 - p.P / 100.0 * 28)));
            }
        }

        int th = s.LowBatteryThreshold;
        return new DeviceVm
        {
            Key = d.Key,
            Name = d.Name,
            Glyph = d.Kind switch { DeviceKind.Mouse => "\uE962", DeviceKind.Controller => "\uE7FC", DeviceKind.Headset => "\uE7F6", _ => "\uE772" },
            SubTitle = string.Join("  ·  ", sub),
            PercentText = pct is int p ? $"{p}%" : "Unavailable",
            PercentFontSize = pct == null ? 14 : 22,
            BarValue = pct ?? 0,
            BarBrush = pct is int q ? (q <= th ? Red : q <= th + 15 ? Amber : Green) : Brushes.Gray,
            Detail = string.Join("\n", det),
            Spark = spark,
            CanSetManual = pct == null || manual,
        };
    }

    static string Ago(DateTime at)
    {
        var d = DateTime.Now - at;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }

    static string Fmt(TimeSpan ts) =>
        ts.TotalHours < 1 ? "under 1 h" :
        ts.TotalHours < 48 ? $"about {Math.Round(ts.TotalHours)} h" :
        $"about {Math.Round(ts.TotalDays, 1)} days";
}
