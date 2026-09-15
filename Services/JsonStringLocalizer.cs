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

    private Dictionary<string, string> GetDictionaryForCulture(string cultureName)
    {
        if (Cache.TryGetValue(cultureName, out var cachedDict))
        {
            return cachedDict;
        }

        var filePath = Path.Combine(_localizationPath, $"{cultureName}.json");

        // Tam eşleşme yoksa dil kodu prefix'iyle eşleşen dosyayı bul (örn. "de" → "de-DE.json")
        if (!File.Exists(filePath))
        {
            var fallbackFiles = Directory.GetFiles(_localizationPath, "*.json");
            var matchedFile = fallbackFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).StartsWith(cultureName.Split('-')[0], StringComparison.OrdinalIgnoreCase));

            if (matchedFile != null)
            {
                filePath = matchedFile;
            }
            else
            {
                // Hiçbiri bulunamazsa varsayılan dil dosyası (tr-TR.json)
                filePath = Path.Combine(_localizationPath, "tr-TR.json");
            }
        }

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                using var document = JsonDocument.Parse(json);

                // İç içe (nested) JSON yapısını "Kategori.Anahtar" → "Değer" şeklinde düzleştir
                var dict = FlattenJson(document.RootElement);

                Cache[cultureName] = dict;
                return dict;
            }
            catch
            {
                // JSON okuma veya parse hatası durumunda boş sözlük dön
            }
        }

        var emptyDict = new Dictionary<string, string>();
        Cache[cultureName] = emptyDict;
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
            Cache.TryRemove(cultureName, out _);
        }
        else
        {
            Cache.Clear();
        }
    }
}
