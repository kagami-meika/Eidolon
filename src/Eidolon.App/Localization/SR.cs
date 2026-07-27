using System.IO;
using System.Text.Json;

namespace Eidolon.App.Localization;

public static class SR
{
    private static Dictionary<string, string> _strings = new();
    private static string _culture = "cn";

    // Default to Chinese base strings. App.OnStartup will apply the cold language from setting.json.
    // Do NOT infer from CultureInfo.CurrentUICulture — that races with cold-switch and can make
    // SetLanguage("en") no-op if the OS UI culture is already English.
    static SR() => LoadCulture("cn");

    private static void Load(string lang)
    {
        var name = $"Eidolon.App.Resources.strings.{lang}.json";
        try
        {
            using var s = typeof(SR).Assembly.GetManifestResourceStream(name);
            if (s is null)
            {
                System.Console.Error.WriteLine($"[SR] missing resource: {name}");
                return;
            }
            using var r = new StreamReader(s);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(r.ReadToEnd());
            if (dict is not null)
                foreach (var kv in dict)
                    _strings[kv.Key] = kv.Value;
            System.Console.Error.WriteLine($"[SR] loaded resource: {name} keys={dict?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"[SR] load failed {name}: {ex.Message}");
        }
    }

    private static void LoadCulture(string lang)
    {
        lang = Normalize(lang);
        _culture = lang;
        _strings.Clear();
        // Always load Chinese as base, then overlay English if needed.
        Load("cn");
        if (_culture == "en")
            Load("en");
    }

    private static string Normalize(string? lang) =>
        (lang ?? "cn").StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "cn";

    public static string Get(string key) =>
        _strings.TryGetValue(key, out var val) ? val : key;

    public static string Format(string key, params object?[] args) =>
        string.Format(Get(key), args);

    public static void Reload() => LoadCulture(_culture);

    public static string Culture => _culture;

    /// <summary>Fires when the language dictionary is reloaded.</summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// Apply language for this process (cold-switch: called once from App.OnStartup).
    /// Always reloads the dictionary so a prior static default cannot block the switch.
    /// </summary>
    public static void SetLanguage(string lang)
    {
        LoadCulture(lang);
        LanguageChanged?.Invoke();
    }
}
