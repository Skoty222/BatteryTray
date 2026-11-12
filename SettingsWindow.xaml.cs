using System.Windows;
using System.Windows.Controls;

namespace BatteryTray;

public partial class SettingsWindow : Window
{
    static readonly string[] Themes = { "System", "Light", "Dark" };
    static readonly int[] Intervals = { 30, 60, 120, 300 };

    public SettingsWindow()
    {
        InitializeComponent();
        var s = App.Settings;

        NotifyBox.IsChecked = s.NotificationsEnabled;
        ThresholdSlider.Value = Math.Clamp(s.LowBatteryThreshold, 5, 50);
        ThresholdText.Text = $"{(int)ThresholdSlider.Value}%";
        ThresholdSlider.ValueChanged += (_, _) => ThresholdText.Text = $"{(int)ThresholdSlider.Value}%";

        StartupBox.IsChecked = s.StartWithWindows;
        ThemeBox.SelectedIndex = Math.Max(0, Array.IndexOf(Themes, s.Theme));
        IntervalBox.SelectedIndex = Math.Max(0, Array.IndexOf(Intervals, s.ScanIntervalSeconds));

        StatusBox.IsChecked = s.ShowConnectionStatus;
        TransportBox.IsChecked = s.ShowTransport;
        SourceBox.IsChecked = s.ShowSource;
        RemainingBox.IsChecked = s.ShowTimeRemaining;
        HistoryBox.IsChecked = s.ShowHistory;
        UpdatedBox.IsChecked = s.ShowLastUpdated;

        foreach (var d in App.Monitor.Devices)
            DevicePanel.Children.Add(new CheckBox
            {
                Content = $"{d.Name}  ({d.Transport})",
                Tag = d.Key,
                IsChecked = !s.HiddenDevices.Contains(d.Key),
            });
        if (DevicePanel.Children.Count == 0) NoDevicesText.Visibility = Visibility.Visible;

        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        Closing += (_, _) => Save();
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();

    void Save()
    {
        var s = App.Settings;
        s.NotificationsEnabled = NotifyBox.IsChecked == true;
        s.LowBatteryThreshold = (int)ThresholdSlider.Value;
        s.StartWithWindows = StartupBox.IsChecked == true;
        s.Theme = Themes[Math.Max(0, ThemeBox.SelectedIndex)];
        s.ScanIntervalSeconds = Intervals[Math.Max(0, IntervalBox.SelectedIndex)];

        s.ShowConnectionStatus = StatusBox.IsChecked == true;
        s.ShowTransport = TransportBox.IsChecked == true;
        s.ShowSource = SourceBox.IsChecked == true;
        s.ShowTimeRemaining = RemainingBox.IsChecked == true;
        s.ShowHistory = HistoryBox.IsChecked == true;
        s.ShowLastUpdated = UpdatedBox.IsChecked == true;

        var listed = DevicePanel.Children.OfType<CheckBox>().ToList();
        var listedKeys = listed.Select(c => (string)c.Tag).ToHashSet();
        var hidden = s.HiddenDevices.Where(k => !listedKeys.Contains(k)).ToList();   // keep hidden devices not currently seen
        hidden.AddRange(listed.Where(c => c.IsChecked != true).Select(c => (string)c.Tag));
        s.HiddenDevices = hidden;

        App.ApplySettings();
    }
}
