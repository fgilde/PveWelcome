using System.Text.Json;
using PveWelcome.Models;

namespace PveWelcome.Services;

public class PveOptions
{
    /// e.g. https://192.168.178.126:8006/api2/json
    public string BaseUrl { get; set; } = "";
    /// full token: USER@REALM!TOKENID=SECRET
    public string ApiToken { get; set; } = "";
}

public class PveClient(HttpClient http, ConnectionConfig config, ILogger<PveClient> log)
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public bool Configured => !string.IsNullOrWhiteSpace(config.Current.PveBaseUrl);

    private string Url(string path) => $"{config.Current.PveBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private HttpRequestMessage Req(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Url(path));
        // Proxmox: Authorization: PVEAPIToken=USER@REALM!TOKENID=SECRET  (note '=', not a space)
        if (!string.IsNullOrWhiteSpace(config.Current.PveApiToken))
            req.Headers.TryAddWithoutValidation("Authorization", $"PVEAPIToken={config.Current.PveApiToken}");
        return req;
    }

    private async Task<JsonElement> GetDataAsync(string path)
    {
        using var res = await http.SendAsync(Req(HttpMethod.Get, path));
        res.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    public async Task<List<string>> GetNodesAsync()
    {
        if (!Configured) return [];
        try
        {
            var data = await GetDataAsync("/nodes");
            return data.EnumerateArray().Select(n => n.GetProperty("node").GetString()!).ToList();
        }
        catch (Exception ex) { log.LogWarning(ex, "get nodes"); return []; }
    }

    public async Task<NodeStatus?> GetNodeStatusAsync(string node)
    {
        try
        {
            var d = await GetDataAsync($"/nodes/{node}/status");
            var cpu = d.TryGetProperty("cpu", out var c) ? c.GetDouble() : 0;
            var mem = d.GetProperty("memory");
            var load = d.TryGetProperty("loadavg", out var la) && la.GetArrayLength() > 0
                ? double.Parse(la[0].GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture) : 0;
            var cpuCount = d.TryGetProperty("cpuinfo", out var ci) && ci.TryGetProperty("cpus", out var cc) ? cc.GetInt32() : 0;
            return new NodeStatus(node, cpu,
                mem.GetProperty("used").GetInt64(), mem.GetProperty("total").GetInt64(),
                d.TryGetProperty("uptime", out var up) ? up.GetInt64() : 0, load, cpuCount);
        }
        catch (Exception ex) { log.LogWarning(ex, "node status {Node}", node); return null; }
    }

    public async Task<List<StorageInfo>> GetStoragesAsync(string node)
    {
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/storage");
            return data.EnumerateArray()
                .Where(s => !s.TryGetProperty("active", out var a) || a.GetInt32() == 1)
                .Select(s => new StorageInfo(
                    s.GetProperty("storage").GetString() ?? "",
                    s.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                    s.TryGetProperty("total", out var tot) ? tot.GetInt64() : 0,
                    s.TryGetProperty("used", out var u) ? u.GetInt64() : 0,
                    s.TryGetProperty("avail", out var av) ? av.GetInt64() : 0,
                    s.TryGetProperty("content", out var ct) ? ct.GetString() ?? "" : ""))
                .OrderByDescending(s => s.Total)
                .ToList();
        }
        catch (Exception ex) { log.LogWarning(ex, "storages {Node}", node); return []; }
    }

    /// Latest backup per vmid, from the given dir/backup storage.
    public async Task<Dictionary<int, BackupInfo>> GetBackupsAsync(string node, string storage = "local")
    {
        var map = new Dictionary<int, BackupInfo>();
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/storage/{storage}/content?content=backup");
            foreach (var b in data.EnumerateArray())
            {
                if (!b.TryGetProperty("vmid", out var v)) continue;
                var vmid = (int)v.GetDouble();
                var ctime = b.TryGetProperty("ctime", out var ct) ? ct.GetInt64() : 0;
                var size = b.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                var volid = b.TryGetProperty("volid", out var vo) ? vo.GetString() ?? "" : "";
                var info = new BackupInfo(vmid, DateTimeOffset.FromUnixTimeSeconds(ctime), size, volid);
                if (!map.TryGetValue(vmid, out var cur) || info.Time > cur.Time) map[vmid] = info;
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "backups {Node}/{Storage}", node, storage); }
        return map;
    }

    /// ALL backups (every archive, not just the latest) from a backup storage.
    public async Task<List<BackupInfo>> GetBackupListAsync(string node, string storage)
    {
        var list = new List<BackupInfo>();
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/storage/{storage}/content?content=backup");
            foreach (var b in data.EnumerateArray())
            {
                if (!b.TryGetProperty("vmid", out var v)) continue;
                var ctime = b.TryGetProperty("ctime", out var ct) ? ct.GetInt64() : 0;
                var size = b.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                var volid = b.TryGetProperty("volid", out var vo) ? vo.GetString() ?? "" : "";
                list.Add(new BackupInfo((int)v.GetDouble(), DateTimeOffset.FromUnixTimeSeconds(ctime), size, volid));
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "backup list {Node}/{Storage}", node, storage); }
        return list;
    }

    /// Delete a single backup archive by its volid.
    public Task<string?> DeleteBackupAsync(string node, string storage, string volid) =>
        SendErr(Req(HttpMethod.Delete, $"/nodes/{node}/storage/{storage}/content/{Uri.EscapeDataString(volid)}"));

    /// CPU/RAM history (last hour, normalized 0..1) from PVE's own rrd store — nothing stored by us.
    private async Task<List<RrdPoint>> RrdAsync(string path, string memUsedKey, string memTotalKey)
    {
        try
        {
            var data = await GetDataAsync($"{path}?timeframe=hour&cf=AVERAGE");
            var pts = new List<RrdPoint>();
            foreach (var e in data.EnumerateArray())
            {
                double D(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                var mt = D(memTotalKey);
                pts.Add(new RrdPoint(D("cpu"), mt > 0 ? D(memUsedKey) / mt : 0));
            }
            return pts;
        }
        catch (Exception ex) { log.LogWarning(ex, "rrd {Path}", path); return []; }
    }

    public Task<List<RrdPoint>> GetNodeRrdAsync(string node) => RrdAsync($"/nodes/{node}/rrddata", "memused", "memtotal");
    public Task<List<RrdPoint>> GetGuestRrdAsync(string node, string type, int vmid) => RrdAsync($"/nodes/{node}/{type}/{vmid}/rrddata", "mem", "maxmem");

    /// Recent node tasks (backups, restores, start/stop, ...), newest first.
    public async Task<List<PveTask>> GetTasksAsync(string node, int limit = 20)
    {
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/tasks?limit={limit}&source=all");
            return data.EnumerateArray().Select(t =>
            {
                string S(string k) => t.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
                var idStr = S("id");
                int? vmid = int.TryParse(idStr, out var vi) ? vi : null;
                var start = t.TryGetProperty("starttime", out var s) ? DateTimeOffset.FromUnixTimeSeconds(s.GetInt64()) : default;
                DateTimeOffset? end = t.TryGetProperty("endtime", out var e) && e.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(e.GetInt64()) : null;
                return new PveTask(S("type"), S("status"), vmid, start, end, S("upid"));
            }).ToList();
        }
        catch (Exception ex) { log.LogWarning(ex, "tasks {Node}", node); return []; }
    }

    /// Log lines of one task (for the activity-feed viewer).
    public async Task<string> GetTaskLogAsync(string node, string upid, int limit = 400)
    {
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/tasks/{Uri.EscapeDataString(upid)}/log?limit={limit}");
            var lines = data.EnumerateArray()
                .Select(e => e.TryGetProperty("t", out var t) ? t.GetString() ?? "" : "");
            return string.Join("\n", lines);
        }
        catch (Exception ex) { log.LogWarning(ex, "task log {Upid}", upid); return "(Log nicht verfügbar)"; }
    }

    /// Physical disks with SMART health.
    public async Task<List<DiskInfo>> GetDisksAsync(string node)
    {
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/disks/list");
            return data.EnumerateArray().Select(d => new DiskInfo(
                d.TryGetProperty("devpath", out var p) ? p.GetString() ?? "" : "",
                d.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                d.TryGetProperty("health", out var h) ? h.GetString() ?? "" : "",
                d.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                d.TryGetProperty("type", out var t) ? t.GetString() ?? "" : ""))
                .ToList();
        }
        catch (Exception ex) { log.LogWarning(ex, "disks {Node}", node); return []; }
    }

    /// Scheduled cluster backup jobs.
    public async Task<List<BackupJob>> GetBackupJobsAsync()
    {
        try
        {
            var data = await GetDataAsync("/cluster/backup");
            return data.EnumerateArray().Select(j =>
            {
                var keep = 0;
                if (j.TryGetProperty("prune-backups", out var pb))
                {
                    // can be an object {"keep-last":"3"} or a string "keep-last=3"
                    if (pb.ValueKind == JsonValueKind.Object && pb.TryGetProperty("keep-last", out var kl))
                        int.TryParse(kl.GetString(), out keep);
                    else if (pb.ValueKind == JsonValueKind.String)
                    {
                        var s = pb.GetString() ?? "";
                        var m = s.Split(',').FirstOrDefault(p => p.StartsWith("keep-last="));
                        if (m != null) int.TryParse(m["keep-last=".Length..], out keep);
                    }
                }
                return new BackupJob(
                    j.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    j.TryGetProperty("schedule", out var sc) ? sc.GetString() ?? "" : (j.TryGetProperty("starttime", out var st) ? st.GetString() ?? "" : ""),
                    j.TryGetProperty("storage", out var stg) ? stg.GetString() ?? "" : "",
                    !j.TryGetProperty("enabled", out var en) || en.GetInt32() == 1,
                    j.TryGetProperty("all", out var all) && all.GetInt32() == 1,
                    j.TryGetProperty("vmid", out var v) ? v.GetString() ?? "" : "",
                    keep);
            }).ToList();
        }
        catch (Exception ex) { log.LogWarning(ex, "backup jobs"); return []; }
    }

    private static Dictionary<string, string> JobForm(string schedule, string storage, int keepLast, bool all, string vmid, bool enabled)
    {
        var f = new Dictionary<string, string>
        {
            ["schedule"] = schedule,
            ["storage"] = storage,
            ["mode"] = "snapshot",
            ["compress"] = "zstd",
            ["prune-backups"] = $"keep-last={Math.Max(1, keepLast)}",
            ["enabled"] = enabled ? "1" : "0",
        };
        if (all) f["all"] = "1"; else f["vmid"] = vmid;
        return f;
    }

    public Task<string?> CreateBackupJobAsync(string schedule, string storage, int keepLast, bool all, string vmid)
    {
        var req = Req(HttpMethod.Post, "/cluster/backup");
        req.Content = new FormUrlEncodedContent(JobForm(schedule, storage, keepLast, all, vmid, true));
        return SendErr(req);
    }

    public Task<string?> UpdateBackupJobAsync(string id, string schedule, string storage, int keepLast, bool all, string vmid, bool enabled)
    {
        var req = Req(HttpMethod.Put, $"/cluster/backup/{Uri.EscapeDataString(id)}");
        var form = JobForm(schedule, storage, keepLast, all, vmid, enabled);
        if (all) form["vmid"] = ""; // switching to all clears explicit vmids
        req.Content = new FormUrlEncodedContent(form);
        return SendErr(req);
    }

    public Task<string?> DeleteBackupJobAsync(string id) =>
        SendErr(Req(HttpMethod.Delete, $"/cluster/backup/{Uri.EscapeDataString(id)}"));

    public Task<string?> SetBackupJobEnabledAsync(string id, bool enabled)
    {
        var req = Req(HttpMethod.Put, $"/cluster/backup/{Uri.EscapeDataString(id)}");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["enabled"] = enabled ? "1" : "0" });
        return SendErr(req);
    }

    /// Number of pending apt updates on the node (from the cached list; 0 on error).
    public async Task<int> GetUpdatesAsync(string node)
    {
        try { return (await GetDataAsync($"/nodes/{node}/apt/update")).GetArrayLength(); }
        catch (Exception ex) { log.LogWarning(ex, "updates {Node}", node); return 0; }
    }

    public async Task<List<Snapshot>> GetSnapshotsAsync(string node, string type, int vmid)
    {
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/{type}/{vmid}/snapshot");
            return data.EnumerateArray().Select(s => new Snapshot(
                    s.GetProperty("name").GetString() ?? "",
                    s.TryGetProperty("description", out var d) ? d.GetString() : null,
                    s.TryGetProperty("snaptime", out var t) && t.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()) : null))
                .Where(s => s.Name != "current")
                .OrderByDescending(s => s.Time)
                .ToList();
        }
        catch (Exception ex) { log.LogWarning(ex, "snapshots {Vmid}", vmid); return []; }
    }

    public Task<string?> CreateSnapshotAsync(string node, string type, int vmid, string name, string? desc)
    {
        var req = Req(HttpMethod.Post, $"/nodes/{node}/{type}/{vmid}/snapshot");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["snapname"] = name, ["description"] = desc ?? "" });
        return SendErr(req);
    }

    public Task<string?> RollbackSnapshotAsync(string node, string type, int vmid, string name) =>
        SendErr(Req(HttpMethod.Post, $"/nodes/{node}/{type}/{vmid}/snapshot/{name}/rollback"));

    public Task<string?> DeleteSnapshotAsync(string node, string type, int vmid, string name) =>
        SendErr(Req(HttpMethod.Delete, $"/nodes/{node}/{type}/{vmid}/snapshot/{name}"));

    /// Send a request, return null on success else "code: body".
    private async Task<string?> SendErr(HttpRequestMessage req)
    {
        try
        {
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadAsStringAsync();
            return $"{(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}";
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<List<PveGuest>> GetGuestsAsync(bool withIp = true)
    {
        var guests = new List<PveGuest>();
        foreach (var node in await GetNodesAsync())
        {
            guests.AddRange(await GuestsOfType(node, "qemu"));
            guests.AddRange(await GuestsOfType(node, "lxc"));
        }
        if (withIp)
        {
            foreach (var g in guests.Where(g => g.IsRunning).ToList())
            {
                var ip = await TryGetIpAsync(g);
                if (ip is not null)
                {
                    var idx = guests.IndexOf(g);
                    guests[idx] = g with { Ip = ip };
                }
            }
        }
        return guests.OrderBy(g => g.VmId).ToList();
    }

    private async Task<List<PveGuest>> GuestsOfType(string node, string type)
    {
        try
        {
            var data = await GetDataAsync($"/nodes/{node}/{type}");
            return data.EnumerateArray().Select(g => new PveGuest(
                (int)g.GetProperty("vmid").GetDouble(),
                g.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                type,
                g.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                node,
                g.TryGetProperty("cpu", out var cpu) ? cpu.GetDouble() : 0,
                g.TryGetProperty("mem", out var mu) ? mu.GetInt64() : 0,
                g.TryGetProperty("maxmem", out var mm) ? mm.GetInt64() : 0,
                g.TryGetProperty("disk", out var du) && du.GetInt64() > 0 ? du.GetInt64() : null,
                g.TryGetProperty("maxdisk", out var dm) ? dm.GetInt64() : null,
                g.TryGetProperty("uptime", out var ut) && ut.GetInt64() > 0 ? ut.GetInt64() : null,
                null)).ToList();
        }
        catch (Exception ex) { log.LogWarning(ex, "guests {Node}/{Type}", node, type); return []; }
    }

    private async Task<string?> TryGetIpAsync(PveGuest g)
    {
        try
        {
            if (g.Type == "lxc")
            {
                var d = await GetDataAsync($"/nodes/{g.Node}/lxc/{g.VmId}/interfaces");
                return FirstIpv4(d, "inet", "name");
            }
            else
            {
                var d = await GetDataAsync($"/nodes/{g.Node}/qemu/{g.VmId}/agent/network-get-interfaces");
                var result = d.TryGetProperty("result", out var r) ? r : d;
                foreach (var iface in result.EnumerateArray())
                {
                    if (!iface.TryGetProperty("ip-addresses", out var addrs)) continue;
                    foreach (var a in addrs.EnumerateArray())
                    {
                        if (a.TryGetProperty("ip-address-type", out var t) && t.GetString() == "ipv4")
                        {
                            var ip = a.GetProperty("ip-address").GetString();
                            if (ip is not null && !ip.StartsWith("127.")) return ip;
                        }
                    }
                }
            }
        }
        catch { /* agent off / ct without net info — best effort */ }
        return null;
    }

    private static string? FirstIpv4(JsonElement arr, string ipProp, string nameProp)
    {
        foreach (var e in arr.EnumerateArray())
        {
            if (e.TryGetProperty(nameProp, out var n) && n.GetString() == "lo") continue;
            if (e.TryGetProperty(ipProp, out var ip))
            {
                var s = ip.GetString();
                if (!string.IsNullOrEmpty(s) && !s.StartsWith("127."))
                    return s.Split('/')[0].Split(',')[0].Trim();
            }
        }
        return null;
    }

    public async Task<bool> ActionAsync(string node, string type, int vmid, string action)
    {
        try
        {
            using var res = await http.SendAsync(Req(HttpMethod.Post, $"/nodes/{node}/{type}/{vmid}/status/{action}"));
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { log.LogWarning(ex, "action {Action} {Vmid}", action, vmid); return false; }
    }

    /// Start a vzdump backup of one guest to the given storage. Returns true if the task started.
    public async Task<bool> TriggerBackupAsync(string node, int vmid, string storage)
    {
        if (string.IsNullOrWhiteSpace(storage)) return false;
        try
        {
            var req = Req(HttpMethod.Post, $"/nodes/{node}/vzdump");
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["vmid"] = vmid.ToString(),
                ["storage"] = storage,
                ["mode"] = "snapshot",
                ["compress"] = "zstd",
                ["remove"] = "0",
            });
            using var res = await http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { log.LogWarning(ex, "backup {Vmid} -> {Storage}", vmid, storage); return false; }
    }

    /// Restore a guest IN PLACE from a backup volume (DESTRUCTIVE: overwrites the guest, force=1).
    /// Returns null on success, else an error message. Target vmid must be stopped.
    public async Task<string?> RestoreAsync(string node, string type, int vmid, string volid)
    {
        if (string.IsNullOrWhiteSpace(volid)) return "kein Backup-Volume gefunden";
        try
        {
            var form = type == "qemu"
                ? new Dictionary<string, string> { ["vmid"] = vmid.ToString(), ["archive"] = volid, ["force"] = "1" }
                : new Dictionary<string, string> { ["vmid"] = vmid.ToString(), ["ostemplate"] = volid, ["restore"] = "1", ["force"] = "1" };
            var req = Req(HttpMethod.Post, $"/nodes/{node}/{type}");
            req.Content = new FormUrlEncodedContent(form);
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadAsStringAsync();
            return $"{(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}";
        }
        catch (Exception ex) { log.LogWarning(ex, "restore {Vmid}", vmid); return ex.Message; }
    }
}
