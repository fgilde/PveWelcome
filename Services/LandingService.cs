using Microsoft.EntityFrameworkCore;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// CRUD + host resolution for public landing pages. DB-backed, cached in memory.
public class LandingService(IServiceScopeFactory scopeFactory)
{
    private List<LandingPage> _cache = [];

    public async Task InitAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"LandingPages\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_LandingPages\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Name\" TEXT NOT NULL DEFAULT '', \"Hosts\" TEXT NOT NULL DEFAULT '', \"Template\" TEXT NOT NULL DEFAULT 'classic', " +
            "\"Html\" TEXT NOT NULL DEFAULT '', \"Css\" TEXT NOT NULL DEFAULT '', \"Js\" TEXT NOT NULL DEFAULT '', " +
            "\"LoginEnabled\" INTEGER NOT NULL DEFAULT 1, \"Active\" INTEGER NOT NULL DEFAULT 1, \"IsDefault\" INTEGER NOT NULL DEFAULT 0);");

        if (!await db.LandingPages.AnyAsync())
        {
            var seed = LandingTemplates.NewFrom("classic");
            seed.Name = "Default";
            seed.IsDefault = true;
            db.LandingPages.Add(seed);
            await db.SaveChangesAsync();
        }
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _cache = await db.LandingPages.AsNoTracking().ToListAsync();
    }

    /// Page serving this host: exact host match first, else the default page, else null.
    public LandingPage? Resolve(string? host)
    {
        var active = _cache.Where(p => p.Active).ToList();
        if (!string.IsNullOrEmpty(host))
        {
            host = host.Split(':')[0].ToLowerInvariant();
            if (host.StartsWith("www.")) host = host[4..];
            var match = active.FirstOrDefault(p => p.HostList.Any(h => h == host || (h.StartsWith("www.") && h[4..] == host)));
            if (match is not null) return match;
        }
        return active.FirstOrDefault(p => p.IsDefault) ?? active.FirstOrDefault();
    }

    public Task<List<LandingPage>> GetAllAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.LandingPages.AsNoTracking().OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync();
    }

    public async Task<LandingPage?> GetAsync(int id)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.LandingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<int> SaveAsync(LandingPage p)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (p.IsDefault)
                await db.LandingPages.Where(x => x.Id != p.Id && x.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
            if (p.Id == 0) db.LandingPages.Add(p);
            else db.LandingPages.Update(p);
            await db.SaveChangesAsync();
        }
        await ReloadAsync();
        return p.Id;
    }

    public async Task DeleteAsync(int id)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.LandingPages.FindAsync(id);
            if (p is not null) { db.LandingPages.Remove(p); await db.SaveChangesAsync(); }
        }
        await ReloadAsync();
    }
}
