using System.Globalization;
using System.Text.Json;

namespace PveWelcome.Services;

/// Tiny i18n helper. German is the source (keys); English comes from Resources/en.json.
/// Untranslated strings fall back to the German key, so the app always renders.
/// Usage in Razor: @Loc.T("Dashboard")  ·  culture is set per request via RequestLocalization.
public static class Loc
{
    private static readonly Dictionary<string, string> En = Load();

    private static Dictionary<string, string> Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "en.json");
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
        }
        catch { /* fall back to German-only */ }
        return new();
    }

    public static bool IsEn => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    /// Translate a German source string to the current UI culture.
    public static string T(string de) => IsEn && En.TryGetValue(de, out var v) && !string.IsNullOrEmpty(v) ? v : de;
}
