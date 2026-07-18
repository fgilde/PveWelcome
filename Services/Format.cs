namespace PveWelcome.Services;

public static class Format
{
    public static string Bytes(long? b)
    {
        if (b is null || b <= 0) return "–";
        double v = b.Value;
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }

    public static string Pct(double fraction) => $"{fraction * 100:0.#} %";

    public static string Uptime(long? seconds)
    {
        if (seconds is null || seconds <= 0) return "–";
        var t = TimeSpan.FromSeconds(seconds.Value);
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
        return $"{t.Minutes}m";
    }
}
