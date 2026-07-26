using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Eidolon.App.Localization;

public static class SR
{
    private static Dictionary<string, string> _strings = new();
    private static string _culture = "cn";

    static SR() => Reload();

    private static void Load(string lang)
    {
        var name = $"Eidolon.App.Resources.strings.{lang}.json";
        try
        {
            using var s = typeof(SR).Assembly.GetManifestResourceStream(name);
            if (s is null) return;
            using var r = new StreamReader(s);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(r.ReadToEnd());
            if (dict is not null)
                foreach (var kv in dict)
                    _strings[kv.Key] = kv.Value;
        }
        catch { }
    }

    public static string Get(string key) =>
        _strings.TryGetValue(key, out var val) ? val : key;

    public static string Format(string key, params object?[] args) =>
        string.Format(Get(key), args);

    public static void Reload()
    {
        _strings.Clear();
        var name = CultureInfo.CurrentUICulture.Name;
        _culture = name.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "cn";
        // Always load Chinese as base, then overlay English if needed
        Load("cn");
        if (_culture == "en")
            Load("en");
    }

    public static string Culture => _culture;

    /// <summary>Switch language at runtime (e.g. from Settings).</summary>
    public static void SetLanguage(string lang)
    {
        lang = (lang ?? "cn").StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "cn";
        if (_culture == lang) return;
        _culture = lang;
        _strings.Clear();
        Load("cn");
        if (_culture == "en")
            Load("en");
    }
}
