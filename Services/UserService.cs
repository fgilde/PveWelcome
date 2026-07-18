using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

public class UserService(AppDbContext db, IPasswordHasher<AppUser> hasher)
{
    /// Ensure the DB exists and seed the admin from env/config on first run.
    public async Task InitAsync(string? adminUser, string? adminPassword)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync()) return;
        if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPassword)) return;

        var u = new AppUser { Username = adminUser.Trim(), Role = "Admin" };
        u.PasswordHash = hasher.HashPassword(u, adminPassword);
        db.Users.Add(u);
        await db.SaveChangesAsync();
    }

    public async Task<AppUser?> ValidateAsync(string username, string password)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Username == username);
        if (u is null) return null;
        var ok = hasher.VerifyHashedPassword(u, u.PasswordHash, password);
        return ok != PasswordVerificationResult.Failed ? u : null;
    }

    public Task<List<AppUser>> ListAsync() => db.Users.OrderBy(u => u.Username).ToListAsync();

    public async Task<bool> CreateAsync(string username, string password, string role)
    {
        username = username.Trim();
        if (await db.Users.AnyAsync(u => u.Username == username)) return false;
        var u = new AppUser { Username = username, Role = role };
        u.PasswordHash = hasher.HashPassword(u, password);
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task DeleteAsync(int id)
    {
        var u = await db.Users.FindAsync(id);
        if (u is null) return;
        // keep at least one user so nobody locks themselves out
        if (await db.Users.CountAsync() <= 1) return;
        db.Users.Remove(u);
        await db.SaveChangesAsync();
    }
}
