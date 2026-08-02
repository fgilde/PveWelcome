using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using PveWelcome.Data;
using PveWelcome.Models;

namespace PveWelcome.Services;

/// CRUD + online probe, Wake-on-LAN and remote shutdown for physical LAN machines.
public class PhysicalMachineService(AppDbContext db, ILogger<PhysicalMachineService> log)
{
    public Task<List<PhysicalMachine>> ListAsync() =>
        db.PhysicalMachines.AsNoTracking().OrderBy(m => m.Name).ToListAsync();

    public Task<PhysicalMachine?> GetAsync(int id) =>
        db.PhysicalMachines.FirstOrDefaultAsync(m => m.Id == id);

    public async Task<int> SaveAsync(PhysicalMachine m)
    {
        var tracked = db.PhysicalMachines.Local.FirstOrDefault(e => e.Id == m.Id);
        if (tracked is not null && !ReferenceEquals(tracked, m)) db.Entry(tracked).State = EntityState.Detached;
        if (m.Id == 0) db.PhysicalMachines.Add(m);
        else db.PhysicalMachines.Update(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    public async Task DeleteAsync(int id)
    {
        var m = await db.PhysicalMachines.FindAsync(id);
        if (m is null) return;
        db.PhysicalMachines.Remove(m);
        await db.SaveChangesAsync();
    }

    /// True when the probe port accepts a connection. Routed traffic, so this works from inside the container.
    public static async Task<bool> IsOnlineAsync(PhysicalMachine m, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(m.Host)) return false;
        try
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync(m.Host, m.ProbePort, timeout.Token);
            return tcp.Connected;
        }
        catch { return false; }
    }

    /// 6×0xFF followed by the MAC repeated 16 times. Accepts "aa:bb:…", "aa-bb-…" or bare hex.
    public static byte[] MagicPacket(string mac)
    {
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12) throw new ArgumentException($"MAC muss 6 Bytes haben, bekam '{mac}'", nameof(mac));
        var addr = Convert.FromHexString(hex);
        var packet = new byte[6 + 16 * 6];
        packet.AsSpan(0, 6).Fill(0xFF);
        for (var i = 0; i < 16; i++) addr.CopyTo(packet, 6 + i * 6);
        return packet;
    }

    /// Send the magic packet from the jump host (the app container's bridge swallows broadcasts).
    public async Task<string> WakeAsync(PhysicalMachine m, CancellationToken ct = default)
    {
        var macs = new[] { m.Mac, m.MacAlt }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (macs.Length == 0) return Loc.T("Keine MAC-Adresse hinterlegt.");

        var sb = new StringBuilder("import socket,base64\n");
        sb.Append("s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)\n");
        sb.Append("s.setsockopt(socket.SOL_SOCKET,socket.SO_BROADCAST,1)\n");
        foreach (var mac in macs)
        {
            var b64 = Convert.ToBase64String(MagicPacket(mac));
            sb.Append($"s.sendto(base64.b64decode('{b64}'),('{m.Broadcast}',{m.WolPort}))\n");
        }
        sb.Append($"print('sent {macs.Length} packet(s) to {m.Broadcast}:{m.WolPort}')\n");

        return await RunOnJumpAsync(m, $"echo {Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()))} | base64 -d | python3 -", ct);
    }

    /// Run the configured shutdown command on the jump host, which reaches the machine over SSH.
    public async Task<string> ShutdownAsync(PhysicalMachine m, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(m.ShutdownCommand)) return Loc.T("Kein Shutdown-Befehl hinterlegt.");
        var cmd = m.ShutdownCommand.Replace("{host}", m.Host).Replace("{user}", m.OsUser);
        return await RunOnJumpAsync(m, cmd, ct);
    }

    private Task<string> RunOnJumpAsync(PhysicalMachine m, string command, CancellationToken ct) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(m.JumpHost)) return Loc.T("Kein Jump-Host konfiguriert.");
        try
        {
            AuthenticationMethod auth = m.JumpAuthIsKey
                ? new PrivateKeyAuthenticationMethod(m.JumpUser, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(m.JumpAuth))))
                : new PasswordAuthenticationMethod(m.JumpUser, m.JumpAuth);
            var ci = new Renci.SshNet.ConnectionInfo(m.JumpHost, m.JumpPort, m.JumpUser, auth) { Timeout = TimeSpan.FromSeconds(15) };

            using var client = new SshClient(ci);
            client.Connect();
            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromSeconds(60);
            var result = cmd.Execute();
            var err = cmd.Error;
            client.Disconnect();
            var text = (result + err).Trim();
            return cmd.ExitStatus == 0
                ? (text.Length == 0 ? Loc.T("OK") : text)
                : Loc.T("Fehler (exit {0}): {1}", cmd.ExitStatus ?? -1, text);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "jump command on {Host}", m.JumpHost);
            return Loc.T("Fehler: {0}", ex.Message);
        }
    }, ct);

    /// A .rdp file the browser can hand to the local mstsc / Remote Desktop client.
    public static string RdpFile(PhysicalMachine m)
    {
        var sb = new StringBuilder();
        sb.Append($"full address:s:{m.Host}:{m.ProbePort}\r\n");
        if (!string.IsNullOrWhiteSpace(m.OsUser)) sb.Append($"username:s:{m.OsUser}\r\n");
        sb.Append("prompt for credentials:i:1\r\n");
        sb.Append("screen mode id:i:2\r\n");
        sb.Append("audiomode:i:0\r\n");
        sb.Append("redirectclipboard:i:1\r\n");
        sb.Append("authentication level:i:2\r\n");
        return sb.ToString();
    }

    /// Assert-based self-check (Program.cs --selfcheck). Fails loudly if the packet layout breaks.
    public static void SelfCheck()
    {
        var p = MagicPacket("74:56:3C:FF:5F:8F");
        if (p.Length != 102) throw new Exception($"Länge {p.Length}, erwartet 102");
        if (p.Take(6).Any(b => b != 0xFF)) throw new Exception("Präambel ist nicht 6×0xFF");
        var mac = new byte[] { 0x74, 0x56, 0x3C, 0xFF, 0x5F, 0x8F };
        for (var i = 0; i < 16; i++)
            if (!p.Skip(6 + i * 6).Take(6).SequenceEqual(mac)) throw new Exception($"MAC-Wiederholung {i} falsch");
        if (!MagicPacket("74-56-3c-ff-5f-8f").SequenceEqual(p)) throw new Exception("Trennzeichen-Varianten weichen ab");
        if (!MagicPacket("74563cff5f8f").SequenceEqual(p)) throw new Exception("Bare-Hex weicht ab");
        try { MagicPacket("74:56:3C"); throw new Exception("Zu kurze MAC hätte werfen müssen"); }
        catch (ArgumentException) { }

        var rdp = RdpFile(new PhysicalMachine { Host = "192.168.178.54", ProbePort = 3389, OsUser = "User" });
        if (!rdp.Contains("full address:s:192.168.178.54:3389")) throw new Exception("RDP-Adresszeile fehlt");
        if (!rdp.Contains("username:s:User")) throw new Exception("RDP-Benutzerzeile fehlt");

        Console.WriteLine("PhysicalMachineService self-check OK");
    }
}
