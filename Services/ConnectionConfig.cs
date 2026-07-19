using Microsoft.EntityFrameworkCore;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// Holds the current PVE/NPM connection settings (DB-backed, seeded from env). Singleton + cached.
public class ConnectionConfig(IServiceScopeFactory scopeFactory)
{
    private ConnectionSettings _current = new();
    public ConnectionSettings Current => _current;

    public event Action? Changed;

    public async Task InitAsync(IConfiguration config)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"Connections\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Connections\" PRIMARY KEY AUTOINCREMENT, " +
            "\"PveBaseUrl\" TEXT NOT NULL, \"PveApiToken\" TEXT NOT NULL, " +
            "\"NpmBaseUrl\" TEXT NOT NULL, \"NpmUser\" TEXT NOT NULL, \"NpmPassword\" TEXT NOT NULL, " +
            "\"BackupStorage\" TEXT NOT NULL DEFAULT '');");
        // add columns for DBs created before these existed (ignore "already exists")
        foreach (var col in new[] { "BackupStorage", "NotifyWebhook", "TelegramToken", "TelegramChatId" })
            try { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"Connections\" ADD COLUMN \"{col}\" TEXT NOT NULL DEFAULT '';"); }
            catch { /* column already exists */ }

        var row = await db.Connections.FirstOrDefaultAsync();
        if (row is null)
        {
            row = new ConnectionSettings
            {
                PveBaseUrl = config["Pve:BaseUrl"] ?? "",
                PveApiToken = config["Pve:ApiToken"] ?? "",
                NpmBaseUrl = config["Npm:BaseUrl"] ?? "",
                NpmUser = config["Npm:User"] ?? "",
                NpmPassword = config["Npm:Password"] ?? "",
            };
            db.Connections.Add(row);
            await db.SaveChangesAsync();
        }
        _current = row;
    }

    public async Task SaveAsync(ConnectionSettings edited)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Connections.FirstOrDefaultAsync() ?? new ConnectionSettings();
            var isNew = row.Id == 0 && !await db.Connections.AnyAsync();
            row.PveBaseUrl = edited.PveBaseUrl.Trim();
            row.PveApiToken = edited.PveApiToken.Trim();
            row.NpmBaseUrl = edited.NpmBaseUrl.Trim();
            row.NpmUser = edited.NpmUser.Trim();
            row.NpmPassword = edited.NpmPassword;
            row.BackupStorage = edited.BackupStorage.Trim();
            row.NotifyWebhook = edited.NotifyWebhook.Trim();
            row.TelegramToken = edited.TelegramToken.Trim();
            row.TelegramChatId = edited.TelegramChatId.Trim();
            if (isNew) db.Connections.Add(row);
            await db.SaveChangesAsync();
            _current = row;
        }
        Changed?.Invoke();
    }
}
