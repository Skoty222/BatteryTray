using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace BatteryTray;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static HistoryStore History { get; } = new();
    public static MonitorService Monitor { get; private set; } = null!;
    public static TrayService? Tray { get; private set; }
    public static bool Exiting { get; private set; }

    static App Instance => (App)Current;
    Mutex? _mutex;
    MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = AppSettings.Load(out _);

        // BatteryTray.exe --diagnose : dump what Windows exposes for your devices to a text file, then exit.
        if (e.Args.Contains("--diagnose"))
        {
            try
            {
                var path = Path.Combine(AppPaths.Dir, "diagnostics.txt");
                File.WriteAllText(path, await new DeviceScanner().DumpAsync(Settings));
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("Diagnose failed: " + ex); }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\BatteryTray.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }   // already running: use its tray icon

        History.Load();
        ThemeService.Init();
        ThemeService.Apply(Settings.Theme);
        StartupService.Apply(Settings.StartWithWindows);

        Monitor = new MonitorService();
        Tray = new TrayService(ShowWindow, () => Monitor.RefreshAsync(), ExitApp);
        Monitor.Updated += () => { Tray?.Update(Monitor.Devices); _window?.Render(); };
        Monitor.Start();

        if (!e.Args.Contains("--tray")) ShowWindow();   // autostart launches with --tray (no window)
    }

    public static void ShowWindow()
    {
        var app = Instance;
        app._window ??= new MainWindow();
        app._window.Show();
        if (app._window.WindowState == WindowState.Minimized) app._window.WindowState = WindowState.Normal;
        app._window.Activate();
    }

    /// <summary>Call after changing App.Settings: saves, then re-applies theme, startup, timer, tray and window.</summary>
    public static void ApplySettings()
    {
        Settings.Normalize();
        Settings.Save();
        ThemeService.Apply(Settings.Theme);
        StartupService.Apply(Settings.StartWithWindows);
        Monitor.RestartTimer();
        Tray?.Update(Monitor.Devices);
        Instance._window?.Render();
    }

    public static void ExitApp()
    {
        Exiting = true;
        History.SaveIfDirty();
        Tray?.Dispose();
        Instance._mutex?.ReleaseMutex();
        Instance.Shutdown();
    }
}
