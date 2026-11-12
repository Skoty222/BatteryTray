using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace BatteryTray;

public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "BatteryTray";

    /// <summary>Per-user Run key: no admin rights, nothing left behind when turned off.</summary>
    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath;
                // Don't register "dotnet.exe" when launched via `dotnet run`.
                if (exe == null || !exe.EndsWith("BatteryTray.exe", StringComparison.OrdinalIgnoreCase)) return;
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Write("Startup registration failed: " + ex.Message); }
    }
}

public static class ThemeService
{
    public static bool IsDark { get; private set; }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Init()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && App.Settings.Theme == "System")
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply("System")));
        };
    }

    public static void Apply(string theme)
    {
        IsDark = theme == "Dark" || (theme == "System" && SystemUsesDark());
        var r = Application.Current.Resources;
        r["Bg"] = Brush(IsDark ? "#1F1F1F" : "#F3F3F3");
        r["Card"] = Brush(IsDark ? "#2B2B2B" : "#FFFFFF");
        r["Text"] = Brush(IsDark ? "#FFFFFF" : "#1B1B1B");
        r["SubText"] = Brush(IsDark ? "#A8A8A8" : "#5F5F5F");
        r["Border"] = Brush(IsDark ? "#3A3A3A" : "#E0E0E0");
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    public static void ApplyTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int v = IsDark ? 1 : 0;
            DwmSetWindowAttribute(h, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref v, sizeof(int));
        }
        catch { /* cosmetic only */ }
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static bool SystemUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
