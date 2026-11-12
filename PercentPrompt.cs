using System.Windows;
using System.Windows.Controls;

namespace BatteryTray;

/// <summary>Tiny dialog for the clearly-labelled manual fallback (e.g. Glorious mouse level read from CORE or the DPI-button LED).</summary>
public sealed class PercentPrompt : Window
{
    readonly TextBox _box = new() { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
    public int Value { get; private set; }
    public bool Cleared { get; private set; }

    public PercentPrompt(string device, int? current)
    {
        Title = "Set battery manually";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Enter the battery % you read elsewhere (for example in Glorious CORE) for \"{device}\". " +
                   "It is shown as a manual entry and is never used for notifications or history.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 340, Margin = new Thickness(0, 0, 0, 10),
        });
        _box.Text = current?.ToString() ?? "";
        panel.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Save", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var clear = new Button { Content = "Clear", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 3, 14, 3), IsCancel = true };

        ok.Click += (_, _) =>
        {
            if (int.TryParse(_box.Text.Trim().TrimEnd('%'), out int v) && v is >= 0 and <= 100) { Value = v; DialogResult = true; }
            else MessageBox.Show(this, "Enter a whole number from 0 to 100.", Title);
        };
        clear.Click += (_, _) => { Cleared = true; DialogResult = true; };

        buttons.Children.Add(ok); buttons.Children.Add(clear); buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
    }
}
