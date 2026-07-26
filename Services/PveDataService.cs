using PveWelcome.Models;

namespace PveWelcome.Services;

/// Caches PVE + NPM data and refreshes it in the background so pages render instantly.
public class PveDataService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, NotificationService notifier, ILogger<PveDataService> log) : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HashSet<string> _lastAlerts = [];
    private bool _alertsSeeded;

    public NodeStatus? Health { get; private set; }
    public IReadOnlyList<PveGuest> Guests { get; private set; } = [];
    public IReadOnlyList<StorageInfo> Storages { get; private set; } = [];
    public IReadOnlyList<string> BackupStorages { get; private set; } = [];
    public IReadOnlyList<double> NodeCpuHist { get; private set; } = [];
    public IReadOnlyList<double> NodeMemHist { get; private set; } = [];
    public IReadOnlyList<PveTask> Tasks { get; private set; } = [];
    public int UpdatesAvailable { get; private set; }
    public IReadOnlyList<DiskInfo> Disks { get; private set; } = [];
    public IReadOnlyList<BackupJob> BackupJobs { get; private set; } = [];
    /// every backup archive across all backup storages, newest first
    public IReadOnlyList<BackupInfo> AllBackups { get; private set; } = [];
    public IReadOnlyList<UptimeMonitor> Monitors { get; private set; } = [];
    /// monitor id -> reachable
    public IReadOnlyDictionary<int, bool> MonitorUp { get; private set; } = new Dictionary<int, bool>();
    public string? Node { get; private set; }
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
        if (!await _lock.WaitAsync(0)) return;
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
                Node = nodes[0];
                Health = await pve.GetNodeStatusAsync(nodes[0]);
                Storages = await pve.GetStoragesAsync(nodes[0]);
                BackupStorages = Storages.Where(s => s.TakesBackups).Select(s => s.Name).ToList();
                var merged = new Dictionary<int, BackupInfo>();
                var all = new List<BackupInfo>();
                foreach (var s in Storages.Where(s => s.TakesBackups))
                {
                    foreach (var b in await pve.GetBackupListAsync(nodes[0], s.Name))
                    {
                        all.Add(b);
                        if (!merged.TryGetValue(b.VmId, out var cur) || b.Time > cur.Time) merged[b.VmId] = b;
                    }
                }
                Backups = merged;
                AllBackups = all.OrderByDescending(b => b.Time).ToList();
                var rrd = await pve.GetNodeRrdAsync(nodes[0]);
                NodeCpuHist = rrd.Select(p => p.Cpu).ToList();
                NodeMemHist = rrd.Select(p => p.Mem).ToList();
                Tasks = await pve.GetTasksAsync(nodes[0]);
                UpdatesAvailable = await pve.GetUpdatesAsync(nodes[0]);
                Disks = await pve.GetDisksAsync(nodes[0]);
                BackupJobs = await pve.GetBackupJobsAsync();
            }
            Guests = await pve.GetGuestsAsync();
            Hosts = await npm.GetHostsAsync();
            DomainUp = await CheckDomainsAsync(Hosts);
            Monitors = await scope.ServiceProvider.GetRequiredService<MonitorService>().ListAsync();
            MonitorUp = await CheckMonitorsAsync(Monitors);
            LastUpdated = DateTime.Now;
            await NotifyNewAlertsAsync();
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

    /// Current alert lines (shared by the dashboard banner and the notifier).
    public IReadOnlyList<string> Alerts
    {
        get
        {
            var list = new List<string>();
            foreach (var s in Storages.Where(s => s.Fraction > 0.90))
                list.Add(Loc.T("Storage '{0}' ist zu {1} % voll", s.Name, (s.Fraction * 100).ToString("0")));
            foreach (var g in Guests.Where(g => !g.IsRunning))
                list.Add(Loc.T("{0} {1} (#{2}) ist gestoppt", g.Kind, g.Name, g.VmId));
            var noBackup = Guests.Count(g => BackupFor(g.VmId) is null);
            if (Backups.Count > 0 && noBackup > 0)
                list.Add(Loc.T("{0} Guest(s) ohne Backup", noBackup));
            if (UpdatesAvailable > 0)
                list.Add(Loc.T("{0} Paket-Update(s) am Node verfügbar", UpdatesAvailable));
            foreach (var d in Disks.Where(d => !d.Healthy && d.Health is not ("UNKNOWN" or "")))
                list.Add(Loc.T("Platte {0} SMART-Status: {1}", d.DevPath, d.Health));
            foreach (var m in Monitors.Where(m => m.Enabled && MonitorUp.TryGetValue(m.Id, out var up) && !up))
                list.Add(Loc.T("Monitor '{0}' nicht erreichbar", m.Name));
            return list;
        }
    }

    private async Task NotifyNewAlertsAsync()
    {
        var current = Alerts.ToHashSet();
        if (_alertsSeeded)
        {
            var fresh = current.Where(a => !_lastAlerts.Contains(a)).ToList();
            if (fresh.Count > 0) await notifier.NotifyAsync(fresh);
        }
        _lastAlerts = current;
        _alertsSeeded = true;
    }

    /// The storage a manual backup writes to (configured, else first backup-capable storage).
    public string? BackupTarget
    {
        get
        {
            using var scope = scopeFactory.CreateScope();
            var cfg = scope.ServiceProvider.GetRequiredService<ConnectionConfig>().Current.BackupStorage;
            return !string.IsNullOrWhiteSpace(cfg) ? cfg : BackupStorages.FirstOrDefault();
        }
    }

    /// Start a manual backup of one guest to the configured target storage.
    public async Task<bool> TriggerBackupAsync(PveGuest g)
    {
        var storage = BackupTarget;
        if (string.IsNullOrWhiteSpace(storage)) return false;
        using var scope = scopeFactory.CreateScope();
        var pve = scope.ServiceProvider.GetRequiredService<PveClient>();
        return await pve.TriggerBackupAsync(g.Node, g.VmId, storage);
    }

    /// DESTRUCTIVE in-place restore of a guest from its latest backup. Returns null on success, else error.
    public async Task<string?> RestoreAsync(PveGuest g)
    {
        var b = BackupFor(g.VmId);
        if (b is null || string.IsNullOrEmpty(b.Volid)) return "kein Backup vorhanden";
        using var scope = scopeFactory.CreateScope();
        var pve = scope.ServiceProvider.GetRequiredService<PveClient>();
        return await pve.RestoreAsync(g.Node, g.Type, g.VmId, b.Volid);
    }

    /// DESTRUCTIVE in-place restore from a SPECIFIC backup archive. Returns null on success, else error.
    public async Task<string?> RestoreFromAsync(BackupInfo b)
    {
        if (Node is null || string.IsNullOrEmpty(b.Volid)) return "kein Backup";
        using var scope = scopeFactory.CreateScope();
        var pve = scope.ServiceProvider.GetRequiredService<PveClient>();
        return await pve.RestoreAsync(Node, b.Type, b.VmId, b.Volid);
    }

    /// Delete a single backup archive. Returns null on success, else error.
    public async Task<string?> DeleteBackupAsync(BackupInfo b)
    {
        if (Node is null || string.IsNullOrEmpty(b.Storage)) return "kein Storage";
        using var scope = scopeFactory.CreateScope();
        var pve = scope.ServiceProvider.GetRequiredService<PveClient>();
        return await pve.DeleteBackupAsync(Node, b.Storage, b.Volid);
    }

    private async Task<Dictionary<string, bool>> CheckDomainsAsync(IReadOnlyList<NpmHost> hosts)
    {
        var client = httpFactory.CreateClient("reach");
        var domains = hosts.Where(h => h.Enabled).SelectMany(h => h.DomainNames).Distinct().ToList();
        var tasks = domains.Select(async d =>
        {
            try
            {
                using var res = await client.GetAsync($"https://{d}/", HttpCompletionOption.ResponseHeadersRead);
                return (d, up: (int)res.StatusCode < 500);
            }
            catch { return (d, up: false); }
        });
        var results = new Dictionary<string, bool>();
        foreach (var (d, up) in await Task.WhenAll(tasks)) results[d] = up;
        return results;
    }

    private async Task<Dictionary<int, bool>> CheckMonitorsAsync(IReadOnlyList<UptimeMonitor> monitors)
    {
        var client = httpFactory.CreateClient("reach");
        var tasks = monitors.Where(m => m.Enabled).Select(async m =>
        {
            try
            {
                using var res = await client.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead);
                return (m.Id, up: (int)res.StatusCode < 500);
            }
            catch { return (m.Id, up: false); }
        });
        var results = new Dictionary<int, bool>();
        foreach (var (id, up) in await Task.WhenAll(tasks)) results[id] = up;
        return results;
    }

    public Task StopAsync(CancellationToken ct) { _timer?.Change(Timeout.Infinite, 0); return Task.CompletedTask; }
    public void Dispose() => _timer?.Dispose();
}
