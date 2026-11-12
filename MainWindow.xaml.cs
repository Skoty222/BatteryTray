using System.ComponentModel;
using System.Windows;

namespace BatteryTray;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        Closing += OnClosing;
        Render();
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.Exiting) return;
        e.Cancel = true;   // closing the window keeps the app running in the tray
        Hide();
    }

    public void Render()
    {
        var s = App.Settings;
        var scanTime = App.Monitor.LastScan ?? DateTime.Now;
        var vms = App.Monitor.Devices
            .Where(d => !s.HiddenDevices.Contains(d.Key))
            .Select(d => DeviceVm.Build(d, s, App.History, scanTime))
            .ToList();

        DeviceList.ItemsSource = vms;
        EmptyText.Visibility = vms.Count == 0 && App.Monitor.LastScan != null ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = App.Monitor.LastScan is DateTime t
            ? $"Last scan {t:HH:mm:ss}  ·  automatic every {s.ScanIntervalSeconds} s"
            : "Scanning...";
    }

    async void OnRefresh(object sender, RoutedEventArgs e) => await App.Monitor.RefreshAsync();

    void OnSettings(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();

    void OnManual(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
        var name = App.Monitor.Devices.FirstOrDefault(d => d.Key == key)?.Name ?? key;
        int? current = App.Settings.Manual.TryGetValue(key, out var m) ? m.Percent : null;

        var prompt = new PercentPrompt(name, current) { Owner = this };
        if (prompt.ShowDialog() != true) return;

        if (prompt.Cleared) App.Settings.Manual.Remove(key);
        else App.Settings.Manual[key] = new ManualReading { Percent = prompt.Value, At = DateTime.Now };
        App.ApplySettings();
    }
}
