using System.Runtime.InteropServices;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace BatteryTray;

/// <summary>Tray icon showing the lowest known battery %, tooltip listing all devices, right-click menu, balloon notifications.</summary>
public sealed class TrayService : IDisposable
{
    readonly WF.NotifyIcon _icon;
    SD.Icon? _current;

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

    public TrayService(Action open, Func<Task> refresh, Action exit)
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add("Refresh now", null, async (_, _) => await refresh());

        var startup = new WF.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        startup.Click += (_, _) => { App.Settings.StartWithWindows = startup.Checked; App.ApplySettings(); };
        menu.Items.Add(startup);
        menu.Opening += (_, _) => startup.Checked = App.Settings.StartWithWindows;

        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _current = MakeIcon(null);
        _icon = new WF.NotifyIcon { Icon = _current, Text = "Battery Tray: scanning...", Visible = true, ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) open(); };
    }

    public void Update(List<DeviceReading> devices)
    {
        var visible = devices.Where(d => !App.Settings.HiddenDevices.Contains(d.Key)).ToList();
        var known = visible.Where(d => d.Percent != null && d.Connected != false).ToList();
        int? lowest = known.Count > 0 ? known.Min(d => d.Percent!.Value) : null;

        var old = _current;
        _current = MakeIcon(lowest);
        _icon.Icon = _current;
        old?.Dispose();

        var lines = visible.Select(d =>
        {
            string n = d.Name.Length > 22 ? d.Name[..22] + "…" : d.Name;
            return $"{n}: {(d.Percent is int p ? p + "%" : "Unavailable")}";
        });
        string tip = visible.Count == 0 ? "Battery Tray: no devices" : string.Join("\n", lines);
        _icon.Text = tip.Length > 127 ? tip[..126] + "…" : tip;   // NotifyIcon limit
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(6000, title, text, WF.ToolTipIcon.Warning);

    static SD.Icon MakeIcon(int? pct)
    {
        int t = App.Settings.LowBatteryThreshold;
        SD.Color color = pct is null ? SD.Color.FromArgb(110, 110, 110)
                       : pct <= t ? SD.Color.FromArgb(214, 48, 49)
                       : pct <= t + 15 ? SD.Color.FromArgb(230, 145, 20)
                       : SD.Color.FromArgb(46, 160, 67);

        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
            using var fill = new SD.SolidBrush(color);
            using var path = RoundedRect(new SD.Rectangle(0, 0, 31, 31), 8);
            g.FillPath(fill, path);

            string text = pct?.ToString() ?? "?";
            using var font = new SD.Font("Segoe UI", text.Length >= 3 ? 14f : 20f, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            using var fmt = new SD.StringFormat { Alignment = SD.StringAlignment.Center, LineAlignment = SD.StringAlignment.Center };
            g.DrawString(text, font, SD.Brushes.White, new SD.RectangleF(0, 1, 32, 32), fmt);
        }

        IntPtr h = bmp.GetHicon();
        try { using var tmp = SD.Icon.FromHandle(h); return (SD.Icon)tmp.Clone(); }
        finally { DestroyIcon(h); }
    }

    static SD.Drawing2D.GraphicsPath RoundedRect(SD.Rectangle r, int d)
    {
        var p = new SD.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
