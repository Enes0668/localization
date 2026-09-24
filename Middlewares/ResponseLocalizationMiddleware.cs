using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enes3.Services;

namespace Enes3.Middlewares
{
    /// <summary>
    /// ResponseLocalization middleware'i için yapılandırma seçenekleri.
    /// </summary>
    public class ResponseLocalizationOptions
    {
        /// <summary>
        /// Çevrilmesi hedeflenen alan adları (örn: "", "Message", "Log.LogMessage").
        /// </summary>
        public string[] TargetPaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// İstek başlığında (Header) dil bilgisinin aranacağı anahtar. 
        /// Varsayılan: "Accept-Language". Özel başlıklar için örn: "lang", "selectedLanguage", "X-Language".
        /// </summary>
        public string HeaderName { get; set; } = "Accept-Language";

        /// <summary>
        /// URL query string üzerinden dil parametresi aranacak anahtar (örn: ?culture=en veya ?lang=en).
        /// Varsayılan: "culture".
        /// </summary>
        public string QueryParamName { get; set; } = "culture";

        /// <summary>
        /// İstekte herhangi bir dil başlığı bulunamazsa kullanılacak varsayılan dil kodu.
        /// Varsayılan: "tr".
        /// </summary>
        public string DefaultCulture { get; set; } = "tr";

        /// <summary>
        /// Doğrudan JSON metni (Veritabanından, Redis'ten veya harici servisten çekilen JSON string).
        /// Eğer bu değer belirtilirse fiziksel dosya aranmaz, doğrudan bu veri kullanılır.
        /// </summary>
        public string? JsonContent { get; set; }

        /// <summary>
        /// JSON verisini dinamik olarak getiren sağlayıcı fonksiyon (örn: veritabanı veya önbellek sorgusu).
        /// </summary>
        public Func<string>? JsonContentProvider { get; set; }

        /// <summary>
        /// localization.json dosyasının fiziksel konumu (Fiziksel dosya kullanmak isteyenler için).
        /// Varsayılan: "Localization/localization.json".
        /// </summary>
        public string? JsonFilePath { get; set; } = "Localization/localization.json";

        public ResponseLocalizationOptions() { }

        public ResponseLocalizationOptions(string[] targetPaths)
        {
            TargetPaths = targetPaths;
        }
    }

    public class ResponseLocalizationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IJsonStringLocalizer _localizer;
        private readonly ResponseLocalizationOptions _options;
        private readonly HashSet<string> _targetPaths;

        public ResponseLocalizationMiddleware(
            RequestDelegate next,
            IJsonStringLocalizer localizer,
            ResponseLocalizationOptions options)
        {
            _next = next;
            _localizer = localizer;
            _options = options;
            _targetPaths = new HashSet<string>(options.TargetPaths, StringComparer.OrdinalIgnoreCase);
        }

        public ResponseLocalizationMiddleware(
            RequestDelegate next,
            IJsonStringLocalizer localizer,
            string[] targetPaths)
            : this(next, localizer, new ResponseLocalizationOptions(targetPaths))
        {
        }

        public ResponseLocalizationMiddleware(
            RequestDelegate next,
            string jsonContentOrPath,
            string[] targetPaths)
            : this(next, new JsonStringLocalizer(jsonContentOrPath), new ResponseLocalizationOptions(targetPaths)
            {
                JsonContent = JsonStringLocalizer.LooksLikeJson(jsonContentOrPath) ? jsonContentOrPath : null,
                JsonFilePath = JsonStringLocalizer.LooksLikeJson(jsonContentOrPath) ? null : jsonContentOrPath
            })
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

                // 3. İstemcinin istediği dili parametrik ayarlara göre belirle:
                var culture = GetCultureFromRequest(context, _options);

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

        private static string GetCultureFromRequest(HttpContext context, ResponseLocalizationOptions options)
        {
            // 1. Query string kontrolü (?culture=en veya ?lang=en)
            if (!string.IsNullOrWhiteSpace(options.QueryParamName) &&
                context.Request.Query.TryGetValue(options.QueryParamName, out var queryLang) &&
                !string.IsNullOrWhiteSpace(queryLang))
            {
                return queryLang.ToString();
            }

            // 2. Kullanıcının config'de belirttiği Header'ı kontrol et (örn: "selectedLanguage", "lang", "Accept-Language")
            if (!string.IsNullOrWhiteSpace(options.HeaderName) &&
                context.Request.Headers.TryGetValue(options.HeaderName, out var headerLang) &&
                !string.IsNullOrWhiteSpace(headerLang))
            {
                return headerLang.ToString();
            }

            // 3. Eğer özel bir header belirtilmiş ama istekte yoksa, standart Accept-Language'e fallback yap
            if (!string.Equals(options.HeaderName, "Accept-Language", StringComparison.OrdinalIgnoreCase) &&
                context.Request.Headers.TryGetValue("Accept-Language", out var standardLang) &&
                !string.IsNullOrWhiteSpace(standardLang))
            {
                return standardLang.ToString();
            }

            // 4. Hiçbir dil bulunamazsa varsayılan dile dön
            return options.DefaultCulture ?? CultureInfo.CurrentUICulture.Name;
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
        /// <summary>
        /// ResponseLocalization servislerini Dependency Injection konteynerine ekler.
        /// </summary>
        public static IServiceCollection AddResponseLocalization(
            this IServiceCollection services,
            Action<ResponseLocalizationOptions>? configureOptions = null)
        {
            var options = new ResponseLocalizationOptions();
            configureOptions?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<IJsonStringLocalizer>(sp =>
            {
                var opt = sp.GetService<ResponseLocalizationOptions>() ?? options;
                return new JsonStringLocalizer(opt);
            });

            return services;
        }

        public static IApplicationBuilder UseResponseLocalization(
            this IApplicationBuilder app,
            Action<ResponseLocalizationOptions> configureOptions)
        {
            var options = new ResponseLocalizationOptions();
            configureOptions(options);

            IJsonStringLocalizer localizer;
            if (!string.IsNullOrWhiteSpace(options.JsonContent) || options.JsonContentProvider != null)
            {
                localizer = new JsonStringLocalizer(options);
            }
            else
            {
                localizer = app.ApplicationServices.GetService<IJsonStringLocalizer>()
                            ?? new JsonStringLocalizer(options);
            }

            return app.UseMiddleware<ResponseLocalizationMiddleware>(localizer, options);
        }

        public static IApplicationBuilder UseResponseLocalization(
            this IApplicationBuilder app,
            ResponseLocalizationOptions options)
        {
            IJsonStringLocalizer localizer;
            if (!string.IsNullOrWhiteSpace(options.JsonContent) || options.JsonContentProvider != null)
            {
                localizer = new JsonStringLocalizer(options);
            }
            else
            {
                localizer = app.ApplicationServices.GetService<IJsonStringLocalizer>()
                            ?? new JsonStringLocalizer(options);
            }

            return app.UseMiddleware<ResponseLocalizationMiddleware>(localizer, options);
        }

        public static IApplicationBuilder UseResponseLocalization(
            this IApplicationBuilder app,
            IJsonStringLocalizer localizer,
            string[] targetPaths)
        {
            return app.UseMiddleware<ResponseLocalizationMiddleware>(localizer, targetPaths);
        }

        public static IApplicationBuilder UseResponseLocalization(
            this IApplicationBuilder app,
            string jsonContentOrPath,
            string[] targetPaths)
        {
            return app.UseMiddleware<ResponseLocalizationMiddleware>(jsonContentOrPath, targetPaths);
        }
    }
}
