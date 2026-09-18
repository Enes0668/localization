using System.Globalization;
using Microsoft.AspNetCore.Localization;
using LocalizationApi.Services;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────────────────────
// 1. SERVİSLER
// ──────────────────────────────────────────────────────────────

// JSON Localizer
builder.Services.AddSingleton<IJsonStringLocalizer, JsonStringLocalizer>();

// CORS — başka domainlerden gelen isteklere izin ver
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// ──────────────────────────────────────────────────────────────
// 2. MİDDLEWARE
// ──────────────────────────────────────────────────────────────
var defaultCulture   = builder.Configuration["LocalizationConfig:DefaultCulture"] ?? "tr-TR";
var culturesFromConfig = builder.Configuration
    .GetSection("LocalizationConfig:SupportedCultures")
    .Get<string[]>() ?? new[] { "tr-TR", "en-US" };

var supportedCultures = culturesFromConfig.Select(c => new CultureInfo(c)).ToArray();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(defaultCulture),
    SupportedCultures     = supportedCultures,
    SupportedUICultures   = supportedCultures
});

app.UseCors();

// ──────────────────────────────────────────────────────────────
// 3. ENDPOINTLER
// ──────────────────────────────────────────────────────────────

// API Bilgilendirme (Kök dizin ve /api/info)
app.MapGet("/", () => Results.Ok(new
{
    Service = "Localization API — Response Transformer",
    Status = "Running",
    SupportedCultures = new[] { "tr-TR (tr)", "en-US (en)" },
    Endpoints = new
    {
        Translate = "POST /api/translate  →  Objeyi al, çevir, geri ver",
        LocalizeKey = "GET  /api/localize/{key}?culture=tr-TR  →  Tek anahtar çevir",
        Keys = "GET  /api/keys?culture=tr-TR  →  Tüm anahtarları listele",
        AddKey = "POST /api/keys  →  Yeni anahtar ekle",
        ClearCache = "DELETE /api/cache  →  Önbelleği temizle"
    }
}));

app.MapGet("/api/info", () => Results.Redirect("/"));

// ──────────────────────────────────────────────────────────────
// ANA ENDPOINT: OBJE ÇEVİRİCİ (herkese açık)
// POST /api/translate
// Body: { culture, data: { ... } }
// ──────────────────────────────────────────────────────────────
app.MapPost("/api/translate", async (HttpContext ctx, IJsonStringLocalizer localizer) =>
{
    TranslateRequest? req;
    try { req = await ctx.Request.ReadFromJsonAsync<TranslateRequest>(); }
    catch { return Results.BadRequest(new { Error = "Geçersiz JSON gövdesi." }); }

    if (req is null || req.Data is null)
        return Results.BadRequest(new { Error = "'data' alanı zorunludur." });

    // Kültür: query string > body > varsayılan
    var cultureName = ctx.Request.Query["culture"].FirstOrDefault()
                      ?? req.Culture
                      ?? CultureInfo.CurrentUICulture.Name;

    // JsonNode üzerinde çalış (mutable)
    var node = System.Text.Json.Nodes.JsonNode.Parse(req.Data.Value.GetRawText());
    if (node is null)
        return Results.BadRequest(new { Error = "Geçersiz 'data' değeri." });

    var translated = new List<object>();
    var notFound   = new List<string>();

    if (req.Fields is null || req.Fields.Length == 0)
    {
        // fields belirtilmemişse tüm JSON içindeki string değerleri otomatik çevir
        void TranslateRecursive(System.Text.Json.Nodes.JsonNode? currentNode, string currentPath = "")
        {
            if (currentNode is System.Text.Json.Nodes.JsonObject obj)
            {
                foreach (var prop in obj.ToList())
                {
                    var childPath = string.IsNullOrEmpty(currentPath) ? prop.Key : $"{currentPath}.{prop.Key}";
                    if (prop.Value is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var strVal))
                    {
                        var localized = localizer.GetWithCulture(strVal, cultureName);
                        if (localized != strVal)
                        {
                            obj[prop.Key] = localized;
                            translated.Add(new { Field = childPath, From = strVal, To = localized });
                        }
                    }
                    else
                    {
                        TranslateRecursive(prop.Value, childPath);
                    }
                }
            }
            else if (currentNode is System.Text.Json.Nodes.JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    TranslateRecursive(arr[i], $"{currentPath}[{i}]");
                }
            }
        }

        TranslateRecursive(node);
    }
    else
    {
        foreach (var fieldPath in req.Fields)
        {
            var parts   = fieldPath.Split('.');
            var current = node;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                current = current?[parts[i]];
                if (current is null) break;
            }

            if (current is null) { notFound.Add(fieldPath); continue; }

            var lastKey     = parts[^1];
            var rawValue    = current[lastKey]?.GetValue<string>();
            if (rawValue is null) { notFound.Add(fieldPath); continue; }

            var localized = localizer.GetWithCulture(rawValue, cultureName);

            if (localized != rawValue)
            {
                current[lastKey] = localized;
                translated.Add(new { Field = fieldPath, From = rawValue, To = localized });
            }
            else
            {
                notFound.Add(fieldPath + $" ('{rawValue}' key bulunamadı)");
            }
        }
    }

    return Results.Ok(new
    {
        Culture    = cultureName,
        Data       = node,
        Translated = translated,
        NotFound   = notFound
    });
});

// ──────────────────────────────────────────────────────────────
// TEK KEY ÇEVİR (herkese açık)
// GET /api/localize/{key}?culture=tr-TR
// ──────────────────────────────────────────────────────────────
app.MapGet("/api/localize/{key}", (string key, HttpContext ctx, IJsonStringLocalizer localizer) =>
{
    var culture = ctx.Request.Query["culture"].FirstOrDefault()
                  ?? CultureInfo.CurrentUICulture.Name;
    var result  = localizer.GetWithCulture(key, culture);
    return Results.Ok(new
    {
        Culture    = culture,
        Key        = key,
        Value      = result,
        IsTranslated = result != key
    });
});

// KEY LİSTESİ (herkese açık)
app.MapGet("/api/keys", (HttpContext ctx, IJsonStringLocalizer localizer) =>
{
    var culture = ctx.Request.Query["culture"].FirstOrDefault()
                  ?? CultureInfo.CurrentUICulture.Name;
    var keys = localizer.GetAllKeysForCulture(culture).OrderBy(k => k).ToList();
    return Results.Ok(new { Culture = culture, Count = keys.Count, Keys = keys });
});

// YENİ KEY EKLE (herkese açık)
app.MapPost("/api/keys", async (HttpContext ctx, IJsonStringLocalizer localizer) =>
{
    AddKeyRequest? req;
    try { req = await ctx.Request.ReadFromJsonAsync<AddKeyRequest>(); }
    catch { return Results.BadRequest(new { Error = "Geçersiz JSON gövdesi." }); }

    if (req is null || string.IsNullOrWhiteSpace(req.Key))
        return Results.BadRequest(new { Error = "'key' alanı zorunludur." });

    if (req.Translations is null || req.Translations.Count == 0)
        return Results.BadRequest(new { Error = "'translations' alanı zorunludur." });

    var filePath = Path.Combine("Localization", "localization.json");
    if (!File.Exists(filePath))
        return Results.Problem("Localization dosyası bulunamadı.");

    var json = await File.ReadAllTextAsync(filePath);
    var rootNode = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject()
                   ?? new System.Text.Json.Nodes.JsonObject();

    var results = new List<object>();
    foreach (var (culture, value) in req.Translations)
    {
        var lang = JsonStringLocalizer.NormalizeCulture(culture);
        if (!rootNode.ContainsKey(lang))
        {
            rootNode[lang] = new System.Text.Json.Nodes.JsonObject();
        }

        var langObj = rootNode[lang]?.AsObject();
        if (langObj is not null)
        {
            langObj[req.Key] = value;
            JsonStringLocalizer.ClearCache(lang);
            results.Add(new { Culture = culture, Language = lang, Success = true });
        }
        else
        {
            results.Add(new { Culture = culture, Language = lang, Success = false, Error = "Dil bloğu geçersiz." });
        }
    }

    var updatedJson = rootNode.ToJsonString(new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
    await File.WriteAllTextAsync(filePath, updatedJson);

    return Results.Ok(new { Key = req.Key, Results = results });
});

// CACHE TEMİZLE (herkese açık)
app.MapDelete("/api/cache", (string? culture) =>
{
    JsonStringLocalizer.ClearCache(culture);
    return Results.Ok(new
    {
        Message = culture != null ? $"'{culture}' cache temizlendi." : "Tüm cache temizlendi."
    });
});

app.Run();

// ──────────────────────────────────────────────────────────────
// MODELLER
// ──────────────────────────────────────────────────────────────
record TranslateRequest(
    string?                          Culture,
    string[]?                        Fields,
    System.Text.Json.JsonElement?    Data
);

record AddKeyRequest(
    string                      Key,
    Dictionary<string, string>  Translations
);
