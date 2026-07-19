using Microsoft.EntityFrameworkCore;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// Resolves per-domain branding from the request host. DB-backed, cached in memory.
public class BrandResolver(IServiceScopeFactory scopeFactory)
{
    private Dictionary<string, BrandConfig> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// Seed from appsettings on first run (empty DB), then load cache.
    public async Task InitAsync(IConfiguration config)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // EnsureCreated does not add tables to an already-existing DB (e.g. prod created before this table).
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"Brands\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Brands\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Host\" TEXT NOT NULL, \"Name\" TEXT NOT NULL, \"Tagline\" TEXT NOT NULL, " +
            "\"Accent\" TEXT NOT NULL, \"ProjectsUrl\" TEXT NOT NULL);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Brands_Host\" ON \"Brands\" (\"Host\");");

        if (!await db.Brands.AnyAsync())
        {
            var seed = config.GetSection("Brands").Get<Dictionary<string, Brand>>() ?? new();
            foreach (var (host, b) in seed)
                db.Brands.Add(new BrandConfig { Host = host, Name = b.Name, Tagline = b.Tagline, Accent = b.Accent, ProjectsUrl = b.ProjectsUrl });
            if (seed.Count > 0) await db.SaveChangesAsync();
        }
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var all = await db.Brands.ToListAsync();
        _cache = all.ToDictionary(b => b.Host, b => b, StringComparer.OrdinalIgnoreCase);
    }

    private BrandConfig DefaultCfg => _cache.TryGetValue("default", out var d) ? d
        : new BrandConfig { Host = "default", Name = "Home", Tagline = "Self-hosted." };

    public Brand Resolve(string? host)
    {
        if (string.IsNullOrEmpty(host)) return DefaultCfg.ToBrand();
        host = host.Split(':')[0].ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        return (_cache.TryGetValue(host, out var b) ? b : DefaultCfg).ToBrand();
    }

    public Task<List<BrandConfig>> GetAllAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Brands.OrderBy(b => b.Host).ToListAsync();
    }

    public async Task SaveAsync(BrandConfig edited)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = await db.Brands.FirstOrDefaultAsync(b => b.Id == edited.Id);
            if (existing is null) return;
            existing.Name = edited.Name;
            existing.Tagline = edited.Tagline;
            existing.Accent = edited.Accent;
            existing.ProjectsUrl = edited.ProjectsUrl;
            await db.SaveChangesAsync();
        }
        await ReloadAsync();
    }

    public async Task AddAsync(BrandConfig b)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Brands.AnyAsync(x => x.Host == b.Host)) return;
            db.Brands.Add(b);
            await db.SaveChangesAsync();
        }
        await ReloadAsync();
    }
}
