using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace LocalizationApi.Services;

public class JsonStringLocalizer : IJsonStringLocalizer
{
    private readonly IWebHostEnvironment _env;
    private readonly string _localizationPath;

    // Cache: culture adı → düzleştirilmiş anahtar/değer sözlüğü
    // Örnek: "tr-TR" → { "Auth.Login": "Giriş yap", "Errors.NotFound": "... bulunamadı." }
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Cache = new();

    public JsonStringLocalizer(IWebHostEnvironment env)
    {
        _env = env;
        _localizationPath = Path.Combine(_env.ContentRootPath, "Localization");
    }

    public string this[string key] => GetString(key);

    public string this[string key, params object[] arguments] => GetString(key, arguments);

    public string GetString(string key)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        var strings = GetDictionaryForCulture(culture);

        if (strings.TryGetValue(key, out var value))
        {
            return value;
        }

        // Anahtar bulunamazsa anahtarın adını dön (Standart .NET fallback davranışı)
        return key;
    }

    public string GetString(string key, params object[] arguments)
    {
        var template = GetString(key);

        if (arguments == null || arguments.Length == 0)
        {
            return template;
        }

        try
        {
            // Parametrik yerelleştirme: {0}, {1} yer tutucuları değerlerle değiştirilir
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException)
        {
            // Şablon ile argüman sayısı/tipi uyuşmazsa ham şablonu dön
            return template;
        }
    }

    /// <summary>
    /// Mevcut kültür için yüklenmiş tüm key'leri döner.
    /// Kategorili yapıda: "Auth.Login", "Errors.NotFound" gibi düzleştirilmiş anahtarlar.
    /// </summary>
    public IEnumerable<string> GetAllKeys()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        return GetDictionaryForCulture(culture).Keys;
    }

    /// <summary>
    /// Belirli bir kültür için tüm key'leri döner.
    /// </summary>
    public IEnumerable<string> GetAllKeysForCulture(string culture)
        => GetDictionaryForCulture(culture).Keys;

    /// <summary>
    /// Belirli bir kültür için key çevirisini döner.
    /// Key bulunamazsa key'in kendisini döner.
    /// </summary>
    public string GetWithCulture(string key, string culture)
    {
        var strings = GetDictionaryForCulture(culture);
        return strings.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// Belirli bir kültür için key çevirisini parametrelerle formatlayarak döner.
    /// Key bulunamazsa key'in kendisini döner.
    /// </summary>
    public string GetWithCulture(string key, string culture, params object[] arguments)
    {
        var template = GetWithCulture(key, culture);

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
            catch (CultureNotFoundException)
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

    public static string NormalizeCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return "tr";

        var code = cultureName.Split('-')[0].ToLowerInvariant();
        return code switch
        {
            "en" => "en",
            _    => "tr"
        };
    }

    private Dictionary<string, string> GetDictionaryForCulture(string cultureName)
    {
        var lang = NormalizeCulture(cultureName);

        if (Cache.TryGetValue(lang, out var cachedDict))
        {
            return cachedDict;
        }

        var filePath = Path.Combine(_localizationPath, "localization.json");

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
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
                // JSON okuma veya parse hatası durumunda boş sözlük dön
            }
        }

        var emptyDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Cache[lang] = emptyDict;
        return emptyDict;
    }

    /// <summary>
    /// İç içe JSON nesnesini nokta notasyonuyla düz sözlüğe çevirir.
    /// Örnek: { "Auth": { "Login": "Giriş yap" } } → { "Auth.Login": "Giriş yap" }
    /// </summary>
    private static Dictionary<string, string> FlattenJson(JsonElement element, string prefix = "")
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            var fullKey = string.IsNullOrEmpty(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                // Alt nesneye özyinelemeli dallan
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

    /// <summary>
    /// Belirtilen kültür için cache'i temizler. Sonraki istekte JSON yeniden yüklenir.
    /// </summary>
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
