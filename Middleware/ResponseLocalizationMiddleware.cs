using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalizationApi.Middleware;

/// <summary>
/// HTTP JSON Yanıtlarını araya girip yakalayan ve belirtilen alanları (fields) 
/// verilen dillere (languagesJson) ve istenen dil anahtarına (languageKey: örn. "lang") göre otomatik çeviren Middleware.
/// </summary>
public class ResponseLocalizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _languageKey;
    private readonly string[] _fields;

    // RAM'de saklanan çeviriler: "tr" -> { "Payment was successful.": "Ödeme başarıyla gerçekleştirildi." }
    private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);

    public ResponseLocalizationMiddleware(
        RequestDelegate next,
        string languagesJson,
        string languageKey,
        string[] fields)
    {
        _next = next;
        _languageKey = string.IsNullOrWhiteSpace(languageKey) ? "lang" : languageKey;
        _fields = fields ?? Array.Empty<string>();

        // 1. Parametreden gelen diller JSON metnini bir kez okuyup RAM'e alıyoruz
        LoadTranslations(languagesJson);
    }

    public ResponseLocalizationMiddleware(
        RequestDelegate next,
        string languagesJson,
        string[] fields)
        : this(next, languagesJson, "lang", fields)
    {
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Swagger ve statik dosya isteklerini pas geç
        if (IsStaticOrSwaggerRequest(context))
        {
            await _next(context);
            return;
        }

        // Response akışını geçici olarak MemoryStream'e yönlendiriyoruz
        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            // Controller çalışsın ve yanıtını bizim MemoryStream'e yazsın
            await _next(context);

            memoryStream.Seek(0, SeekOrigin.Begin);

            bool isJson = context.Response.ContentType != null &&
                          context.Response.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);

            if (isJson && memoryStream.Length > 0)
            {
                using var reader = new StreamReader(memoryStream, Encoding.UTF8, leaveOpen: true);
                string rawJson = await reader.ReadToEndAsync();

                // İstekten hedef dili al (_languageKey üzerinden örn: ?lang=tr veya lang: tr)
                string cultureName = GetRequestedCulture(context);

                // JSON'daki belirtilen alanları çevir
                string translatedJson = TranslateJson(rawJson, cultureName);

                byte[] translatedBytes = Encoding.UTF8.GetBytes(translatedJson);
                context.Response.ContentLength = translatedBytes.Length;
                await originalBodyStream.WriteAsync(translatedBytes, 0, translatedBytes.Length);
            }
            else
            {
                memoryStream.Seek(0, SeekOrigin.Begin);
                await memoryStream.CopyToAsync(originalBodyStream);
            }
        }
        finally
        {
            // Orijinal akışı geri koyuyoruz
            context.Response.Body = originalBodyStream;
        }
    }

    /// <summary>
    /// 1. Parametre olarak gelen JSON metnini ayrıştırır ve RAM'e sözlük olarak kaydeder.
    /// </summary>
    private void LoadTranslations(string languagesJson)
    {
        if (string.IsNullOrWhiteSpace(languagesJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(languagesJson);

            foreach (var langProp in doc.RootElement.EnumerateObject())
            {
                if (langProp.Value.ValueKind == JsonValueKind.Object)
                {
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    FlattenJson(langProp.Value, dict);

                    string rawKey = langProp.Name.Trim().ToLowerInvariant();
                    _translations[rawKey] = dict;

                    // Alt çizgi ile tanımlandıysa tireli halini de ekle (örn: tr_tr -> tr-tr)
                    string hyphenKey = rawKey.Replace('_', '-');
                    if (!_translations.ContainsKey(hyphenKey))
                    {
                        _translations[hyphenKey] = dict;
                    }

                    // Kök dili de ekle (örn: "tr-tr" ise "tr" olarak da fallback ekle)
                    string rootKey = rawKey.Split('-', '_')[0];
                    if (!_translations.ContainsKey(rootKey))
                    {
                        _translations[rootKey] = dict;
                    }
                }
            }
        }
        catch
        {
            // JSON formatı geçersizse boş bırak
        }
    }

    /// <summary>
    /// İç içe JSON nesnelerini nokta notasyonuyla düzleştirir.
    /// </summary>
    private static void FlattenJson(JsonElement element, Dictionary<string, string> dict, string prefix = "")
    {
        foreach (var property in element.EnumerateObject())
        {
            string currentKey = string.IsNullOrEmpty(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenJson(property.Value, dict, currentKey);
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                dict[currentKey] = property.Value.GetString() ?? currentKey;
            }
        }
    }

    /// <summary>
    /// JSON içindeki belirtilen alanları (fields) bulup istenen dile çevirir.
    /// </summary>
    private string TranslateJson(string json, string cultureName)
    {
        var dictionary = FindDictionary(cultureName);

        // İlgili dil için sözlüğümüz yoksa orijinal JSON'ı dön
        if (dictionary == null)
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node == null) return json;

            bool matchedAnyField = false;

            // Parametrede verilen alanları (fields) ara ve çevir
            if (_fields.Length > 0)
            {
                foreach (string fieldPath in _fields)
                {
                    if (TryTranslateSpecificField(node, fieldPath, dictionary))
                    {
                        matchedAnyField = true;
                    }
                }
            }

            // Eğer özel bir field eşleşmediyse veya field verilmediyse JSON içindeki metinleri tara
            if (!matchedAnyField)
            {
                TranslateAllStringsRecursive(node, dictionary);
            }

            return node.ToJsonString(new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
        }
        catch
        {
            return json;
        }
    }

    private static bool TryTranslateSpecificField(JsonNode rootNode, string fieldPath, Dictionary<string, string> dictionary)
    {
        string[] parts = fieldPath.Split('.');
        JsonNode? currentNode = rootNode;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            currentNode = currentNode?[parts[i]];
            if (currentNode == null) return false;
        }

        if (currentNode == null) return false;

        string lastKey = parts[parts.Length - 1];
        string? rawValue = currentNode[lastKey]?.GetValue<string>();

        if (string.IsNullOrEmpty(rawValue)) return false;

        if (dictionary.TryGetValue(rawValue, out string? localizedValue))
        {
            currentNode[lastKey] = localizedValue;
            return true;
        }

        return false;
    }

    private static void TranslateAllStringsRecursive(JsonNode? currentNode, Dictionary<string, string> dictionary)
    {
        if (currentNode is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (property.Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? textValue))
                {
                    if (!string.IsNullOrEmpty(textValue) && dictionary.TryGetValue(textValue, out string? localized))
                    {
                        jsonObject[property.Key] = localized;
                    }
                }
                else
                {
                    TranslateAllStringsRecursive(property.Value, dictionary);
                }
            }
        }
        else if (currentNode is JsonArray jsonArray)
        {
            for (int i = 0; i < jsonArray.Count; i++)
            {
                TranslateAllStringsRecursive(jsonArray[i], dictionary);
            }
        }
    }

    /// <summary>
    /// İstekten hedef dili okur.
    /// Öncelik: Belirtilen languageKey (örn: ?lang=tr veya Header: lang: tr) -> ?culture= -> Accept-Language -> Sistem dili
    /// </summary>
    private string GetRequestedCulture(HttpContext context)
    {
        // 1. Kullanıcının belirlediği özel anahtar (Örn: ?lang=tr)
        string? queryLang = context.Request.Query[_languageKey].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryLang))
            return queryLang;

        // 2. Kullanıcının belirlediği özel Header (Örn: lang: tr)
        string? headerLang = context.Request.Headers[_languageKey].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerLang))
            return headerLang;

        // 3. Standart ?culture= query parametresi
        string? queryCulture = context.Request.Query["culture"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryCulture))
            return queryCulture;

        // 4. Standart Accept-Language başlığı
        string? headerCulture = context.Request.Headers["Accept-Language"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerCulture))
            return headerCulture;

        return CultureInfo.CurrentUICulture.Name;
    }

    /// <summary>
    /// İstenen dil için en uygun sözlüğü bulur:
    /// 1. Tam birebir eşleşme (örn: "tr-tr", "en-us", "turkish", "1")
    /// 2. Alt çizgi / tire uyarlaması (örn: "tr_TR" -> "tr-tr")
    /// 3. Kök dil fallback (örn: "tr-TR" -> "tr", "en-GB" -> "en")
    /// </summary>
    private Dictionary<string, string>? FindDictionary(string? requestedCulture)
    {
        if (string.IsNullOrWhiteSpace(requestedCulture)) return null;

        string normalized = requestedCulture.Trim().ToLowerInvariant();

        // 1. Adım: Birebir tam eşleşme
        if (_translations.TryGetValue(normalized, out var dict))
            return dict;

        // 2. Adım: Alt çizgi / tire uyarlaması
        string hyphen = normalized.Replace('_', '-');
        if (_translations.TryGetValue(hyphen, out dict))
            return dict;

        // 3. Adım: Kök dile dönüş (Fallback)
        string root = normalized.Split('-', '_')[0];
        if (_translations.TryGetValue(root, out dict))
            return dict;

        return null;
    }

    private static bool IsStaticOrSwaggerRequest(HttpContext context)
    {
        string? path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path)) return false;

        return path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
    }
}
