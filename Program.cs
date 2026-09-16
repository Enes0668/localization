using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.IdentityModel.Tokens;
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

// JWT Ayarlarını appsettings.json'dan oku
var jwtKey      = builder.Configuration["Jwt:Key"]!;
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var jwtExpiry   = int.Parse(builder.Configuration["Jwt:ExpiryMinutes"]!);

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,   // Token süresi dolmuşsa reddet
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew                = TimeSpan.Zero // Süre toleransını sıfırla
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ──────────────────────────────────────────────────────────────
// 2. DEMO KULLANICILARI (Gerçek projede veritabanından gelir)
// Şifre düz metin — production'da mutlaka hash kullanılır!
// ──────────────────────────────────────────────────────────────
var users = new Dictionary<string, (string Password, string Role)>
{
    { "admin", ("simplex", "Admin") }
};

// ──────────────────────────────────────────────────────────────
// 3. MİDDLEWARE SIRASI — ÖNEMLİ!
// UseRequestLocalization → UseAuthentication → UseAuthorization
// ──────────────────────────────────────────────────────────────
var supportedCultures = new[]
{
    new CultureInfo("tr-TR"),
    new CultureInfo("en-US")
};

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("tr-TR"),
    SupportedCultures     = supportedCultures,
    SupportedUICultures   = supportedCultures
});

app.UseCors();
app.UseAuthentication(); // JWT token'ı oku ve doğrula
app.UseAuthorization();  // Yetkiyi kontrol et

// wwwroot/ klasöründeki statik dosyaları sun (index.html UI)
app.UseDefaultFiles();   // / → index.html yönlendirmesi
app.UseStaticFiles();    // wwwroot/ klasörünü sun

// ──────────────────────────────────────────────────────────────
// 4. ENDPOINTler
// ──────────────────────────────────────────────────────────────

// API Bilgilendirme (herkese açık)
app.MapGet("/api/info", () => Results.Ok(new
{
    Message = "Localization API v3 — Obje Çevirici",
    PublicEndpoints = new[]
    {
        "POST /auth/login  →  { username, password } → token al"
    },
    ProtectedEndpoints = new[]
    {
        "POST /api/translate  →  Objeyi al, belirtilen alanları çevir, objeyi geri ver",
        "GET  /api/localize/{key}?culture=tr-TR  →  Tek key çevir",
        "GET  /api/keys?culture=tr-TR  →  Tüm key'leri listele",
        "POST /api/keys  →  Yeni çeviri key'i ekle (Admin)",
        "DELETE /api/cache  →  Cache temizle (Admin)"
    },
    HowToUse = "1) /auth/login ile token al  2) Her istekte 'Authorization: Bearer {token}' ekle"
}));

// ──────────────────────────────────────────────────────────────
// AUTH ENDPOINTLERİ (herkese açık)
// ──────────────────────────────────────────────────────────────

app.MapPost("/auth/login", (LoginRequest req) =>
{
    if (!users.TryGetValue(req.Username, out var user) || user.Password != req.Password)
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, req.Username),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(jwtIssuer, jwtAudience, claims,
        expires: DateTime.UtcNow.AddMinutes(jwtExpiry), signingCredentials: creds);

    return Results.Ok(new
    {
        Token     = new JwtSecurityTokenHandler().WriteToken(token),
        ExpiresIn = $"{jwtExpiry} dakika",
        Username  = req.Username,
        Role      = user.Role
    });
});

app.MapGet("/auth/me", (ClaimsPrincipal user) => Results.Ok(new
{
    Username        = user.Identity?.Name,
    Role            = user.FindFirst(ClaimTypes.Role)?.Value,
    IsAuthenticated = user.Identity?.IsAuthenticated
})).RequireAuthorization();

// ──────────────────────────────────────────────────────────────
// ANA ENDPOINT: OBJE ÇEVİRİCİ (token zorunlu)
// POST /api/translate
// Body: { culture, fields: ["message","processInfo.message"], data: { ... } }
// ──────────────────────────────────────────────────────────────
app.MapPost("/api/translate", async (HttpContext ctx, IJsonStringLocalizer localizer) =>
{
    TranslateRequest? req;
    try { req = await ctx.Request.ReadFromJsonAsync<TranslateRequest>(); }
    catch { return Results.BadRequest(new { Error = "Geçersiz JSON gövdesi." }); }

    if (req is null || req.Data is null)
        return Results.BadRequest(new { Error = "'data' alanı zorunludur." });

    if (req.Fields is null || req.Fields.Length == 0)
        return Results.BadRequest(new { Error = "'fields' alanı zorunludur." });

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

    foreach (var fieldPath in req.Fields)
    {
        var parts   = fieldPath.Split('.');
        var current = node;

        // İç içe path'e git (son elemana kadar)
        for (int i = 0; i < parts.Length - 1; i++)
        {
            current = current?[parts[i]];
            if (current is null) break;
        }

        if (current is null) { notFound.Add(fieldPath); continue; }

        var lastKey     = parts[^1];
        var rawValue    = current[lastKey]?.GetValue<string>();
        if (rawValue is null) { notFound.Add(fieldPath); continue; }

        // LocalizationAPI'den çeviriyi al
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

    return Results.Ok(new
    {
        Culture    = cultureName,
        Data       = node,
        Translated = translated,
        NotFound   = notFound
    });
}).RequireAuthorization();

// ──────────────────────────────────────────────────────────────
// TEK KEY ÇEVİR (token zorunlu)
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
}).RequireAuthorization();

// KEY LİSTESİ (token zorunlu)
app.MapGet("/api/keys", (HttpContext ctx, IJsonStringLocalizer localizer) =>
{
    var culture = ctx.Request.Query["culture"].FirstOrDefault()
                  ?? CultureInfo.CurrentUICulture.Name;
    var keys = localizer.GetAllKeysForCulture(culture).OrderBy(k => k).ToList();
    return Results.Ok(new { Culture = culture, Count = keys.Count, Keys = keys });
}).RequireAuthorization();

// YENİ KEY EKLE (sadece Admin)
app.MapPost("/api/keys", async (HttpContext ctx, ClaimsPrincipal user, IJsonStringLocalizer localizer) =>
{
    if (user.FindFirst(ClaimTypes.Role)?.Value != "Admin")
        return Results.Forbid();

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

    return Results.Ok(new { Key = req.Key, Results = results, AddedBy = user.Identity?.Name });
}).RequireAuthorization();

// CACHE TEMİZLE (sadece Admin)
app.MapDelete("/api/cache", (string? culture, ClaimsPrincipal user) =>
{
    if (user.FindFirst(ClaimTypes.Role)?.Value != "Admin")
        return Results.Forbid();

    JsonStringLocalizer.ClearCache(culture);
    return Results.Ok(new
    {
        Message   = culture != null ? $"'{culture}' cache temizlendi." : "Tüm cache temizlendi.",
        ClearedBy = user.Identity?.Name
    });
}).RequireAuthorization();

app.Run();

// ──────────────────────────────────────────────────────────────
// MODELLER
// ──────────────────────────────────────────────────────────────
record LoginRequest(string Username, string Password);

record TranslateRequest(
    string?                          Culture,
    string[]                         Fields,
    System.Text.Json.JsonElement?    Data
);

record AddKeyRequest(
    string                      Key,
    Dictionary<string, string>  Translations
);
