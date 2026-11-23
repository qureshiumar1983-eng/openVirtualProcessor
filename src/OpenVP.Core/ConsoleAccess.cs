using System.Security.Cryptography;
using System.Text;

namespace OpenVP.Core;

/// <summary>
/// Limits wrong passwords. 5 wrong passwords from one address lock that address out for 5 minutes; 30 from all addresses together
/// lock everyone out for 5 minutes. A correct password clears that address's count.
/// </summary>
public sealed class AuthThrottle
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _failures = new();
    private readonly List<DateTimeOffset> _all = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly int _perAddress, _global;
    private readonly TimeSpan _window, _lockout;

    public AuthThrottle(int perAddress = 5, int global = 30, TimeSpan? window = null, TimeSpan? lockout = null, Func<DateTimeOffset>? clock = null)
    {
        _perAddress = perAddress; _global = global;
        _window = window ?? TimeSpan.FromMinutes(5);
        _lockout = lockout ?? TimeSpan.FromMinutes(5);
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>How much longer this address is locked out (null if it isn't).</summary>
    public TimeSpan? LockedFor(string address)
    {
        lock (_gate)
        {
            var now = _now();
            Prune(now);
            TimeSpan? left = null;
            if (_failures.TryGetValue(address, out var f) && f.Count >= _perAddress) left = f[^1] + _lockout - now;
            if (_all.Count >= _global && _all[^1] + _lockout - now is var g && (left == null || g > left)) left = g;
            return left is { } l && l > TimeSpan.Zero ? l : null;
        }
    }

    public void Failed(string address)
    {
        lock (_gate)
        {
            var now = _now();
            Prune(now);
            if (!_failures.TryGetValue(address, out var f)) _failures[address] = f = new();
            f.Add(now);
            _all.Add(now);
        }
    }

    public void Succeeded(string address) { lock (_gate) _failures.Remove(address); }

    private void Prune(DateTimeOffset now)
    {
        // Failures count for the window; once an address hits its limit the lockout runs from its last failure.
        foreach (var (k, f) in _failures.ToList())
        {
            var keep = f.Count >= _perAddress ? _lockout : _window;
            f.RemoveAll(t => now - t > keep);
            if (f.Count == 0) _failures.Remove(k);
        }
        var gkeep = _all.Count >= _global ? _lockout : _window;
        _all.RemoveAll(t => now - t > gkeep);
    }
}

public sealed record ConsoleAuthResult(bool Ok, string? User = null, string? Message = null);

/// <summary>
/// How the telnet and SSH consoles check a login. Today the password is the OpenVP access key (<see cref="AccessKeyAuth"/>).
/// When user accounts exist, an implementation of this checks the user name and password and returns who it was; set
/// <see cref="RequiresUsername"/> to make telnet ask for a user name.
/// </summary>
public interface IConsoleAuth
{
    bool RequiresUsername { get; }
    ConsoleAuthResult Authenticate(string? user, string password, string remote);
}

/// <summary>The password is the access key. The user name is ignored. With the key switched off, nobody can log in.</summary>
public sealed class AccessKeyAuth : IConsoleAuth
{
    private readonly Func<string?> _key;
    private readonly AuthThrottle _throttle;

    public AccessKeyAuth(Func<string?> key, AuthThrottle? throttle = null)
    {
        _key = key;
        _throttle = throttle ?? new AuthThrottle();
    }

    public AuthThrottle Throttle => _throttle;
    public bool RequiresUsername => false;

    public ConsoleAuthResult Authenticate(string? user, string password, string remote)
    {
        if (_throttle.LockedFor(remote) is { } left)
            return new(false, null, $"Too many wrong passwords. Try again in {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} minute(s).");
        var key = _key();
        if (string.IsNullOrEmpty(key)) return new(false, null, "The console needs the access key, and it is switched off (OpenVP:ApiKey=off).");
        var ok = !string.IsNullOrEmpty(password)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(password));
        if (!ok) { _throttle.Failed(remote); return new(false, null, "Wrong password."); }
        _throttle.Succeeded(remote);
        return new(true, string.IsNullOrWhiteSpace(user) ? "admin" : user.Trim());
    }
}

/// <summary>Caps how many console sessions are open: <see cref="MaxTotal"/> altogether, <see cref="MaxPerAddress"/> from one address.</summary>
public sealed class ConsoleGate
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _byAddress = new();
    private int _total;

    public int MaxTotal { get; init; } = 5;
    public int MaxPerAddress { get; init; } = 3;
    public int Open { get { lock (_gate) return _total; } }

    public bool TryEnter(string address, out IDisposable lease)
    {
        lock (_gate)
        {
            _byAddress.TryGetValue(address, out var n);
            if (_total >= MaxTotal || n >= MaxPerAddress) { lease = NullLease.Instance; return false; }
            _total++;
            _byAddress[address] = n + 1;
        }
        lease = new Lease(this, address);
        return true;
    }

    private void Leave(string address)
    {
        lock (_gate)
        {
            _total = Math.Max(0, _total - 1);
            if (_byAddress.TryGetValue(address, out var n)) { if (n <= 1) _byAddress.Remove(address); else _byAddress[address] = n - 1; }
        }
    }

    private sealed class NullLease : IDisposable { public static readonly NullLease Instance = new(); public void Dispose() { } }

    private sealed class Lease(ConsoleGate gate, string address) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) gate.Leave(address); }
    }
}
