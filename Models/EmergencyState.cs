namespace PveWelcome.Models;

/// Emergency mode ("Notfall-Modus"): single row holding config + what was paused/disabled, so it survives restarts.
public class EmergencyState
{
    public int Id { get; set; } = 1;
    public bool Active { get; set; }
    public DateTime? Since { get; set; }
    public string Reason { get; set; } = "";
    /// guests touched on activation, "node:type:vmid" comma-separated (qemu = suspended, lxc = shut down)
    public string Paused { get; set; } = "";
    /// NPM proxy-host ids disabled on activation, comma-separated
    public string DisabledHosts { get; set; } = "";
    /// auto-activate when a VM-disk storage reaches Threshold %
    public bool AutoEnabled { get; set; } = true;
    public int Threshold { get; set; } = 95;
    /// vmids never paused (comma-separated); the guest hosting NPM/PveWelcome is always kept
    public string KeepVmIds { get; set; } = "";
    /// domains whose NPM host stays enabled (comma-separated); PveWelcome's own hosts are always kept
    public string KeepDomains { get; set; } = "";
}
