using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PropTest.Infrastructure;

public sealed record SavedBoard(string Hostname, string Name, string LastAddress, string ProtectedKey, bool AutoConnect)
{
    public override string ToString() => Name;
}

public sealed class SavedBoardStore(string path)
{
    public string? SelectedHostname { get; set; }
    public List<SavedBoard> Boards { get; private set; } = [];
    public string LoadError { get; private set; } = "";
    sealed record FileData(int Version, string? SelectedHostname, List<SavedBoard> Boards);
    public void Load()
    {
        if (!File.Exists(path)) return;
        try {
            var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path)) ?? throw new JsonException();
            if (data.Version != 1 || data.Boards is null || data.Boards.Any(b => b is null || !ValidHost(b.Hostname))) throw new JsonException();
            Boards = data.Boards; SelectedHostname = data.SelectedHostname;
        } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) {
            LoadError = "Не вдалося прочитати збережені плати. Файл не перезаписано: " + path;
        }
    }
    public static bool ValidHost(string? host) => host is not null && host.Length > 18 && host.StartsWith("proptest-v2-", StringComparison.Ordinal)
        && host.EndsWith(".local", StringComparison.Ordinal) && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.');
    public void Save()
    {
        if (LoadError.Length > 0) throw new IOException(LoadError);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new FileData(1, SelectedHostname, Boards), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
    public SavedBoard Remember(string hostname, string address, string key)
    {
        if (!ValidHost(hostname)) throw new ArgumentException("Плата не повідомила постійне ім’я.");
        var old = Boards.Find(b => b.Hostname == hostname);
        var next = new SavedBoard(hostname, old?.Name ?? "PROpTEST · " + hostname[12..^6], address,
            key.Length > 0 ? LocalBoardSecret.Protect(key) : old?.ProtectedKey ?? "", old?.AutoConnect ?? true);
        Boards.RemoveAll(b => b.Hostname == hostname); Boards.Add(next); SelectedHostname = hostname; Save(); return next;
    }
    public void Replace(SavedBoard value)
    {
        var index = Boards.FindIndex(b => b.Hostname == value.Hostname);
        if (index < 0) throw new ArgumentException("Плату не збережено.");
        Boards[index] = value; Save();
    }
}

// Windows account-bound DPAPI; never fall back to plaintext on other platforms.
public static class LocalBoardSecret
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptProtectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
    static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Запам’ятовування доступу наразі підтримується у Windows.");
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        } finally {
            for (int n = 0; n < input.Length; n++) Marshal.WriteByte(input.Data, n, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
    public static string Protect(string key) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(key), true));
    public static string Unprotect(string value) => value.Length == 0 ? "" : Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));
}
