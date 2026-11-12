using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Gaming.Input;

namespace BatteryTray;

/// <summary>Scans on a timer (and on controller plug/unplug and resume from sleep), records history, raises low-battery notifications.</summary>
public sealed class MonitorService
{
    readonly DeviceScanner _scanner = new();
    readonly DispatcherTimer _timer = new();
    readonly HashSet<string> _notified = new();
    bool _busy;

    public List<DeviceReading> Devices { get; private set; } = new();
    public DateTime? LastScan { get; private set; }
    public event Action? Updated;

    public MonitorService()
    {
        _timer.Tick += (_, _) => _ = RefreshAsync();
        RawGameController.RawGameControllerAdded += (_, _) => Poke(500);
        RawGameController.RawGameControllerRemoved += (_, _) => Poke(500);
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) Poke(5000); };
    }

    public void Start()
    {
        RestartTimer();
        _ = RefreshAsync(1500);   // small delay so Windows can populate the controller list first
    }

    public void RestartTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(15, App.Settings.ScanIntervalSeconds));
        _timer.Start();
    }

    void Poke(int delayMs) =>
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync(delayMs)));

    public async Task RefreshAsync(int delayMs = 0)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs);
            var list = await _scanner.ScanAsync(App.Settings);
            var now = DateTime.Now;

            foreach (var d in list)
                if (d.Percent is int p && d.Connected != false) App.History.Record(d.Key, p, now);
            App.History.SaveIfDirty();

            Devices = list;
            LastScan = now;
            CheckNotifications(list);
            Updated?.Invoke();
        }
        catch (Exception ex) { Log.Write("Scan failed: " + ex); }
        finally { _busy = false; }
    }

    void CheckNotifications(List<DeviceReading> list)
    {
        var s = App.Settings;
        foreach (var d in list)
        {
            if (s.HiddenDevices.Contains(d.Key)) continue;
            if (d.Percent is not int p || d.Connected == false) continue;   // only real, current readings

            if (p <= s.LowBatteryThreshold)
            {
                if (_notified.Add(d.Key) && s.NotificationsEnabled)
                    App.Tray?.Notify($"{d.Name}: battery low", $"{p}% remaining.");
            }
            else if (p > s.LowBatteryThreshold + 3)
            {
                _notified.Remove(d.Key);   // re-arm after it has been charged
            }
        }
    }
}
