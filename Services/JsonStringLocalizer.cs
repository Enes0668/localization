using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace LocalizationApi.Services;

/// <summary>
/// Çevirileri verilen JSON metninden (veya dosyadan) okuyup RAM'de saklayan 
/// ve talep edildiğinde dile göre çevirisini veren servis.
/// </summary>
public class JsonStringLocalizer : IJsonStringLocalizer
{
    private readonly string? _languagesJson;
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Cache = new();

    public JsonStringLocalizer(string? languagesJson = null)
    {
        _languagesJson = languagesJson;
    }

    public string this[string key] => GetString(key);

    public string this[string key, params object[] arguments] => GetString(key, arguments);

    public string GetString(string key)
    {
        string currentCulture = CultureInfo.CurrentUICulture.Name;
        return GetWithCulture(key, currentCulture);
    }

    public string GetString(string key, params object[] arguments)
    {
        string currentCulture = CultureInfo.CurrentUICulture.Name;
        return GetWithCulture(key, currentCulture, arguments);
    }

    public string GetWithCulture(string key, string culture)
    {
        Dictionary<string, string> dictionary = GetDictionaryForCulture(culture);

        if (dictionary.TryGetValue(key, out string? value))
        {
            return value;
        }

        return key;
    }

    public string GetWithCulture(string key, string culture, params object[] arguments)
    {
        string template = GetWithCulture(key, culture);

        if (arguments == null || arguments.Length == 0)
        {
            return template;
        }

        try
        {
            CultureInfo cultureInfo;
            try
            {
                cultureInfo = CultureInfo.GetCultureInfo(culture);
            }
            catch
            {
                cultureInfo = CultureInfo.CurrentCulture;
            }

            return string.Format(cultureInfo, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public IEnumerable<string> GetAllKeysForCulture(string culture)
    {
        return GetDictionaryForCulture(culture).Keys;
    }

    public IEnumerable<string> GetSupportedCultures()
    {
        string? json = _languagesJson;
        if (string.IsNullOrWhiteSpace(json) && File.Exists("Localization/localization.json"))
        {
            json = File.ReadAllText("Localization/localization.json");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string> { "tr" };
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        }
        catch
        {
            return new List<string> { "tr" };
        }
    }

    private Dictionary<string, string> GetDictionaryForCulture(string cultureName)
    {
        string language = cultureName.Split('-')[0].ToLowerInvariant();

        if (Cache.TryGetValue(language, out var cachedDict))
        {
            return cachedDict;
        }

        string? json = _languagesJson;
        if (string.IsNullOrWhiteSpace(json) && File.Exists("Localization/localization.json"))
        {
            json = File.ReadAllText("Localization/localization.json");
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty(language, out var langElem) && langElem.ValueKind == JsonValueKind.Object)
                {
                    var dict = FlattenJson(langElem);
                    Cache[language] = dict;
                    return dict;
                }
            }
            catch { }
        }

        var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Cache[language] = empty;
        return empty;
    }

    private static Dictionary<string, string> FlattenJson(JsonElement element, string prefix = "")
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            string currentKey = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in FlattenJson(property.Value, currentKey))
                {
                    result[item.Key] = item.Value;
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[currentKey] = property.Value.GetString() ?? currentKey;
            }
        }

        return result;
    }
}
