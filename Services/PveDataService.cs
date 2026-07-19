using PveWelcome.Models;

namespace PveWelcome.Services;

/// Caches PVE + NPM data and refreshes it in the background so pages render instantly.
public class PveDataService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, ILogger<PveDataService> log) : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public NodeStatus? Health { get; private set; }
    public IReadOnlyList<PveGuest> Guests { get; private set; } = [];
    public IReadOnlyList<StorageInfo> Storages { get; private set; } = [];
    public IReadOnlyList<NpmHost> Hosts { get; private set; } = [];
    public IReadOnlyDictionary<int, BackupInfo> Backups { get; private set; } = new Dictionary<int, BackupInfo>();
    /// domain -> reachable from outside (real end-to-end check via Cloudflare)
    public IReadOnlyDictionary<string, bool> DomainUp { get; private set; } = new Dictionary<string, bool>();
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
            if (nodes.Count > 0)
            {
                Health = await pve.GetNodeStatusAsync(nodes[0]);
                Storages = await pve.GetStoragesAsync(nodes[0]);
                Backups = await pve.GetBackupsAsync(nodes[0]);
            }
            Guests = await pve.GetGuestsAsync();
            Hosts = await npm.GetHostsAsync();
            DomainUp = await CheckDomainsAsync(Hosts);
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

    public BackupInfo? BackupFor(int vmid) => Backups.TryGetValue(vmid, out var b) ? b : null;

    /// Real external reachability of each served domain (GET https://domain via Cloudflare).
    private async Task<Dictionary<string, bool>> CheckDomainsAsync(IReadOnlyList<NpmHost> hosts)
    {
        var client = httpFactory.CreateClient("reach");
        var domains = hosts.Where(h => h.Enabled).SelectMany(h => h.DomainNames).Distinct().ToList();
        var tasks = domains.Select(async d =>
        {
            try
            {
                using var res = await client.GetAsync($"https://{d}/", HttpCompletionOption.ResponseHeadersRead);
                return (d, up: (int)res.StatusCode < 500); // 2xx/3xx/401/403 = reachable; 5xx/521/timeout = down
            }
            catch { return (d, up: false); }
        });
        var results = new Dictionary<string, bool>();
        foreach (var (d, up) in await Task.WhenAll(tasks)) results[d] = up;
        return results;
    }

    public Task StopAsync(CancellationToken ct) { _timer?.Change(Timeout.Infinite, 0); return Task.CompletedTask; }
    public void Dispose() => _timer?.Dispose();
}
