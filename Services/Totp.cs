using System.Security.Cryptography;
using System.Text;

namespace PveWelcome.Services;

/// Minimal RFC 6238 TOTP (SHA1, 6 digits, 30 s) + Base32. No external dependency.
/// Enrollment requires a valid code before enabling, which doubles as the runtime self-check.
public static class Totp
{
    private const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static bool Verify(string? secret, string? code, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;
        code = code.Trim().Replace(" ", "");
        var key = Base32Decode(secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (long i = -window; i <= window; i++)
            if (Code(key, step + i) == code) return true;
        return false;
    }

    public static string OtpAuthUri(string secret, string account, string issuer = "PveWelcome") =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&period=30&digits=6";

    private static string Code(byte[] key, long counter)
    {
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        using var h = new HMACSHA1(key);
        var hash = h.ComputeHash(msg);
        int off = hash[^1] & 0x0f;
        int bin = ((hash[off] & 0x7f) << 24) | ((hash[off + 1] & 0xff) << 16) | ((hash[off + 2] & 0xff) << 8) | (hash[off + 3] & 0xff);
        return (bin % 1_000_000).ToString("D6");
    }

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int bits = 0, val = 0;
        foreach (var b in data)
        {
            val = (val << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(B32[(val >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(B32[(val << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string s)
    {
        s = s.TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        var bytes = new List<byte>();
        int bits = 0, val = 0;
        foreach (var c in s)
        {
            var idx = B32.IndexOf(c);
            if (idx < 0) continue;
            val = (val << 5) | idx; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((val >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return bytes.ToArray();
    }
}
