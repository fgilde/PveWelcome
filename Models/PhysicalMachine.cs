namespace PveWelcome.Models;

/// A physical box on the LAN (not a PVE guest): status probe, Wake-on-LAN, remote shutdown, RDP.
/// Everything the machine needs runs through a jump host that sits directly on the LAN (typically the
/// PVE node) — the app container is behind Docker's bridge and cannot emit a broadcast itself.
public class PhysicalMachine
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// Address of the machine itself.
    public string Host { get; set; } = "";
    /// Port used for the online probe (RDP by default — ICMP is often firewalled off).
    public int ProbePort { get; set; } = 3389;
    public string OsUser { get; set; } = "";

    /// Burned-in NIC address. In S5 the card falls back to this even when the OS overrides the MAC.
    public string Mac { get; set; } = "";
    /// Optional second address (a software MAC override), woken alongside the first.
    public string MacAlt { get; set; } = "";
    public string Broadcast { get; set; } = "255.255.255.255";
    public int WolPort { get; set; } = 9;

    /// Jump host on the LAN that sends the magic packet and reaches the machine over SSH.
    public string JumpHost { get; set; } = "";
    public int JumpPort { get; set; } = 22;
    public string JumpUser { get; set; } = "root";
    public string JumpAuth { get; set; } = "";
    public bool JumpAuthIsKey { get; set; } = true;

    /// How the jump host reaches this machine. {host}, {user} and {cmd} are substituted;
    /// {cmd} is shell-quoted for you. Used by the shutdown button and the command console.
    public string SshCommandTemplate { get; set; } =
        "ssh -o BatchMode=yes -o StrictHostKeyChecking=no -i /root/.ssh/id_pc {user}@{host} {cmd}";

    /// Command run through SshCommandTemplate when the shutdown button is pressed.
    public string ShutdownCommand { get; set; } = "shutdown /s /t 0";

    /// Domain of the Guacamole instance, e.g. rdp.example.com (proxied in NPM). Empty = no browser RDP.
    public string GuacHost { get; set; } = "";
    /// Guacamole's numeric connection identifier for this machine.
    public string GuacConnectionId { get; set; } = "";
    /// Guacamole auth backend the connection lives in — "postgresql", "mysql" or "sqlserver".
    public string GuacDataSource { get; set; } = "postgresql";

    public bool Enabled { get; set; } = true;
}
