using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using PveWelcome.Services;

namespace PveWelcome.Mcp;

/// MCP tools exposing PveWelcome's capabilities so a connected AI can inspect and manage the homelab.
[McpServerToolType]
public static class PveMcpTools
{
    [McpServerTool(Name = "list_guests"), Description("List all Proxmox guests (VMs and LXC containers) with status, IP and resource usage.")]
    public static object ListGuests(PveDataService data) =>
        data.Guests.Select(g => new
        {
            g.VmId, g.Name, kind = g.Kind, g.Type, g.Node, g.Status, running = g.IsRunning, g.Ip,
            cpuPct = Math.Round(g.CpuFraction * 100, 1),
            memUsedMb = g.MemUsed / 1048576, memMaxMb = g.MemMax / 1048576,
            uptimeSec = g.Uptime
        }).ToList();

    [McpServerTool(Name = "node_health"), Description("Get Proxmox node health: CPU, memory, uptime, load and pending updates.")]
    public static object NodeHealth(PveDataService data) => new
    {
        node = data.Node,
        cpuPct = data.Health is null ? 0 : Math.Round(data.Health.CpuFraction * 100, 1),
        memUsedMb = data.Health?.MemUsed / 1048576, memTotalMb = data.Health?.MemMax / 1048576,
        uptimeSec = data.Health?.Uptime, load = data.Health?.LoadAvg1,
        updatesAvailable = data.UpdatesAvailable,
        storages = data.Storages.Select(s => new { s.Name, s.Type, usedPct = Math.Round(s.Fraction * 100, 1) })
    };

    [McpServerTool(Name = "guest_action"), Description("Start, stop, reboot or shutdown a guest. action = start|stop|reboot|shutdown.")]
    public static async Task<object> GuestAction(PveDataService data, PveClient pve,
        [Description("Guest VM id")] int vmid,
        [Description("start | stop | reboot | shutdown")] string action)
    {
        var g = data.Guests.FirstOrDefault(x => x.VmId == vmid);
        if (g is null) return new { ok = false, error = $"Guest #{vmid} nicht gefunden" };
        var allowed = new[] { "start", "stop", "reboot", "shutdown" };
        if (!allowed.Contains(action)) return new { ok = false, error = $"action muss eins von {string.Join(",", allowed)} sein" };
        var ok = await pve.ActionAsync(g.Node, g.Type, vmid, action);
        return new { ok, vmid, action };
    }

    [McpServerTool(Name = "backup_guest"), Description("Trigger a vzdump backup of one guest to the configured backup storage.")]
    public static async Task<object> BackupGuest(PveDataService data, [Description("Guest VM id")] int vmid)
    {
        var g = data.Guests.FirstOrDefault(x => x.VmId == vmid);
        if (g is null) return new { ok = false, error = $"Guest #{vmid} nicht gefunden" };
        var ok = await data.TriggerBackupAsync(g);
        return new { ok, vmid, target = data.BackupTarget };
    }

    [McpServerTool(Name = "list_backups"), Description("List backup archives across all backup storages, newest first.")]
    public static object ListBackups(PveDataService data) =>
        data.AllBackups.Select(b => new { b.VmId, time = b.Time.ToString("u"), sizeMb = b.Size / 1048576, b.Storage, b.Volid }).ToList();

    [McpServerTool(Name = "list_domains"), Description("List reverse-proxy (NPM) hosts: which domains map to which internal target.")]
    public static object ListDomains(PveDataService data) =>
        data.Hosts.Select(h => new { domains = h.DomainNames, target = $"{h.ForwardScheme}://{h.ForwardHost}:{h.ForwardPort}", h.Enabled }).ToList();

    [McpServerTool(Name = "list_scripts"), Description("List the custom scripts attached to a guest, with last run result.")]
    public static async Task<object> ListScripts(ScriptService scripts, [Description("Guest VM id")] int vmid) =>
        (await scripts.ListAsync(vmid)).Select(s => new { s.Id, s.Name, s.ExecMode, lastExit = s.LastExit, lastRun = s.LastRunAt?.ToString("u") }).ToList();

    [McpServerTool(Name = "run_script"), Description("Run a custom script by its id (PVE agent or SSH per its config) and return the full output and exit code.")]
    public static async Task<object> RunScript(ScriptService scripts, [Description("Script id from list_scripts")] int scriptId)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in scripts.RunAsync(scriptId))
            sb.Append(chunk);
        var output = sb.ToString();
        var exit = 0;
        var idx = output.LastIndexOf("[exit ", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var tail = output[(idx + 6)..].TrimEnd(']', '\n', '\r', ' ');
            if (int.TryParse(tail, out var e)) exit = e;
        }
        return new { scriptId, exit, output };
    }
}
