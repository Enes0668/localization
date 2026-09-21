using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using LocalizationApi.Services;

namespace LocalizationApi.Middleware;

/// <summary>
/// ASP.NET Core boru hattında (Pipeline) üretilen HTTP JSON yanıtlarını yakalayan
/// ve yapılandırılmış alanları (DefaultFields) veya metinleri otomatik yerelleştiren ara yazılım.
/// </summary>
public class ResponseLocalizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IJsonStringLocalizer _localizer;
    private readonly string[] _defaultFields;

    public ResponseLocalizationMiddleware(
        RequestDelegate next,
        IJsonStringLocalizer localizer,
        IConfiguration configuration)
    {
        _next = next;
        _localizer = localizer;
        _defaultFields = configuration
            .GetSection("LocalizationConfig:DefaultFields")
            .Get<string[]>() ?? Array.Empty<string>();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Swagger ve statik dosya isteklerini pas geç
        if (context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.Value?.EndsWith(".html") == true ||
            context.Request.Path.Value?.EndsWith(".js") == true ||
            context.Request.Path.Value?.EndsWith(".css") == true)
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await _next(context);

            memoryStream.Seek(0, SeekOrigin.Begin);

            var contentType = context.Response.ContentType;
            var isJson = contentType != null && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);

            if (isJson && memoryStream.Length > 0)
            {
                using var reader = new StreamReader(memoryStream, Encoding.UTF8, leaveOpen: true);
                var rawJson = await reader.ReadToEndAsync();

                // Dil tespiti: Query string (?culture=) > Accept-Language header > Thread kültürü
                var cultureName = context.Request.Query["culture"].FirstOrDefault()
                                  ?? context.Request.Headers["Accept-Language"].FirstOrDefault()
                                  ?? CultureInfo.CurrentUICulture.Name;

                var translatedJson = TranslateJson(rawJson, cultureName);

                var bytes = Encoding.UTF8.GetBytes(translatedJson);
                context.Response.ContentLength = bytes.Length;
                await originalBodyStream.WriteAsync(bytes, 0, bytes.Length);
            }
            else
            {
                memoryStream.Seek(0, SeekOrigin.Begin);
                await memoryStream.CopyToAsync(originalBodyStream);
            }
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private string TranslateJson(string json, string cultureName)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return json;

            bool TryTranslateField(string fieldPath)
            {
                var parts   = fieldPath.Split('.');
                var current = node;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    current = current?[parts[i]];
                    if (current is null) break;
                }

                if (current is null) return false;

                var lastKey  = parts[^1];
                var rawValue = current[lastKey]?.GetValue<string>();
                if (rawValue is null) return false;

                var localized = _localizer.GetWithCulture(rawValue, cultureName);
                if (localized != rawValue)
                {
                    current[lastKey] = localized;
                    return true;
                }

                return false;
            }

            void TranslateRecursive(JsonNode? currentNode)
            {
                if (currentNode is JsonObject obj)
                {
                    foreach (var prop in obj.ToList())
                    {
                        if (prop.Value is JsonValue val && val.TryGetValue<string>(out var strVal))
                        {
                            var localized = _localizer.GetWithCulture(strVal, cultureName);
                            if (localized != strVal)
                            {
                                obj[prop.Key] = localized;
                            }
                        }
                        else
                        {
                            TranslateRecursive(prop.Value);
                        }
                    }
                }
                else if (currentNode is JsonArray arr)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        TranslateRecursive(arr[i]);
                    }
                }
            }

            // 1. Önce DefaultFields yollarına bak (ResponseValue.Message, Response.Texts.UserMessage vb.)
            var matchedAny = false;
            if (_defaultFields.Length > 0)
            {
                foreach (var field in _defaultFields)
                {
                    if (TryTranslateField(field))
                    {
                        matchedAny = true;
                    }
                }
            }

            // 2. DefaultFields yoksa veya eşleşmediyse rekürsif tarama yap
            if (!matchedAny)
            {
                TranslateRecursive(node);
            }

            return node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
        }
        catch
        {
            return json; // Parse hatası durumunda orijinal çıktıyı bozma
        }
    }
}
