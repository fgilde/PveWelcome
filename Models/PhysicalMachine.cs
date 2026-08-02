namespace PveWelcome.Models;

/// A physical box on the LAN (not a PVE guest): status probe, Wake-on-LAN, remote shutdown, RDP.
/// Wake and shutdown run over a jump host that sits directly on the LAN (the PVE node) — the app
/// container is behind Docker's bridge and cannot emit a broadcast itself.
public class PhysicalMachine
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// Address of the machine itself, e.g. 192.168.178.54.
    public string Host { get; set; } = "";
    /// Port used for the online probe (RDP by default — ICMP is often firewalled off).
    public int ProbePort { get; set; } = 3389;
    public string OsUser { get; set; } = "";

    /// Burned-in NIC address. In S5 the card falls back to this even when Windows overrides the MAC.
    public string Mac { get; set; } = "";
    /// Optional second address (a software MAC override), woken alongside the first.
    public string MacAlt { get; set; } = "";
    public string Broadcast { get; set; } = "255.255.255.255";
    public int WolPort { get; set; } = 9;

    /// Jump host on the LAN that sends the magic packet and the shutdown command.
    public string JumpHost { get; set; } = "";
    public int JumpPort { get; set; } = 22;
    public string JumpUser { get; set; } = "root";
    public string JumpAuth { get; set; } = "";
    public bool JumpAuthIsKey { get; set; } = true;

    /// Shutdown command executed ON the jump host. {host} and {user} are substituted.
    public string ShutdownCommand { get; set; } = "";

    /// Full URL of the browser-RDP session (Guacamole), shown as "connect".
    public string GuacUrl { get; set; } = "";
    /// Hostname that should render this machine as its landing page, e.g. pc.nksoft.de.
    public string PublicHost { get; set; } = "";

    public bool Enabled { get; set; } = true;
}
