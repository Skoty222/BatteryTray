using System.Text.Json;

namespace BatteryTray;

public enum DeviceKind { Mouse, Controller, Headset, Other }

/// <summary>One device as seen by the latest scan. Percent is null whenever Windows/the device gives no reliable level.</summary>
public sealed class DeviceReading
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceKind Kind { get; set; }
    public string Transport { get; set; } = "";
    public bool? Connected { get; set; }
    public string? Status { get; set; }      // overrides the Connected/Not connected text (e.g. "Receiver detected")
    public int? Percent { get; set; }
    public string? Source { get; set; }      // where the number came from
    public string? Note { get; set; }        // why the level is unavailable
}

public sealed class ManualReading
{
    public int Percent { get; set; }
    public DateTime At { get; set; }
}

public sealed class AppSettings
{
    public int LowBatteryThreshold { get; set; } = 15;
    public bool NotificationsEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public string Theme { get; set; } = "System";          // System | Light | Dark
    public int ScanIntervalSeconds { get; set; } = 60;

    public bool ShowConnectionStatus { get; set; } = true;
    public bool ShowTransport { get; set; } = true;
    public bool ShowSource { get; set; } = true;
    public bool ShowLastUpdated { get; set; } = true;
    public bool ShowTimeRemaining { get; set; } = true;
    public bool ShowHistory { get; set; } = true;

    public List<string> HiddenDevices { get; set; } = new();
    public Dictionary<string, ManualReading> Manual { get; set; } = new();

    /// <summary>Extra USB receivers to list, as "VID_xxxx" or "VID_xxxx&amp;PID_yyyy" (find them with --diagnose).</summary>
    public List<string> ExtraUsbIds { get; set; } = new();

    static string FilePath => Path.Combine(AppPaths.Dir, "settings.json");

    public static AppSettings Load(out bool firstRun)
    {
        firstRun = !File.Exists(FilePath);
        if (!firstRun)
        {
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) { s.Normalize(); return s; }
            }
            catch (Exception ex) { Log.Write("Settings load failed: " + ex.Message); }
        }
        return new AppSettings();
    }

    public void Normalize()
    {
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 1, 90);
        ScanIntervalSeconds = Math.Clamp(ScanIntervalSeconds, 15, 3600);
        if (Theme is not ("System" or "Light" or "Dark")) Theme = "System";
        HiddenDevices ??= new(); Manual ??= new(); ExtraUsbIds ??= new();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Write("Settings save failed: " + ex.Message); }
    }
}

public static class AppPaths
{
    public static readonly string Dir = Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryTray")).FullName;
}

public static class Log
{
    static readonly object Gate = new();
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = Path.Combine(AppPaths.Dir, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:u} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
