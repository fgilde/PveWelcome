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

/// A backup of a guest (a single vzdump archive).
public record BackupInfo(int VmId, DateTimeOffset Time, long Size, string Volid = "")
{
    /// Storage name is the part before ':' in the volid (e.g. "nas-backup:backup/...").
    public string Storage => Volid.Contains(':') ? Volid.Split(':')[0] : "";
    /// Guest type derived from the archive name (vzdump-qemu-… / vzdump-lxc-…).
    public string Type => Volid.Contains("vzdump-qemu") ? "qemu" : "lxc";
}

/// One history sample from PVE rrddata (values normalized 0..1).
public record RrdPoint(double Cpu, double Mem);

/// A recent PVE task (backup, restore, start/stop, ...).
public record PveTask(string Type, string Status, int? VmId, DateTimeOffset Start, DateTimeOffset? End, string Upid)
{
    public bool IsRunning => End is null;
    public bool Ok => End is not null && (Status == "OK" || string.IsNullOrEmpty(Status));
    public string Label => VmId is null ? Type : $"{Type} #{VmId}";
}

/// A guest snapshot.
public record Snapshot(string Name, string? Description, DateTimeOffset? Time);

/// A physical disk on the node (SMART health).
public record DiskInfo(string DevPath, string Model, string Health, long Size, string Type)
{
    public bool Healthy => Health is "PASSED" or "OK" or "";
}

/// A scheduled cluster backup job.
public record BackupJob(string Id, string Schedule, string Storage, bool Enabled, bool All, string Vmid, int KeepLast)
{
    public string Selection => All ? "alle" : string.IsNullOrEmpty(Vmid) ? "?" : Vmid;
}

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
