using Microsoft.EntityFrameworkCore;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// CRUD for user-defined uptime monitors (DB-backed).
public class MonitorService(AppDbContext db)
{
    public Task<List<UptimeMonitor>> ListAsync() => db.Monitors.OrderBy(m => m.Name).ToListAsync();

    public async Task AddAsync(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http")) url = "https://" + url;
        db.Monitors.Add(new UptimeMonitor { Name = string.IsNullOrWhiteSpace(name) ? url : name.Trim(), Url = url.Trim() });
        await db.SaveChangesAsync();
    }

    public async Task ToggleAsync(int id)
    {
        var m = await db.Monitors.FindAsync(id);
        if (m is null) return;
        m.Enabled = !m.Enabled;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var m = await db.Monitors.FindAsync(id);
        if (m is null) return;
        db.Monitors.Remove(m);
        await db.SaveChangesAsync();
    }
}
