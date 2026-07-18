using System.Net.Http.Headers;
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

public class PveClient(HttpClient http, ILogger<PveClient> log)
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> GetDataAsync(string path)
    {
        using var res = await http.GetAsync(path);
        res.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("data").Clone();
    }

    public bool Configured => http.BaseAddress is not null;

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
        // action: start | stop | reboot | shutdown
        try
        {
            using var res = await http.PostAsync($"/nodes/{node}/{type}/{vmid}/status/{action}", null);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) { log.LogWarning(ex, "action {Action} {Vmid}", action, vmid); return false; }
    }

    public static AuthenticationHeaderValue BuildAuth(string token) => new("PVEAPIToken", token);
}
