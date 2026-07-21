using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// AI integration: run a prompt against the configured provider.
/// - hermes: SSH into the Hermes agent host and run `hermes -z '...'` (agent has its own SSH + tools => full power).
/// - claude: Anthropic Messages API (text only in this version, no system tools).
public class AiService(AppDbContext db, IHttpClientFactory httpFactory, ILogger<AiService> log)
{
    public async Task<AiSettings> GetSettingsAsync()
        => await db.AiSettings.FirstOrDefaultAsync() ?? new AiSettings();

    public async Task SaveSettingsAsync(AiSettings edited)
    {
        var row = await db.AiSettings.FirstOrDefaultAsync();
        if (row is null) { db.AiSettings.Add(edited); }
        else
        {
            row.Provider = edited.Provider;
            row.HermesHost = edited.HermesHost.Trim(); row.HermesPort = edited.HermesPort;
            row.HermesUser = edited.HermesUser.Trim(); row.HermesAuth = edited.HermesAuth;
            row.HermesAuthIsKey = edited.HermesAuthIsKey; row.HermesCommand = string.IsNullOrWhiteSpace(edited.HermesCommand) ? "hermes" : edited.HermesCommand.Trim();
            row.HermesModel = edited.HermesModel.Trim(); row.HermesYolo = edited.HermesYolo;
            row.ClaudeApiKey = edited.ClaudeApiKey.Trim(); row.ClaudeModel = edited.ClaudeModel.Trim();
        }
        await db.SaveChangesAsync();
    }

    public Task<List<AiRun>> RecentAsync(int n = 15)
        => db.AiRuns.OrderByDescending(r => r.At).Take(n).ToListAsync();

    /// Run a prompt; returns the agent/model output (or an error string). Always audited.
    public async Task<(bool ok, string output)> SendAsync(string prompt)
    {
        var s = await GetSettingsAsync();
        bool ok;
        string output;
        try
        {
            output = s.Provider == "claude" ? await RunClaudeAsync(s, prompt) : await RunHermesAsync(s, prompt);
            ok = true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ai send ({Provider})", s.Provider);
            output = $"Fehler: {ex.Message}";
            ok = false;
        }
        db.AiRuns.Add(new AiRun { At = DateTime.UtcNow, Provider = s.Provider, Prompt = prompt, Result = output.Length > 20000 ? output[..20000] : output, Ok = ok });
        await db.SaveChangesAsync();
        return (ok, output);
    }

    private static Task<string> RunHermesAsync(AiSettings s, string prompt)
    {
        if (string.IsNullOrWhiteSpace(s.HermesHost)) throw new InvalidOperationException("Hermes-Host nicht konfiguriert.");
        AuthenticationMethod auth = s.HermesAuthIsKey
            ? new PrivateKeyAuthenticationMethod(s.HermesUser, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(s.HermesAuth))))
            : new PasswordAuthenticationMethod(s.HermesUser, s.HermesAuth);
        var ci = new Renci.SshNet.ConnectionInfo(s.HermesHost, s.HermesPort, s.HermesUser, auth) { Timeout = TimeSpan.FromSeconds(15) };

        var esc = prompt.Replace("'", "'\\''");
        var full = $"{s.HermesCommand} -z '{esc}'";
        if (s.HermesYolo) full += " --yolo";
        if (!string.IsNullOrWhiteSpace(s.HermesModel)) full += $" -m {s.HermesModel}";

        using var client = new SshClient(ci);
        client.Connect();
        using var cmd = client.CreateCommand(full);
        cmd.CommandTimeout = TimeSpan.FromMinutes(5);
        var outp = cmd.Execute();
        var err = cmd.Error;
        client.Disconnect();
        var res = (outp ?? "").Trim();
        if (res.Length == 0) res = (err ?? "").Trim();
        return Task.FromResult(res.Length == 0 ? "(keine Ausgabe)" : res);
    }

    private async Task<string> RunClaudeAsync(AiSettings s, string prompt)
    {
        if (string.IsNullOrWhiteSpace(s.ClaudeApiKey)) throw new InvalidOperationException("Claude API-Key nicht konfiguriert.");
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.TryAddWithoutValidation("x-api-key", s.ClaudeApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        var body = new { model = string.IsNullOrWhiteSpace(s.ClaudeModel) ? "claude-sonnet-5" : s.ClaudeModel, max_tokens = 2048, messages = new[] { new { role = "user", content = prompt } } };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new Exception($"{(int)res.StatusCode}: {raw[..Math.Min(200, raw.Length)]}");
        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
            .Select(c => c.GetProperty("text").GetString()).FirstOrDefault();
        return text ?? "(keine Ausgabe)";
    }
}
