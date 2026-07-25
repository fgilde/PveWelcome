namespace PveWelcome.Models;

/// A custom script attached to a guest, run over SSH on a target host.
public class GuestScript
{
    public int Id { get; set; }
    public int VmId { get; set; }
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public string ExecMode { get; set; } = "pve";
    public string SshHost { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = "root";
    public string SshAuth { get; set; } = "";
    public bool SshAuthIsKey { get; set; }
    public int? LastExit { get; set; }
    public string? LastOutput { get; set; }
    public DateTime? LastRunAt { get; set; }
}
