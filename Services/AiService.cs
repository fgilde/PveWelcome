using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// One chat turn. Role: "user" | "assistant".
public record ChatMsg(string Role, string Text);

/// AI integration: run a prompt against the configured provider (Hermes via SSH or Claude API).
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

    /// Stream a chat completion for a conversation. Yields output chunks as they arrive; audits at the end.
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMsg> conversation, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var s = await GetSettingsAsync();
        var sb = new StringBuilder();
        var lastUser = conversation.LastOrDefault(m => m.Role == "user")?.Text ?? "";
        var stream = s.Provider == "claude" ? StreamClaudeAsync(s, conversation, ct) : StreamHermesAsync(s, conversation, ct);
        try
        {
            await foreach (var chunk in stream.WithCancellation(ct)) { sb.Append(chunk); yield return chunk; }
        }
        finally
        {
            try
            {
                var res = sb.ToString();
                db.AiRuns.Add(new AiRun { At = DateTime.UtcNow, Provider = s.Provider, Prompt = lastUser, Result = res.Length > 20000 ? res[..20000] : res, Ok = sb.Length > 0 });
                await db.SaveChangesAsync();
            }
            catch { }
        }
    }

    private async IAsyncEnumerable<string> StreamClaudeAsync(AiSettings s, IReadOnlyList<ChatMsg> conv, [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.ClaudeApiKey)) throw new InvalidOperationException("Claude API-Key nicht konfiguriert.");
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.TryAddWithoutValidation("x-api-key", s.ClaudeApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        var msgs = conv.Select(m => new { role = m.Role == "assistant" ? "assistant" : "user", content = m.Text }).ToArray();
        var body = new { model = string.IsNullOrWhiteSpace(s.ClaudeModel) ? "claude-sonnet-5" : s.ClaudeModel, max_tokens = 4096, stream = true, messages = msgs };
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) { var e = await res.Content.ReadAsStringAsync(ct); throw new Exception($"{(int)res.StatusCode}: {e[..Math.Min(200, e.Length)]}"); }
        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (!line.StartsWith("data:")) continue;
            var json = line[5..].Trim();
            if (json.Length == 0 || json == "[DONE]") continue;
            string? text = null;
            try { using var d = JsonDocument.Parse(json); if (d.RootElement.TryGetProperty("delta", out var delta) && delta.TryGetProperty("text", out var t)) text = t.GetString(); }
            catch { }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }

    private async IAsyncEnumerable<string> StreamHermesAsync(AiSettings s, IReadOnlyList<ChatMsg> conv, [EnumeratorCancellation] CancellationToken ct)
    {
        var ci = BuildHermesCi(s);

        var tb = new StringBuilder();
        foreach (var m in conv) tb.AppendLine($"{(m.Role == "assistant" ? "Assistant" : "User")}: {m.Text}");
        var esc = tb.ToString().Replace("'", "'\\''");
        var full = $"{s.HermesCommand} -z '{esc}'";
        if (s.HermesYolo) full += " --yolo";
        if (!string.IsNullOrWhiteSpace(s.HermesModel)) full += $" -m {s.HermesModel}";

        using var client = new SshClient(ci);
        client.Connect();
        using var cmd = client.CreateCommand(full);
        cmd.CommandTimeout = TimeSpan.FromMinutes(5);
        var async = cmd.BeginExecute();
        using var reader = new StreamReader(cmd.OutputStream);
        var buf = new char[512];
        int n;
        while ((n = await reader.ReadAsync(buf, ct)) > 0)
            yield return new string(buf, 0, n);
        cmd.EndExecute(async);
        client.Disconnect();
    }

    private static Renci.SshNet.ConnectionInfo BuildHermesCi(AiSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.HermesHost)) throw new InvalidOperationException("Hermes-Host nicht konfiguriert.");
        AuthenticationMethod auth = s.HermesAuthIsKey
            ? new PrivateKeyAuthenticationMethod(s.HermesUser, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(s.HermesAuth))))
            : new PasswordAuthenticationMethod(s.HermesUser, s.HermesAuth);
        return new Renci.SshNet.ConnectionInfo(s.HermesHost, s.HermesPort, s.HermesUser, auth) { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// Register this app's MCP endpoint into the Hermes config (idempotent); returns a human-readable status.
    public async Task<string> RegisterMcpAsync(string mcpUrl)
    {
        var s = await GetSettingsAsync();
        if (s.Provider != "hermes" || string.IsNullOrWhiteSpace(s.HermesHost))
            return "Interne AI (Hermes) ist nicht konfiguriert — nichts einzutragen.";
        if (string.IsNullOrWhiteSpace(mcpUrl) || !mcpUrl.Contains("://")) return "Bitte eine gültige MCP-URL angeben (z. B. http://…/mcp).";
        var ci = BuildHermesCi(s);
        var script =
            "CFG=\"$HOME/.hermes/config.yaml\"; mkdir -p \"$HOME/.hermes\"; touch \"$CFG\"; " +
            "grep -q '^mcp_servers:' \"$CFG\" || printf '\\nmcp_servers:\\n' >> \"$CFG\"; " +
            "sed -i '/^  pvewelcome:/{N;d;}' \"$CFG\"; " +
            $"sed -i '/^mcp_servers:/a\\  pvewelcome:\\n    url: {mcpUrl}' \"$CFG\"; echo OK";
        return await Task.Run(() =>
        {
            try
            {
                using var client = new SshClient(ci);
                client.Connect();
                using var cmd = client.CreateCommand(script);
                var outp = (cmd.Execute() ?? "").Trim();
                var err = cmd.Error;
                client.Disconnect();
                return outp.Contains("OK")
                    ? "✅ PveWelcome-MCP in die interne AI (Hermes) eingetragen/aktualisiert."
                    : (string.IsNullOrWhiteSpace(err) ? "Fertig." : "Fehler: " + err);
            }
            catch (Exception ex) { log.LogWarning(ex, "register mcp"); return "Fehler: " + ex.Message; }
        });
    }

    private static Task<string> RunHermesAsync(AiSettings s, string prompt)
    {
        var ci = BuildHermesCi(s);

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
