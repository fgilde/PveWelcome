using Microsoft.EntityFrameworkCore;
using PveWelcome.Models;

namespace PveWelcome.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BrandConfig> Brands => Set<BrandConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        b.Entity<BrandConfig>().HasIndex(x => x.Host).IsUnique();
    }
}
