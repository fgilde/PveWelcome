using Microsoft.EntityFrameworkCore;
using PveWelcome.Models;

namespace PveWelcome.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BrandConfig> Brands => Set<BrandConfig>();
    public DbSet<ConnectionSettings> Connections => Set<ConnectionSettings>();
    public DbSet<UptimeMonitor> Monitors => Set<UptimeMonitor>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<AiRun> AiRuns => Set<AiRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        b.Entity<BrandConfig>().HasIndex(x => x.Host).IsUnique();
    }
}
