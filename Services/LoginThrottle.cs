namespace PveWelcome.Services;

/// In-memory brute-force guard: lock a username after too many failed logins.
/// ponytail: per-instance in-memory; fine for a single app instance. Move to DB/Redis if scaled out.
public class LoginThrottle
{
    private const int MaxFails = 5;
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, (int fails, DateTimeOffset? until)> _map = new();
    private readonly object _lock = new();

    public bool IsLocked(string key)
    {
        lock (_lock)
            return _map.TryGetValue(key, out var s) && s.until is { } u && u > DateTimeOffset.UtcNow;
    }

    public void Fail(string key)
    {
        lock (_lock)
        {
            var s = _map.GetValueOrDefault(key);
            var fails = s.fails + 1;
            _map[key] = fails >= MaxFails ? (0, DateTimeOffset.UtcNow + LockFor) : (fails, null);
        }
    }

    public void Reset(string key)
    {
        lock (_lock) _map.Remove(key);
    }
}
