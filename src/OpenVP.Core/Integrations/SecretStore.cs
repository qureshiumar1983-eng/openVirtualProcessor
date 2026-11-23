using System.Runtime.InteropServices;
using System.Text;

namespace OpenVP.Core.Integrations;

/// <summary>
/// Keeps secrets (the MQTT password) in one small file, <c>data/integrations/secrets.json</c>. On Windows each value is
/// encrypted with DPAPI for the current user (so the file is useless on another account or machine); elsewhere the
/// values are stored as they are and the file is readable only by its owner, like <c>apikey.txt</c>. Values are never
/// returned by the API. If DPAPI fails the value is stored plainly rather than lost.
/// </summary>
public sealed class SecretStore
{
    private const string Prefix = "dpapi:";
    private readonly string _file;
    private readonly object _gate = new();
    private Dictionary<string, string> _values;
    private readonly Func<string, string?> _protect;
    private readonly Func<string, string?> _unprotect;

    public SecretStore(string dataDirectory) : this(dataDirectory, Dpapi.Protect, Dpapi.Unprotect) { }

    /// <summary>For tests: supply the protection functions (they return null when they can't).</summary>
    public SecretStore(string dataDirectory, Func<string, string?> protect, Func<string, string?> unprotect)
    {
        _file = Path.Combine(dataDirectory, "integrations", "secrets.json");
        _protect = protect;
        _unprotect = unprotect;
        _values = IntegrationFiles.Load<Dictionary<string, string>>(_file);
    }

    public string FilePath => _file;

    public bool Has(string name) { lock (_gate) return _values.TryGetValue(name, out var v) && v.Length > 0; }

    public string? Get(string name)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(name, out var v) || v.Length == 0) return null;
            if (!v.StartsWith(Prefix, StringComparison.Ordinal)) return v;
            return _unprotect(v[Prefix.Length..]);
        }
    }

    /// <summary>Saves a value; null or empty removes it.</summary>
    public void Set(string name, string? value)
    {
        lock (_gate)
        {
            var next = new Dictionary<string, string>(_values);
            if (string.IsNullOrEmpty(value)) next.Remove(name);
            else next[name] = _protect(value) is { } p ? Prefix + p : value;
            IntegrationFiles.Save(_file, next, ownerOnly: true);
            _values = next;
        }
    }

    /// <summary>Windows DPAPI through crypt32 (no package needed).</summary>
    private static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob { public int cbData; public IntPtr pbData; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        private static string? Run(byte[] input, bool protect)
        {
            if (!OperatingSystem.IsWindows()) return null;
            var handle = GCHandle.Alloc(input, GCHandleType.Pinned);
            try
            {
                var inBlob = new DataBlob { cbData = input.Length, pbData = handle.AddrOfPinnedObject() };
                var ok = protect ? CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var o1) : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out o1);
                if (!ok) return null;
                try
                {
                    var bytes = new byte[o1.cbData];
                    Marshal.Copy(o1.pbData, bytes, 0, o1.cbData);
                    return protect ? Convert.ToBase64String(bytes) : Encoding.UTF8.GetString(bytes);
                }
                finally { LocalFree(o1.pbData); }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or ArgumentException) { return null; }
            finally { handle.Free(); }
        }

        public static string? Protect(string plain) => Run(Encoding.UTF8.GetBytes(plain), true);
        public static string? Unprotect(string b64)
        {
            try { return Run(Convert.FromBase64String(b64), false); }
            catch (FormatException) { return null; }
        }
    }
}
