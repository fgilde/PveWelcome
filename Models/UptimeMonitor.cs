namespace PveWelcome.Models;

/// A user-defined HTTP endpoint to watch (mini uptime monitor).
public class UptimeMonitor
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
