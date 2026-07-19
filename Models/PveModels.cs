namespace PveWelcome.Models;

/// A VM (qemu) or container (lxc) on the node.
public record PveGuest(
    int VmId,
    string Name,
    string Type,          // "qemu" | "lxc"
    string Status,        // "running" | "stopped" | ...
    string Node,
    double CpuFraction,   // 0..1
    long MemUsed,
    long MemMax,
    long? DiskUsed,
    long? DiskMax,
    long? Uptime,         // seconds
    string? Ip            // best-effort primary IPv4, null if unknown
)
{
    public bool IsRunning => Status == "running";
    public string Kind => Type == "qemu" ? "VM" : "CT";
}

/// Latest backup of a guest.
public record BackupInfo(int VmId, DateTimeOffset Time, long Size, string Volid = "");

/// A Proxmox storage pool on the node.
public record StorageInfo(string Name, string Type, long Total, long Used, long Avail, string Content = "")
{
    public double Fraction => Total > 0 ? (double)Used / Total : 0;
    public bool TakesBackups => Content.Contains("backup");
}

/// Health of a Proxmox node.
public record NodeStatus(
    string Node,
    double CpuFraction,
    long MemUsed,
    long MemMax,
    long Uptime,
    double LoadAvg1,
    int CpuCount
);
