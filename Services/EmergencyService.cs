using Microsoft.EntityFrameworkCore;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// Notfall-Modus: pauses guests (qemu suspend / lxc shutdown) and disables public NPM hosts, restores both on deactivate.
/// Auto-activates once when a VM-disk storage crosses the threshold (re-arms after it drops 2 points below).
public class EmergencyService(IServiceScopeFactory scopeFactory, NotificationService notifier, ILogger<EmergencyService> log)
{
    // ponytail: PveWelcome's published port on the NPM host, hardcoded; make it a setting if the deployment port changes
    public const int SelfPort = 8091;

    private EmergencyState _s = new();
    private bool _armed = true;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EmergencyState State => _s;
    public string? LastMessage { get; private set; }
    public event Action? Changed;

    public async Task InitAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"Emergency\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Emergency\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Active\" INTEGER NOT NULL DEFAULT 0, \"Since\" TEXT NULL, \"Reason\" TEXT NOT NULL DEFAULT '', " +
            "\"Paused\" TEXT NOT NULL DEFAULT '', \"DisabledHosts\" TEXT NOT NULL DEFAULT '', " +
            "\"AutoEnabled\" INTEGER NOT NULL DEFAULT 1, \"Threshold\" INTEGER NOT NULL DEFAULT 95, " +
            "\"KeepVmIds\" TEXT NOT NULL DEFAULT '', \"KeepDomains\" TEXT NOT NULL DEFAULT '');");
        var row = await db.Emergency.FirstOrDefaultAsync();
        if (row is null) { row = new EmergencyState(); db.Emergency.Add(row); await db.SaveChangesAsync(); }
        _s = row;
    }

    /// Host part of the NPM base URL = the guest that runs NPM, tunnel and PveWelcome (never paused).
    public static string? CoreIp(string npmBaseUrl) =>
        Uri.TryCreate(npmBaseUrl, UriKind.Absolute, out var u) ? u.Host : null;

    private static HashSet<string> Split(string csv) =>
        csv.Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// Pure: which guests get paused and which NPM hosts get disabled.
    /// Guests without a known IP are skipped on purpose — we must never pause the box we run on.
    public static (List<PveGuest> Guests, List<NpmHost> Hosts) Plan(
        IEnumerable<PveGuest> guests, IEnumerable<NpmHost> hosts, string? coreIp, string keepVmIds, string keepDomains)
    {
        var keepIds = Split(keepVmIds);
        var keepDom = Split(keepDomains);
        var g = guests.Where(x => x.IsRunning && x.Ip is not null && x.Ip != coreIp && !keepIds.Contains(x.VmId.ToString())).ToList();
        var h = hosts.Where(x => x.Enabled
                                 && !(x.ForwardHost == coreIp && x.ForwardPort == SelfPort)
                                 && !x.DomainNames.Any(keepDom.Contains)).ToList();
        return (g, h);
    }

    /// Storages holding VM/CT disks that are at/over the threshold.
    public List<StorageInfo> HotStorages(IEnumerable<StorageInfo> storages) =>
        storages.Where(s => IsDiskStorage(s) && s.Fraction * 100 >= _s.Threshold).ToList();

    private static bool IsDiskStorage(StorageInfo s) => s.Content.Contains("images") || s.Content.Contains("rootdir");

    public async Task<string> ActivateAsync(string reason, IReadOnlyList<PveGuest> guests, IReadOnlyList<NpmHost> hosts)
    {
        await _lock.WaitAsync();
        try
        {
            if (_s.Active) return LastMessage = Loc.T("Notfall-Modus ist bereits aktiv.");
            using var scope = scopeFactory.CreateScope();
            var pve = scope.ServiceProvider.GetRequiredService<PveClient>();
            var npm = scope.ServiceProvider.GetRequiredService<NpmClient>();
            var conn = scope.ServiceProvider.GetRequiredService<ConnectionConfig>().Current;
            var (pg, ph) = Plan(guests, hosts, CoreIp(conn.NpmBaseUrl), _s.KeepVmIds, _s.KeepDomains);

            var paused = new List<string>();
            var failed = new List<string>();
            foreach (var g in pg)
            {
                var ok = await pve.ActionAsync(g.Node, g.Type, g.VmId, g.Type == "qemu" ? "suspend" : "shutdown");
                (ok ? paused : failed).Add(ok ? $"{g.Node}:{g.Type}:{g.VmId}" : $"{g.Name} #{g.VmId}");
            }
            var disabled = new List<int>();
            foreach (var h in ph)
            {
                if (await npm.SetEnabledAsync(h.Id, false)) disabled.Add(h.Id);
                else failed.Add(string.Join(",", h.DomainNames));
            }

            await SaveAsync(s =>
            {
                s.Active = true;
                s.Since = DateTime.Now;
                s.Reason = reason;
                s.Paused = string.Join(",", paused);
                s.DisabledHosts = string.Join(",", disabled);
            });
            var msg = Loc.T("🚨 Notfall-Modus AKTIV ({0}): {1} Guest(s) pausiert/gestoppt, {2} NPM-Host(s) deaktiviert.",
                reason, paused.Count, disabled.Count);
            if (failed.Count > 0) msg += " " + Loc.T("Fehlgeschlagen: {0}", string.Join("; ", failed));
            log.LogWarning("{Msg}", msg);
            await notifier.NotifyAsync([msg]);
            return LastMessage = msg;
        }
        finally { _lock.Release(); Changed?.Invoke(); }
    }

    public async Task<string> DeactivateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!_s.Active) return LastMessage = Loc.T("Notfall-Modus ist nicht aktiv.");
            using var scope = scopeFactory.CreateScope();
            var pve = scope.ServiceProvider.GetRequiredService<PveClient>();
            var npm = scope.ServiceProvider.GetRequiredService<NpmClient>();

            var failed = new List<string>();
            foreach (var p in Split(_s.Paused))
            {
                var parts = p.Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[2], out var vmid)) continue;
                if (!await pve.ActionAsync(parts[0], parts[1], vmid, parts[1] == "qemu" ? "resume" : "start")) failed.Add("#" + vmid);
            }
            foreach (var id in Split(_s.DisabledHosts))
                if (int.TryParse(id, out var hid) && !await npm.SetEnabledAsync(hid, true)) failed.Add("NPM " + hid);

            await SaveAsync(s => { s.Active = false; s.Since = null; s.Reason = ""; s.Paused = ""; s.DisabledHosts = ""; });
            _armed = false; // don't instantly re-trigger while the storage is still hot; re-arms once it drops
            var msg = Loc.T("✅ Notfall-Modus beendet, Guests und NPM-Hosts wiederhergestellt.");
            if (failed.Count > 0) msg += " " + Loc.T("Fehlgeschlagen: {0}", string.Join("; ", failed));
            await notifier.NotifyAsync([msg]);
            return LastMessage = msg;
        }
        finally { _lock.Release(); Changed?.Invoke(); }
    }

    /// Called after every data refresh.
    public async Task CheckAutoAsync(IReadOnlyList<StorageInfo> storages, IReadOnlyList<PveGuest> guests, IReadOnlyList<NpmHost> hosts)
    {
        var disk = storages.Where(IsDiskStorage).ToList();
        if (disk.Count == 0) return;
        if (disk.All(s => s.Fraction * 100 < _s.Threshold - 2)) { _armed = true; return; }
        var hot = HotStorages(disk);
        if (hot.Count == 0 || !_armed || !_s.AutoEnabled || _s.Active) return;
        _armed = false;
        var reason = Loc.T("Auto: Storage {0} bei {1} %",
            string.Join(", ", hot.Select(s => s.Name)), (hot.Max(s => s.Fraction) * 100).ToString("0.0"));
        await ActivateAsync(reason, guests, hosts);
    }

    public Task SaveSettingsAsync(bool auto, int threshold, string keepVmIds, string keepDomains) =>
        SaveAsync(s =>
        {
            s.AutoEnabled = auto;
            s.Threshold = Math.Clamp(threshold, 50, 100);
            s.KeepVmIds = keepVmIds.Trim();
            s.KeepDomains = keepDomains.Trim();
        });

    private async Task SaveAsync(Action<EmergencyState> apply)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Emergency.FirstAsync();
        apply(row);
        await db.SaveChangesAsync();
        _s = row;
        Changed?.Invoke();
    }

    public static void SelfCheck()
    {
        PveGuest G(int id, string ip, string type = "qemu", string status = "running") =>
            new(id, "g" + id, type, status, "pve", 0, 0, 0, null, null, null, ip);
        var guests = new[]
        {
            G(300, "10.0.0.100"), G(312, "10.0.0.63"), G(202, "10.0.0.101", "lxc"),
            G(310, "10.0.0.60", status: "stopped"), G(311, "10.0.0.61"), G(399, null!),
        };
        var hosts = new[]
        {
            new NpmHost(1, ["console.example"], "10.0.0.100", SelfPort, "http", true, true),
            new NpmHost(2, ["jelly.example"], "10.0.0.63", 8096, "http", true, true),
            new NpmHost(3, ["vault.example"], "10.0.0.100", 8222, "http", true, true),
            new NpmHost(4, ["old.example"], "10.0.0.63", 80, "http", false, false),
        };
        var (pg, ph) = Plan(guests, hosts, "10.0.0.100", "311", "vault.example");
        var ids = pg.Select(g => g.VmId).OrderBy(x => x).ToArray();
        if (!ids.SequenceEqual([202, 312])) throw new Exception($"Guests falsch: {string.Join(",", ids)} (erwartet 202,312: core, gestoppt, keep, ohne IP raus)");
        var hids = ph.Select(h => h.Id).ToArray();
        if (!hids.SequenceEqual([2])) throw new Exception($"Hosts falsch: {string.Join(",", hids)} (erwartet 2: self, keep, disabled raus)");
        if (CoreIp("http://192.168.178.100:81") != "192.168.178.100") throw new Exception("CoreIp parse");
        Console.WriteLine("EmergencyService.SelfCheck OK");
    }
}
