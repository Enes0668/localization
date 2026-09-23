using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Enes3.Services;

public class JsonStringLocalizer : IJsonStringLocalizer
{
    private readonly string _filePath;
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Cache = new();

    public JsonStringLocalizer(string localizationPath)
    {
        if (File.Exists(localizationPath))
        {
            _filePath = localizationPath;
        }
        else if (Directory.Exists(localizationPath))
        {
            _filePath = Path.Combine(localizationPath, "localization.json");
        }
        else
        {
            // Varsayılan göreceli yol
            _filePath = Path.Combine(AppContext.BaseDirectory, "Localization", "localization.json");
            if (!File.Exists(_filePath))
            {
                _filePath = Path.Combine(Directory.GetCurrentDirectory(), "Localization", "localization.json");
            }
        }
    }

    public JsonStringLocalizer(IWebHostEnvironment env)
        : this(Path.Combine(env.ContentRootPath, "Localization", "localization.json"))
    {
    }

    public string this[string key] => GetString(key);

    public string this[string key, params object[] arguments] => GetString(key, arguments);

    public string GetString(string key)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        return GetWithCulture(key, culture);
    }

    public string GetString(string key, params object[] arguments)
    {
        var template = GetString(key);
        if (arguments == null || arguments.Length == 0) return template;
        try { return string.Format(CultureInfo.CurrentCulture, template, arguments); }
        catch { return template; }
    }

    public IEnumerable<string> GetAllKeys() => GetAllKeysForCulture(CultureInfo.CurrentUICulture.Name);

    public IEnumerable<string> GetAllKeysForCulture(string culture)
        => GetDictionaryForCulture(culture).Keys;

    public string GetWithCulture(string key, string culture)
    {
        var strings = GetDictionaryForCulture(culture);
        return strings.TryGetValue(key, out var value) ? value : key;
    }

    public string GetWithCulture(string key, string culture, params object[] arguments)
    {
        var template = GetWithCulture(key, culture);
        if (arguments == null || arguments.Length == 0) return template;
        try
        {
            CultureInfo ci;
            try { ci = CultureInfo.GetCultureInfo(culture); }
            catch { ci = CultureInfo.CurrentCulture; }
            return string.Format(ci, template, arguments);
        }
        catch { return template; }
    }

    public IEnumerable<string> GetSupportedCultures()
    {
        if (!File.Exists(_filePath)) return new[] { "tr", "en" };
        try
        {
            var json = File.ReadAllText(_filePath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        }
        catch { return new[] { "tr", "en" }; }
    }

    public static string NormalizeCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return "tr";
        return cultureName.Split('-', ',')[0].Trim().ToLowerInvariant();
    }

    private Dictionary<string, string> GetDictionaryForCulture(string cultureName)
    {
        var lang = NormalizeCulture(cultureName);

        if (Cache.TryGetValue(lang, out var cachedDict))
        {
            return cachedDict;
        }

        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.TryGetProperty(lang, out var langElement) &&
                    langElement.ValueKind == JsonValueKind.Object)
                {
                    var dict = FlattenJson(langElement);
                    Cache[lang] = dict;
                    return dict;
                }
            }
            catch
            {
                // Parse hatası
            }
        }

        var emptyDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Cache[lang] = emptyDict;
        return emptyDict;
    }

    private static Dictionary<string, string> FlattenJson(JsonElement element, string prefix = "")
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            var fullKey = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in FlattenJson(property.Value, fullKey))
                {
                    result[nested.Key] = nested.Value;
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[fullKey] = property.Value.GetString() ?? fullKey;
            }
        }

        return result;
    }

    public static void ClearCache(string? cultureName = null)
    {
        if (cultureName != null)
        {
            var lang = NormalizeCulture(cultureName);
            Cache.TryRemove(lang, out _);
        }
        else
        {
            Cache.Clear();
        }
    }
}
