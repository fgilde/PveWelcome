using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// Per-guest custom scripts: CRUD + run via the PVE guest agent (token, no SSH) or over SSH with live output.
public class ScriptService(AppDbContext db, PveDataService data, PveClient pve, ILogger<ScriptService> log)
{
    public Task<List<GuestScript>> ListAsync(int vmid) =>
        db.GuestScripts.AsNoTracking().Where(s => s.VmId == vmid).OrderBy(s => s.Name).ToListAsync();

    public Task<GuestScript?> GetAsync(int id) => db.GuestScripts.FirstOrDefaultAsync(s => s.Id == id);

    public async Task<int> SaveAsync(GuestScript s)
    {
        var tracked = db.GuestScripts.Local.FirstOrDefault(e => e.Id == s.Id);
        if (tracked is not null && !ReferenceEquals(tracked, s)) db.Entry(tracked).State = EntityState.Detached;
        if (s.Id == 0) db.GuestScripts.Add(s);
        else db.GuestScripts.Update(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var s = await db.GuestScripts.FindAsync(id);
        if (s is null) return;
        db.GuestScripts.Remove(s);
        await db.SaveChangesAsync();
    }

    /// Run a script; yields output chunks live (SSH) or the final output (PVE agent), persisting exit code + output.
    public async IAsyncEnumerable<string> RunAsync(int id, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var s = await GetAsync(id);
        if (s is null) { yield return "Script nicht gefunden."; yield break; }

        var sb = new StringBuilder();
        var exit = -1;

        if (s.ExecMode != "ssh")
        {
            var g = data.Guests.FirstOrDefault(x => x.VmId == s.VmId);
            if (g is null) { yield return "Guest nicht in PVE gefunden — SSH-Modus nutzen."; yield break; }
            if (g.Type != "qemu") { yield return $"PVE-Agent-Exec geht nur für QEMU-VMs. '{g.Name}' ist {g.Kind} — bitte SSH-Modus wählen."; yield break; }
            yield return $"[PVE-Agent-Exec · {g.Node}/#{s.VmId} · läuft…]\n";
            var (code, outp, err) = await pve.GuestExecAsync(g.Node, s.VmId, s.Content, ct);
            exit = code;
            if (!string.IsNullOrEmpty(outp)) { sb.Append(outp); yield return outp; }
            if (!string.IsNullOrEmpty(err)) { sb.Append(err); yield return err; }
            yield return $"\n[exit {exit}]";
            await PersistAsync(s, exit, sb.ToString(), id);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(s.SshHost)) { yield return "Kein SSH-Host konfiguriert."; yield break; }
        var ci = BuildCi(s);

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Content.Replace("\r\n", "\n")));
        var full = $"echo {b64} | base64 -d | bash";

        using (var client = new SshClient(ci))
        {
            client.Connect();
            using var cmd = client.CreateCommand(full);
            cmd.CommandTimeout = TimeSpan.FromMinutes(30);
            var ar = cmd.BeginExecute();
            using var reader = new StreamReader(cmd.OutputStream);
            var buf = new char[512];
            int n;
            while ((n = await reader.ReadAsync(buf, ct)) > 0)
            {
                var chunk = new string(buf, 0, n);
                sb.Append(chunk);
                yield return chunk;
            }
            cmd.EndExecute(ar);
            exit = cmd.ExitStatus ?? -1;
            var err = cmd.Error;
            if (!string.IsNullOrEmpty(err)) { sb.Append(err); yield return err; }
            client.Disconnect();
        }
        yield return $"\n[exit {exit}]";
        await PersistAsync(s, exit, sb.ToString(), id);
    }

    private async Task PersistAsync(GuestScript s, int exit, string output, int id)
    {
        try
        {
            s.LastExit = exit;
            s.LastOutput = output.Length > 20000 ? output[..20000] : output;
            s.LastRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { log.LogWarning(ex, "persist script run {Id}", id); }
    }

    /// List a directory on the SSH target (for the server file browser). Dirs first, name + isDir.
    public Task<List<DirEntry>> ListDirAsync(GuestScript s, string path) => Task.Run(() =>
    {
        var entries = new List<DirEntry>();
        try
        {
            using var client = new SshClient(BuildCi(s));
            client.Connect();
            using var cmd = client.RunCommand($"ls -1Ap --group-directories-first {Esc(path)} 2>/dev/null");
            foreach (var line in cmd.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var isDir = line.EndsWith('/');
                entries.Add(new DirEntry(isDir ? line[..^1] : line, isDir));
            }
            client.Disconnect();
        }
        catch (Exception ex) { log.LogWarning(ex, "ls {Path}", path); }
        return entries;
    });

    /// Read a file on the SSH target (to load its content into the editor).
    public Task<string> CatFileAsync(GuestScript s, string path) => Task.Run(() =>
    {
        try
        {
            using var client = new SshClient(BuildCi(s));
            client.Connect();
            using var cmd = client.RunCommand($"cat {Esc(path)}");
            var r = cmd.Result;
            client.Disconnect();
            return r;
        }
        catch (Exception ex) { return $"# Fehler beim Lesen: {ex.Message}"; }
    });

    private static Renci.SshNet.ConnectionInfo BuildCi(GuestScript s)
    {
        AuthenticationMethod auth = s.SshAuthIsKey
            ? new PrivateKeyAuthenticationMethod(s.SshUser, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(s.SshAuth))))
            : new PasswordAuthenticationMethod(s.SshUser, s.SshAuth);
        return new Renci.SshNet.ConnectionInfo(s.SshHost, s.SshPort, s.SshUser, auth) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static string Esc(string? p) => "'" + (string.IsNullOrEmpty(p) ? "/" : p).Replace("'", "'\\''") + "'";
}

public record DirEntry(string Name, bool IsDir);
