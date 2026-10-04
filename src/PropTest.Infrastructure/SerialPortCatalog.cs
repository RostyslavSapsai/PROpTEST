using System.IO.Ports;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PropTest.Infrastructure;

public sealed record SerialPortInfo(string PortName, string Description, bool IsArduino);
public static class SerialPortCatalog
{
    public static bool IsCandidateUsbId(string id) => new[] { "VID_2341&", "VID_2A03&", "VID_303A&", "VID_10C4&PID_EA60", "VID_1A86&PID_7523", "VID_1A86&PID_55D4", "VID_1A86&PID_55D3" }
        .Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    public static IReadOnlyList<SerialPortInfo> List()
    {
        var names = SerialPort.GetPortNames().Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var known = OperatingSystem.IsWindows() ? WindowsArduinoPorts() : new Dictionary<string,string>();
        return names.Select(name => new SerialPortInfo(name, known.GetValueOrDefault(name) ?? name, known.ContainsKey(name))).ToArray();
    }
    [SupportedOSPlatform("windows")]
    static Dictionary<string,string> WindowsArduinoPorts()
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb is null) return result;
        foreach (var deviceName in usb.GetSubKeyNames())
        {
            // USB identity is only a discovery hint; the firmware handshake is still mandatory.
            if (!IsCandidateUsbId(deviceName)) continue;
            using var device = usb.OpenSubKey(deviceName);
            if (device is null) continue;
            foreach (var instanceName in device.GetSubKeyNames())
            {
                using var instance = device.OpenSubKey(instanceName);
                using var parameters = instance?.OpenSubKey("Device Parameters");
                if (parameters?.GetValue("PortName") is string port)
                    result[port] = instance?.GetValue("FriendlyName") as string ?? "Arduino · " + port;
            }
        }
        return result;
    }
}

// One attempt per attachment; manual disconnect suppresses attempts until the board is reattached.
public sealed class AutoConnectionTracker
{
    readonly HashSet<string> attempted = new(StringComparer.OrdinalIgnoreCase);
    public void Observe(IReadOnlyList<SerialPortInfo> ports) => attempted.RemoveWhere(name => !ports.Any(p => p.PortName.Equals(name, StringComparison.OrdinalIgnoreCase)));
    public void Suppress(string? port) { if (port is not null) attempted.Add(port); }
    public void Retry(string? port) { if (port is not null) attempted.Remove(port); }
    public string? Next(IReadOnlyList<SerialPortInfo> ports)
    {
        var next = ports.FirstOrDefault(p => p.IsArduino && !attempted.Contains(p.PortName));
        if (next is null) return null;
        attempted.Add(next.PortName); return next.PortName;
    }
}
