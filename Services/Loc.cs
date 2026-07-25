using System.Text.Json;

namespace PveWelcome.Services;

/// Tiny i18n helper. German is the source; English comes from Resources/en.json, falling back to the German key.
public static class Loc
{
    private static readonly Dictionary<string, string> En = Load();

    /// Active language ("de" | "en"), set at startup + on /set-lang from persisted settings.
    public static string Lang { get; set; } = "de";

    private static Dictionary<string, string> Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "en.json");
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch { }
        return new();
    }

    public static bool IsEn => Lang == "en";

    /// Translate a German source string to the current UI culture.
    public static string T(string de) => IsEn && En.TryGetValue(de, out var v) && !string.IsNullOrEmpty(v) ? v : de;
}
