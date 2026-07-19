using PveWelcome.Models;

namespace PveWelcome.Services;

/// Caches PVE + NPM data and refreshes it in the background so pages render instantly.
public class PveDataService(IServiceScopeFactory scopeFactory, ILogger<PveDataService> log) : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public NodeStatus? Health { get; private set; }
    public IReadOnlyList<PveGuest> Guests { get; private set; } = [];
    public IReadOnlyList<NpmHost> Hosts { get; private set; } = [];
    public DateTime LastUpdated { get; private set; }
    public bool Refreshing { get; private set; }

    /// Raised (on a background thread) whenever the cached data changes.
    public event Action? Changed;

    public Task StartAsync(CancellationToken ct)
    {
        _timer = new Timer(async _ => await RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(20));
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        if (!await _lock.WaitAsync(0)) return; // skip if a refresh is already running
        Refreshing = true;
        Changed?.Invoke();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var pve = scope.ServiceProvider.GetRequiredService<PveClient>();
            var npm = scope.ServiceProvider.GetRequiredService<NpmClient>();

            var nodes = await pve.GetNodesAsync();
            Health = nodes.Count > 0 ? await pve.GetNodeStatusAsync(nodes[0]) : null;
            Guests = await pve.GetGuestsAsync();
            Hosts = await npm.GetHostsAsync();
            LastUpdated = DateTime.Now;
        }
        catch (Exception ex) { log.LogWarning(ex, "data refresh"); }
        finally
        {
            Refreshing = false;
            _lock.Release();
            Changed?.Invoke();
        }
    }

    /// NPM hosts whose target is this guest's IP.
    public List<NpmHost> HostsFor(PveGuest g) =>
        g.Ip is null ? [] : Hosts.Where(h => h.ForwardHost == g.Ip).ToList();

    public Task StopAsync(CancellationToken ct) { _timer?.Change(Timeout.Infinite, 0); return Task.CompletedTask; }
    public void Dispose() => _timer?.Dispose();
}
