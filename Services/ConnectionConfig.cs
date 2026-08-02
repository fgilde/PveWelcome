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
        foreach (var col in new[] { "BackupStorage", "NotifyWebhook", "TelegramToken", "TelegramChatId", "Language", "McpUrl", "McpToken" })
            try { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"Connections\" ADD COLUMN \"{col}\" TEXT NOT NULL DEFAULT '';"); }
            catch { }
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"Monitors\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Monitors\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Name\" TEXT NOT NULL, \"Url\" TEXT NOT NULL, \"Enabled\" INTEGER NOT NULL DEFAULT 1);");
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"TotpSecret\" TEXT NULL;"); }
        catch { }
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"AiSettings\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_AiSettings\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Provider\" TEXT NOT NULL DEFAULT 'hermes', \"HermesHost\" TEXT NOT NULL DEFAULT '', " +
            "\"HermesPort\" INTEGER NOT NULL DEFAULT 22, \"HermesUser\" TEXT NOT NULL DEFAULT 'hermes', " +
            "\"HermesAuth\" TEXT NOT NULL DEFAULT '', \"HermesAuthIsKey\" INTEGER NOT NULL DEFAULT 0, " +
            "\"HermesCommand\" TEXT NOT NULL DEFAULT 'hermes', \"HermesModel\" TEXT NOT NULL DEFAULT '', " +
            "\"HermesYolo\" INTEGER NOT NULL DEFAULT 1, \"ClaudeApiKey\" TEXT NOT NULL DEFAULT '', " +
            "\"ClaudeModel\" TEXT NOT NULL DEFAULT 'claude-sonnet-5');");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"AiRuns\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_AiRuns\" PRIMARY KEY AUTOINCREMENT, " +
            "\"At\" TEXT NOT NULL, \"Provider\" TEXT NOT NULL, \"Prompt\" TEXT NOT NULL, " +
            "\"Result\" TEXT NOT NULL, \"Ok\" INTEGER NOT NULL DEFAULT 0);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"GuestScripts\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_GuestScripts\" PRIMARY KEY AUTOINCREMENT, " +
            "\"VmId\" INTEGER NOT NULL, \"Name\" TEXT NOT NULL, \"Content\" TEXT NOT NULL DEFAULT '', " +
            "\"SshHost\" TEXT NOT NULL DEFAULT '', \"SshPort\" INTEGER NOT NULL DEFAULT 22, " +
            "\"SshUser\" TEXT NOT NULL DEFAULT 'root', \"SshAuth\" TEXT NOT NULL DEFAULT '', " +
            "\"SshAuthIsKey\" INTEGER NOT NULL DEFAULT 0, \"LastExit\" INTEGER NULL, " +
            "\"LastOutput\" TEXT NULL, \"LastRunAt\" TEXT NULL, \"ExecMode\" TEXT NOT NULL DEFAULT 'pve');");
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"GuestScripts\" ADD COLUMN \"ExecMode\" TEXT NOT NULL DEFAULT 'pve';"); }
        catch { }
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"PhysicalMachines\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_PhysicalMachines\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Name\" TEXT NOT NULL DEFAULT '', \"Host\" TEXT NOT NULL DEFAULT '', " +
            "\"ProbePort\" INTEGER NOT NULL DEFAULT 3389, \"OsUser\" TEXT NOT NULL DEFAULT '', " +
            "\"Mac\" TEXT NOT NULL DEFAULT '', \"MacAlt\" TEXT NOT NULL DEFAULT '', " +
            "\"Broadcast\" TEXT NOT NULL DEFAULT '255.255.255.255', \"WolPort\" INTEGER NOT NULL DEFAULT 9, " +
            "\"JumpHost\" TEXT NOT NULL DEFAULT '', \"JumpPort\" INTEGER NOT NULL DEFAULT 22, " +
            "\"JumpUser\" TEXT NOT NULL DEFAULT 'root', \"JumpAuth\" TEXT NOT NULL DEFAULT '', " +
            "\"JumpAuthIsKey\" INTEGER NOT NULL DEFAULT 1, \"ShutdownCommand\" TEXT NOT NULL DEFAULT '', " +
            "\"GuacUrl\" TEXT NOT NULL DEFAULT '', \"PublicHost\" TEXT NOT NULL DEFAULT '', " +
            "\"Enabled\" INTEGER NOT NULL DEFAULT 1);");

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
        if (string.IsNullOrWhiteSpace(row.McpToken))
        {
            row.McpToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }
        _current = row;
        Loc.Lang = row.Language == "en" ? "en" : "de";
    }

    /// Generate a fresh MCP token (invalidates the old one) and persist it.
    public async Task<string> RegenerateMcpTokenAsync()
    {
        var t = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        _current.McpToken = t;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Connections.FirstOrDefaultAsync();
        if (row is not null) { row.McpToken = t; await db.SaveChangesAsync(); }
        return t;
    }

    /// Persist just the UI language (called by /set-lang) and apply it immediately.
    public async Task SetLanguageAsync(string lang)
    {
        lang = lang == "en" ? "en" : "de";
        Loc.Lang = lang;
        _current.Language = lang;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Connections.FirstOrDefaultAsync();
        if (row is not null) { row.Language = lang; await db.SaveChangesAsync(); }
    }

    /// Persist just the MCP endpoint URL (from the MCP admin page).
    public async Task SetMcpUrlAsync(string url)
    {
        url = (url ?? "").Trim();
        _current.McpUrl = url;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Connections.FirstOrDefaultAsync();
        if (row is not null) { row.McpUrl = url; await db.SaveChangesAsync(); }
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
            row.McpUrl = edited.McpUrl.Trim();
            if (isNew) db.Connections.Add(row);
            await db.SaveChangesAsync();
            _current = row;
        }
        Changed?.Invoke();
    }
}
