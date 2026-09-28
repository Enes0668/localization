using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalizationApi.Middleware;

/// <summary>
/// HTTP JSON Yanıtlarını araya girip yakalayan ve belirtilen alanları (fields) 
/// verilen dillere (languagesJson) göre otomatik çeviren Middleware.
/// 
/// 1. PARAMETRE: TÜM DİLLER (TR, EN) -> JSON metni
/// 2. PARAMETRE: FİELD -> Çevrilecek alanlar listesi (Örn: "ResponseValue.Message")
/// </summary>
public class ResponseLocalizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string[] _fields;

    // RAM'de saklanan çeviriler: "tr" -> { "Payment was successful.": "Ödeme başarıyla gerçekleştirildi." }
    private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);

    public ResponseLocalizationMiddleware(
        RequestDelegate next,
        string languagesJson,
        string[] fields)
    {
        _next = next;
        _fields = fields ?? Array.Empty<string>();

        // 1. Parametreden gelen diller JSON metnini bir kez okuyup RAM'e alıyoruz
        LoadTranslations(languagesJson);
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

                // İstekten hedef dili al (Örn: ?culture=tr)
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
                string language = NormalizeCulture(langProp.Name);

                if (langProp.Value.ValueKind == JsonValueKind.Object)
                {
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    FlattenJson(langProp.Value, dict);
                    _translations[language] = dict;
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
        string language = NormalizeCulture(cultureName);

        // İlgili dil için sözlüğümüz yoksa orijinal JSON'ı dön
        if (!_translations.TryGetValue(language, out var dictionary))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node == null) return json;

            bool matchedAnyField = false;

            // 2. Parametrede verilen alanları (fields) ara ve çevir
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

    private static string GetRequestedCulture(HttpContext context)
    {
        string? queryCulture = context.Request.Query["culture"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryCulture))
            return queryCulture;

        string? headerCulture = context.Request.Headers["Accept-Language"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerCulture))
            return headerCulture;

        return CultureInfo.CurrentUICulture.Name;
    }

    private static string NormalizeCulture(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return "tr";
        return cultureName.Split('-')[0].ToLowerInvariant();
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
