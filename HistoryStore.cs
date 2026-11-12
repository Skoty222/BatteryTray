using System.Text.Json;

namespace BatteryTray;

public sealed class HistoryPoint
{
    public long T { get; set; }   // unix seconds
    public int P { get; set; }    // percent
}

/// <summary>Stores only real readings (never manual ones). Kept 30 days, written to %AppData%\BatteryTray\history.json.</summary>
public sealed class HistoryStore
{
    static string FilePath => Path.Combine(AppPaths.Dir, "history.json");
    Dictionary<string, List<HistoryPoint>> _data = new();
    bool _dirty;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _data = JsonSerializer.Deserialize<Dictionary<string, List<HistoryPoint>>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) { Log.Write("History load failed: " + ex.Message); _data = new(); }
    }

    public void SaveIfDirty()
    {
        if (!_dirty) return;
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_data)); _dirty = false; }
        catch (Exception ex) { Log.Write("History save failed: " + ex.Message); }
    }

    public void Record(string key, int pct, DateTime now)
    {
        long t = new DateTimeOffset(now).ToUnixTimeSeconds();
        if (!_data.TryGetValue(key, out var list)) { list = new List<HistoryPoint>(); _data[key] = list; }
        if (list.Count > 0)
        {
            var last = list[^1];
            if (last.P == pct && t - last.T < 30 * 60) return;   // unchanged: keep at most one point per 30 min
        }
        list.Add(new HistoryPoint { T = t, P = pct });
        long cutoff = t - 30L * 24 * 3600;
        list.RemoveAll(p => p.T < cutoff);
        _dirty = true;
    }

    public List<HistoryPoint> Get(string key, DateTime since)
    {
        long s = new DateTimeOffset(since).ToUnixTimeSeconds();
        return _data.TryGetValue(key, out var l) ? l.Where(p => p.T >= s).ToList() : new List<HistoryPoint>();
    }

    /// <summary>
    /// Linear estimate from the current discharge run (back to the last charge or a gap of 12 h+).
    /// Returns null unless there are >= 3 points, >= 30 min, and a drop of >= 2 points - never a guess.
    /// </summary>
    public TimeSpan? EstimateRemaining(string key)
    {
        if (!_data.TryGetValue(key, out var pts) || pts.Count < 3) return null;
        int start = pts.Count - 1;
        while (start > 0
               && pts[start - 1].P >= pts[start].P - 1
               && pts[start].T - pts[start - 1].T <= 12 * 3600)
            start--;
        var first = pts[start];
        var last = pts[^1];
        double hours = (last.T - first.T) / 3600.0;
        int drop = first.P - last.P;
        if (hours < 0.5 || drop < 2) return null;
        double remaining = last.P / (drop / hours);
        return TimeSpan.FromHours(Math.Min(remaining, 999));
    }
}
