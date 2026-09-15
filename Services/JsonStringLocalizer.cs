using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace LocalizationApi.Services;

public class JsonStringLocalizer : IJsonStringLocalizer
{
    private readonly IWebHostEnvironment _env;
    private readonly string _localizationPath;
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Cache = new();

    public JsonStringLocalizer(IWebHostEnvironment env)
    {
        _env = env;
        // Furkan Bey'in istediği gibi bağımsız "Localization" klasöründen okuma yapılır
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

    private Dictionary<string, string> GetDictionaryForCulture(string cultureName)
    {
        if (Cache.TryGetValue(cultureName, out var cachedDict))
        {
            return cachedDict;
        }

        var filePath = Path.Combine(_localizationPath, $"{cultureName}.json");

        // Tam eşleşme yoksa (örneğin "tr" gelip dosya "tr-TR.json" ise veya tam tersi)
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
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new Dictionary<string, string>();

                Cache[cultureName] = dict;
                return dict;
            }
            catch
            {
                // JSON okuma hatası durumunda boş sözlük dön
            }
        }

        var emptyDict = new Dictionary<string, string>();
        Cache[cultureName] = emptyDict;
        return emptyDict;
    }
}
