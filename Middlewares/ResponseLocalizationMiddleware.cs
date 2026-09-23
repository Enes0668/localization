using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enes3.Services;

namespace Enes3.Middlewares
{
    public class ResponseLocalizationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IJsonStringLocalizer _localizer;
        private readonly HashSet<string> _targetPaths;

        public ResponseLocalizationMiddleware(
            RequestDelegate next,
            IJsonStringLocalizer localizer,
            string[] targetPaths)
        {
            _next = next;
            _localizer = localizer;
            _targetPaths = new HashSet<string>(targetPaths, StringComparer.OrdinalIgnoreCase);
        }

        public ResponseLocalizationMiddleware(
            RequestDelegate next,
            string jsonFilePath,
            string[] targetPaths)
            : this(next, new JsonStringLocalizer(jsonFilePath), targetPaths)
        {
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
                // 1. Controller çalışır ve ham çıktıyı MemoryStream'e yazar:
                await _next(context);

                // 2. RAM'deki çıktıyı oku:
                memoryStream.Seek(0, SeekOrigin.Begin);
                var responseBody = await new StreamReader(memoryStream, Encoding.UTF8).ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(responseBody) || context.Response.StatusCode == StatusCodes.Status204NoContent)
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    await memoryStream.CopyToAsync(originalBodyStream);
                    return;
                }

                // 3. İstemcinin istediği dili belirle:
                var culture = context.Request.Query["culture"].FirstOrDefault()
                              ?? context.Request.Headers["Accept-Language"].FirstOrDefault()
                              ?? CultureInfo.CurrentUICulture.Name;

                // 4. Doğrudan yerel JSON sözlüğü üzerinden bellekte (In-Memory) çevir:
                var modifiedContent = ProcessResponse(responseBody, _targetPaths, _localizer, culture);
                var modifiedBytes = Encoding.UTF8.GetBytes(modifiedContent);

                context.Response.Body = originalBodyStream;
                if (!context.Response.HasStarted)
                {
                    context.Response.Headers.ContentLength = modifiedBytes.Length;
                }

                // 5. Çevrilmiş cevabı istemciye gönder:
                await originalBodyStream.WriteAsync(modifiedBytes, 0, modifiedBytes.Length);
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }

        private static string ProcessResponse(
            string rawBody,
            HashSet<string> targetPaths,
            IJsonStringLocalizer localizer,
            string culture)
        {
            try
            {
                var jsonNode = JsonNode.Parse(rawBody);
                if (jsonNode != null)
                {
                    // Kök eleman tek bir string ise (Örn: "Olmadı.")
                    if (jsonNode is JsonValue jsonVal && jsonVal.TryGetValue<string>(out var stringVal))
                    {
                        if (targetPaths.Contains(""))
                        {
                            var localizedRoot = localizer.GetWithCulture(stringVal, culture);
                            return JsonSerializer.Serialize(localizedRoot);
                        }
                        return rawBody;
                    }

                    // JSON nesnesi ise hedef path'leri tara ve sadece listedekileri çevir
                    foreach (var path in targetPaths)
                    {
                        if (string.IsNullOrWhiteSpace(path)) continue;

                        var segments = path.Split('.');
                        TraverseAndLocalize(jsonNode, segments, 0, localizer, culture);
                    }

                    return jsonNode.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });
                }
            }
            catch (JsonException)
            {
                // Düz metin (raw string) ise ve hedef path'lerde "" varsa
                if (targetPaths.Contains(""))
                {
                    return localizer.GetWithCulture(rawBody, culture);
                }
            }

            return rawBody;
        }

        private static void TraverseAndLocalize(
            JsonNode? currentNode,
            string[] segments,
            int segmentIndex,
            IJsonStringLocalizer localizer,
            string culture)
        {
            if (currentNode == null || segmentIndex >= segments.Length) return;

            var currentSegment = segments[segmentIndex];
            var isLast = segmentIndex == segments.Length - 1;

            if (currentNode is JsonObject obj)
            {
                var matchedKey = obj.Select(kv => kv.Key)
                                    .FirstOrDefault(k => k.Equals(currentSegment, StringComparison.OrdinalIgnoreCase));

                if (matchedKey == null) return;

                if (isLast)
                {
                    var targetNode = obj[matchedKey];
                    if (targetNode is JsonValue value && value.TryGetValue<string>(out var originalStr))
                    {
                        var localized = localizer.GetWithCulture(originalStr, culture);
                        obj[matchedKey] = JsonValue.Create(localized);
                    }
                }
                else
                {
                    TraverseAndLocalize(obj[matchedKey], segments, segmentIndex + 1, localizer, culture);
                }
            }
            else if (currentNode is JsonArray array)
            {
                foreach (var item in array)
                {
                    TraverseAndLocalize(item, segments, segmentIndex, localizer, culture);
                }
            }
        }
    }

    public static class ResponseLocalizationMiddlewareExtensions
    {
        public static IApplicationBuilder UseResponseLocalization(
            this IApplicationBuilder app,
            IJsonStringLocalizer localizer,
            string[] targetPaths)
        {
            return app.UseMiddleware<ResponseLocalizationMiddleware>(localizer, targetPaths);
        }

        public static IApplicationBuilder UseResponseLocalization(
            this IApplicationBuilder app,
            string jsonFilePath,
            string[] targetPaths)
        {
            return app.UseMiddleware<ResponseLocalizationMiddleware>(jsonFilePath, targetPaths);
        }
    }
}
