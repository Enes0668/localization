using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Enes3.Middlewares;

namespace Enes3.Services;

public class JsonStringLocalizer : IJsonStringLocalizer
{
    private string? _filePath;
    private string? _rawJsonContent;
    private readonly Func<string>? _jsonProvider;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache = new();

    /// <summary>
    /// ResponseLocalizationOptions üzerinden yapılandırılan constructor.
    /// JsonContent varsa doğrudan hafızadan çalışır, yoksa JsonFilePath'i kullanır.
    /// </summary>
    public JsonStringLocalizer(ResponseLocalizationOptions options)
    {
        _rawJsonContent = options.JsonContent;
        _jsonProvider = options.JsonContentProvider;
        _filePath = options.JsonFilePath;
    }

    /// <summary>
    /// Doğrudan JSON string içeriği veya dosya yolu alan constructor.
    /// Eğer gelen metin '{' veya '[' ile başlıyorsa doğrudan JSON içeriği olarak algılar (Veritabanı vb. için).
    /// </summary>
    public JsonStringLocalizer(string jsonContentOrPath, bool isContent = false)
    {
        if (isContent || LooksLikeJson(jsonContentOrPath))
        {
            _rawJsonContent = jsonContentOrPath;
        }
        else
        {
            _filePath = ResolveFilePath(jsonContentOrPath);
        }
    }

    /// <summary>
    /// JSON içeriğini dinamik olarak getiren bir sağlayıcı fonksiyon (örn: veritabanı / redis / cache sorgusu).
    /// </summary>
    public JsonStringLocalizer(Func<string> jsonProvider)
    {
        _jsonProvider = jsonProvider;
    }

    public JsonStringLocalizer(IWebHostEnvironment env)
        : this(Path.Combine(env.ContentRootPath, "Localization", "localization.json"), isContent: false)
    {
    }

    public JsonStringLocalizer()
        : this("Localization/localization.json", isContent: false)
    {
    }

    /// <summary>
    /// Doğrudan JSON string üzerinden bir localizer örneği oluşturur (Veritabanı, Redis vb. için).
    /// </summary>
    public static JsonStringLocalizer FromJson(string jsonContent) => new(jsonContent, isContent: true);

    /// <summary>
    /// Dosya yolundan bir localizer örneği oluşturur.
    /// </summary>
    public static JsonStringLocalizer FromFile(string filePath) => new(filePath, isContent: false);

    /// <summary>
    /// Dinamik sağlayıcı fonksiyon ile localizer örneği oluşturur.
    /// </summary>
    public static JsonStringLocalizer FromProvider(Func<string> provider) => new(provider);

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
        var json = GetJsonData();
        if (string.IsNullOrWhiteSpace(json)) return new[] { "tr", "en" };
        try
        {
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

    /// <summary>
    /// Çalışma zamanında JSON verisini güncellemek (örn: veritabanı değiştiğinde cache tazelemek) için.
    /// </summary>
    public void Reload(string newJsonContent)
    {
        _rawJsonContent = newJsonContent;
        _cache.Clear();
    }

    public void ClearCache(string? cultureName = null)
    {
        if (cultureName != null)
        {
            var lang = NormalizeCulture(cultureName);
            _cache.TryRemove(lang, out _);
        }
        else
        {
            _cache.Clear();
        }
    }

    public static bool LooksLikeJson(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.TrimStart();
        return trimmed.StartsWith("{") || trimmed.StartsWith("[");
    }

    private string? GetJsonData()
    {
        // 1. Doğrudan verilmiş JSON içeriği (DB / Redis / String)
        if (!string.IsNullOrWhiteSpace(_rawJsonContent))
        {
            return _rawJsonContent;
        }

        // 2. Dinamik sağlayıcı fonksiyon
        if (_jsonProvider != null)
        {
            try
            {
                return _jsonProvider();
            }
            catch
            {
                return null;
            }
        }

        // 3. Fiziksel dosya yolu
        if (!string.IsNullOrWhiteSpace(_filePath) && File.Exists(_filePath))
        {
            try
            {
                return File.ReadAllText(_filePath);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private Dictionary<string, string> GetDictionaryForCulture(string cultureName)
    {
        var lang = NormalizeCulture(cultureName);

        if (_cache.TryGetValue(lang, out var cachedDict))
        {
            return cachedDict;
        }

        var json = GetJsonData();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty(lang, out var langElement) &&
                    langElement.ValueKind == JsonValueKind.Object)
                {
                    var dict = FlattenJson(langElement);
                    _cache[lang] = dict;
                    return dict;
                }
            }
            catch
            {
                // Parse hatası durumunda boş döner
            }
        }

        var emptyDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _cache[lang] = emptyDict;
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

    private static string ResolveFilePath(string localizationPath)
    {
        if (File.Exists(localizationPath)) return localizationPath;
        if (Directory.Exists(localizationPath)) return Path.Combine(localizationPath, "localization.json");

        var baseDir = Path.Combine(AppContext.BaseDirectory, "Localization", "localization.json");
        if (File.Exists(baseDir)) return baseDir;

        var currDir = Path.Combine(Directory.GetCurrentDirectory(), "Localization", "localization.json");
        if (File.Exists(currDir)) return currDir;

        return localizationPath;
    }
}
