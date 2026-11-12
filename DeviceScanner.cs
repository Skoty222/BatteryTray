using System.Text;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Gaming.Input;
using Windows.Storage.Streams;

namespace BatteryTray;

/// <summary>
/// Finds devices and reads batteries ONLY through interfaces Windows itself exposes:
///  1. Bluetooth (classic + LE): the Windows PnP battery property (the one Settings > Bluetooth uses),
///     then the standard Bluetooth LE Battery Service for LE devices.
///  2. Game controllers: Windows.Gaming.Input battery report.
///  3. USB receivers (HID mice): presence only. No battery is read from them - nothing documented exists.
/// Anything else is reported as Unavailable. Nothing here writes to a device.
/// </summary>
public sealed class DeviceScanner
{
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";   // DEVPKEY_Device_BatteryLevel
    const string PropConnected = "System.Devices.Aep.IsConnected";
    const string PropContainer = "System.Devices.Aep.ContainerId";
    const string PropCodMajor = "System.Devices.Aep.Bluetooth.Cod.Major";

    const string GloriousVid = "258A";   // vendor id seen on Glorious receivers; heuristic only - confirm with --diagnose

    public DeviceScanner()
    {
        _ = RawGameController.RawGameControllers.Count;   // touch early: Windows fills this list asynchronously
    }

    public async Task<List<DeviceReading>> ScanAsync(AppSettings settings)
    {
        var all = new List<DeviceReading>();
        all.AddRange(await ScanBluetoothAsync());
        all.AddRange(await ScanUsbReceiversAsync(settings));
        all.AddRange(ScanControllers());
        return Merge(all).OrderBy(d => d.Kind).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- Bluetooth ----------

    async Task<List<DeviceReading>> ScanBluetoothAsync()
    {
        var list = new List<DeviceReading>();
        var requested = new[] { PropConnected, PropContainer, PropCodMajor, BatteryKey };
        var sources = new[]
        {
            (Selector: BluetoothDevice.GetDeviceSelectorFromPairingState(true), Le: false),
            (Selector: BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), Le: true),
        };

        foreach (var src in sources)
        {
            DeviceInformationCollection infos;
            try { infos = await DeviceInformation.FindAllAsync(src.Selector, requested); }
            catch (Exception ex) { Log.Write("Bluetooth enumeration failed: " + ex.Message); continue; }

            foreach (var info in infos)
            {
                if (string.IsNullOrWhiteSpace(info.Name)) continue;
                bool? connected = Prop(info, PropConnected) as bool?;
                int? pct = null;
                string? source = null;

                if (connected != false)
                {
                    pct = ToPercent(Prop(info, BatteryKey));
                    if (pct != null) source = "Windows Bluetooth battery property";

                    if (pct == null && connected == true)
                    {
                        pct = await ContainerBatteryAsync(info);
                        if (pct != null) source = "Windows device battery property";
                    }
                    if (pct == null && connected == true && src.Le)
                    {
                        pct = await WithTimeout(GattBatteryAsync(info.Id), 8000);
                        if (pct != null) source = "Bluetooth LE Battery Service";
                    }
                }

                list.Add(new DeviceReading
                {
                    Key = (src.Le ? "ble:" : "bt:") + info.Id,
                    Name = info.Name,
                    Kind = GuessKind(info.Name, ToUInt(Prop(info, PropCodMajor))),
                    Transport = src.Le ? "Bluetooth LE" : "Bluetooth",
                    Connected = connected,
                    Percent = pct,
                    Source = source,
                    Note = pct == null
                        ? (connected == false ? "Not connected." : "This device doesn't report a battery level to Windows.")
                        : null,
                });
            }
        }
        return list;
    }

    static async Task<int?> ContainerBatteryAsync(DeviceInformation aep)
    {
        if (Prop(aep, PropContainer) is not Guid cid) return null;
        try
        {
            var nodes = await DeviceInformation.FindAllAsync(
                $"System.Devices.ContainerId:=\"{cid:B}\"", new[] { BatteryKey }, DeviceInformationKind.Device);
            foreach (var n in nodes)
            {
                var p = ToPercent(Prop(n, BatteryKey));
                if (p != null) return p;
            }
        }
        catch (Exception ex) { Log.Write("Container battery query failed: " + ex.Message); }
        return null;
    }

    static async Task<int?> GattBatteryAsync(string deviceId)
    {
        try
        {
            using var dev = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (dev == null) return null;
            var svcs = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
            if (svcs.Status != GattCommunicationStatus.Success || svcs.Services.Count == 0) return null;
            using var svc = svcs.Services[0];
            var chars = await svc.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0) return null;
            var read = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
            if (read.Status != GattCommunicationStatus.Success) return null;
            return ToPercent(DataReader.FromBuffer(read.Value).ReadByte());
        }
        catch (Exception ex) { Log.Write("GATT battery read failed: " + ex.Message); return null; }
    }

    static async Task<int?> WithTimeout(Task<int?> task, int ms)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        return done == task ? await task : null;
    }

    // ---------- USB receivers (presence only) ----------

    async Task<List<DeviceReading>> ScanUsbReceiversAsync(AppSettings settings)
    {
        var list = new List<DeviceReading>();
        DeviceInformationCollection infos;
        try { infos = await DeviceInformation.FindAllAsync(HidDevice.GetDeviceSelector(0x01, 0x02)); }   // generic desktop / mouse
        catch (Exception ex) { Log.Write("HID enumeration failed: " + ex.Message); return list; }

        var seen = new HashSet<string>();
        foreach (var info in infos)
        {
            var m = Regex.Match(info.Id.ToUpperInvariant(), @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})");
            if (!m.Success) continue;
            string vid = m.Groups[1].Value, pid = m.Groups[2].Value;

            bool glorious = vid == GloriousVid;
            bool extra = settings.ExtraUsbIds.Any(x =>
                x.Equals($"VID_{vid}", StringComparison.OrdinalIgnoreCase) ||
                x.Equals($"VID_{vid}&PID_{pid}", StringComparison.OrdinalIgnoreCase));
            if (!glorious && !extra) continue;          // wired mice also show up here, so only list known wireless receivers
            if (!seen.Add($"{vid}:{pid}")) continue;

            list.Add(new DeviceReading
            {
                Key = $"usb:VID_{vid}&PID_{pid}",
                Name = glorious ? "Glorious mouse (2.4 GHz receiver)" : $"USB mouse receiver ({vid}:{pid})",
                Kind = DeviceKind.Mouse,
                Transport = "USB receiver",
                Connected = true,
                Status = "Receiver detected",
                Percent = null,
                Note = glorious
                    ? "Windows doesn't expose this mouse's battery and Glorious documents no public interface for it. Check Glorious CORE or hold the DPI button."
                    : "Windows doesn't expose a battery level for this receiver.",
            });
        }
        return list;
    }

    // ---------- Game controllers ----------

    static List<DeviceReading> ScanControllers()
    {
        var list = new List<DeviceReading>();
        foreach (var c in RawGameController.RawGameControllers)
        {
            int? pct = null;
            try
            {
                var rep = c.TryGetBatteryReport();
                if (rep?.FullChargeCapacityInMilliwattHours is int full && full > 0 &&
                    rep.RemainingCapacityInMilliwattHours is int rem)
                    pct = Math.Clamp((int)Math.Round(rem * 100.0 / full), 0, 100);
            }
            catch (Exception ex) { Log.Write("Controller battery failed: " + ex.Message); }

            list.Add(new DeviceReading
            {
                Key = "gc:" + c.NonRoamableId,
                Name = string.IsNullOrWhiteSpace(c.DisplayName) ? "Game controller" : c.DisplayName,
                Kind = DeviceKind.Controller,
                Transport = "Game controller",
                Connected = true,
                Percent = pct,
                Source = pct != null ? "Windows game controller battery report" : null,
                Note = pct == null ? "Windows reports no battery for this controller (wired, or not supported)." : null,
            });
        }
        return list;
    }

    // ---------- helpers ----------

    /// <summary>Same device seen via several routes (e.g. a paired controller shows up as Bluetooth AND game controller) -> one row.</summary>
    static List<DeviceReading> Merge(IEnumerable<DeviceReading> all)
    {
        var result = new List<DeviceReading>();
        foreach (var g in all.GroupBy(d => Regex.Replace(d.Name.ToLowerInvariant(), @"\s+", " ").Trim()))
        {
            var items = g.ToList();
            var primary = items.FirstOrDefault(d => d.Key.StartsWith("bt:") || d.Key.StartsWith("ble:")) ?? items[0];
            foreach (var other in items)
            {
                if (ReferenceEquals(other, primary)) continue;
                if (primary.Percent == null && other.Percent != null)
                {
                    primary.Percent = other.Percent; primary.Source = other.Source; primary.Note = null;
                }
                if (other.Connected == true && primary.Connected != true) primary.Connected = true;
            }
            result.Add(primary);
        }
        return result;
    }

    static DeviceKind GuessKind(string name, uint? codMajor)
    {
        var n = name.ToLowerInvariant();
        if (Regex.IsMatch(n, "headset|headphone|earbud|buds|airpods|arctis|cloud|wh-|wf-")) return DeviceKind.Headset;
        if (Regex.IsMatch(n, "controller|gamepad|xbox|dualsense|dualshock|joy-con")) return DeviceKind.Controller;
        if (Regex.IsMatch(n, @"mouse|mice|trackball|model [od]|mx master|mx anywhere|deathadder|viper")) return DeviceKind.Mouse;
        if (codMajor == 4) return DeviceKind.Headset;
        return DeviceKind.Other;
    }

    static object? Prop(DeviceInformation i, string key) => i.Properties.TryGetValue(key, out var v) ? v : null;

    static int? ToPercent(object? o)
    {
        if (o == null) return null;
        try { int v = Convert.ToInt32(o); return v is >= 0 and <= 100 ? v : null; }
        catch { return null; }
    }

    static uint? ToUInt(object? o)
    {
        if (o == null) return null;
        try { return Convert.ToUInt32(o); } catch { return null; }
    }

    // ---------- diagnostics (run: BatteryTray.exe --diagnose) ----------

    public async Task<string> DumpAsync(AppSettings settings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"BatteryTray diagnostics  {DateTime.Now:u}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine();

        var requested = new[] { PropConnected, PropContainer, PropCodMajor, BatteryKey, "System.Devices.Aep.IsPaired" };
        foreach (var (label, selector) in new[]
        {
            ("Bluetooth classic (paired)", BluetoothDevice.GetDeviceSelectorFromPairingState(true)),
            ("Bluetooth LE (paired)", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)),
        })
        {
            sb.AppendLine($"=== {label} ===");
            try
            {
                foreach (var info in await DeviceInformation.FindAllAsync(selector, requested))
                {
                    sb.AppendLine($"* {info.Name}  |  {info.Id}");
                    foreach (var kv in info.Properties) sb.AppendLine($"    {kv.Key} = {kv.Value ?? "(null)"}");
                    if (Prop(info, PropContainer) is Guid cid)
                    {
                        try
                        {
                            var nodes = await DeviceInformation.FindAllAsync(
                                $"System.Devices.ContainerId:=\"{cid:B}\"", new[] { BatteryKey }, DeviceInformationKind.Device);
                            foreach (var n in nodes)
                                sb.AppendLine($"    devnode: {n.Name} | battery prop = {Prop(n, BatteryKey) ?? "(none)"}");
                        }
                        catch (Exception ex) { sb.AppendLine("    devnode query failed: " + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("  failed: " + ex.Message); }
            sb.AppendLine();
        }

        sb.AppendLine("=== HID mice (find your receiver's VID/PID here) ===");
        try
        {
            foreach (var info in await DeviceInformation.FindAllAsync(HidDevice.GetDeviceSelector(0x01, 0x02)))
                sb.AppendLine($"* {info.Name}  |  {info.Id}");
        }
        catch (Exception ex) { sb.AppendLine("  failed: " + ex.Message); }
        sb.AppendLine();

        await Task.Delay(1500);
        sb.AppendLine("=== Game controllers ===");
        foreach (var c in RawGameController.RawGameControllers)
        {
            string rep = "no report";
            try
            {
                var r = c.TryGetBatteryReport();
                if (r != null) rep = $"remaining={r.RemainingCapacityInMilliwattHours} mWh, full={r.FullChargeCapacityInMilliwattHours} mWh, status={r.Status}";
            }
            catch (Exception ex) { rep = "error: " + ex.Message; }
            sb.AppendLine($"* {c.DisplayName}  |  {c.NonRoamableId}  |  {rep}");
        }
        sb.AppendLine();

        sb.AppendLine("=== What the app would show ===");
        foreach (var d in await ScanAsync(settings))
            sb.AppendLine($"* {d.Name} [{d.Kind}, {d.Transport}] connected={d.Connected} battery={(d.Percent is int pc ? pc + "%" : "Unavailable")} source={d.Source ?? "-"}");
        return sb.ToString();
    }
}
