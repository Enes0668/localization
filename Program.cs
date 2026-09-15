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
    new CultureInfo("en-US"),
    new CultureInfo("de-DE")
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
    Message = "Localization API v2 — JWT Korumalı",
    PublicEndpoints = new[]
    {
        "POST /auth/login  →  { username, password } → token al"
    },
    ProtectedEndpoints = new[]
    {
        "GET /auth/me  →  kim olduğunu gör (token gerekli)",
        "GET /api/localize/{key}?culture=tr-TR&p0=değer",
        "GET /api/keys?culture=en-US",
        "DELETE /api/cache"
    },
    HowToUse = "Önce /auth/login ile token al, sonra her istekte 'Authorization: Bearer {token}' header'ı ekle"
}));

// ──────────────────────────────────────────────────────────────
// AUTH ENDPOINTLERİ (herkese açık — token olmadan erişilir)
// ──────────────────────────────────────────────────────────────

// LOGIN → Token üretir
app.MapPost("/auth/login", (LoginRequest req) =>
{
    // Kullanıcıyı kontrol et
    if (!users.TryGetValue(req.Username, out var user) || user.Password != req.Password)
    {
        return Results.Unauthorized();
    }

    // JWT Token oluştur
    var claims = new[]
    {
        new Claim(ClaimTypes.Name,          req.Username),
        new Claim(ClaimTypes.Role,          user.Role),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) // Token benzersizliği
    };

    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer:             jwtIssuer,
        audience:           jwtAudience,
        claims:             claims,
        expires:            DateTime.UtcNow.AddMinutes(jwtExpiry),
        signingCredentials: creds
    );

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new
    {
        Token     = tokenString,
        ExpiresIn = $"{jwtExpiry} dakika",
        Username  = req.Username,
        Role      = user.Role
    });
});

// KİM OLDUĞUMU GÖR (token gerekli — test amaçlı)
app.MapGet("/auth/me", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        Username = user.Identity?.Name,
        Role     = user.FindFirst(ClaimTypes.Role)?.Value,
        IsAuthenticated = user.Identity?.IsAuthenticated
    });
}).RequireAuthorization();

// ──────────────────────────────────────────────────────────────
// LOCALIZATION ENDPOINTLERİ (token zorunlu)
// ──────────────────────────────────────────────────────────────

// GENERIC LOCALIZE
app.MapGet("/api/localize/{key}", (string key, HttpContext ctx, IJsonStringLocalizer localizer) =>
{
    var args = ctx.Request.Query
        .Where(q => q.Key.StartsWith("p", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(q.Key[1..], out _))
        .OrderBy(q => int.Parse(q.Key[1..]))
        .Select(q => (object)q.Value.ToString())
        .ToArray();

    var message = args.Length > 0 ? localizer[key, args] : localizer[key];

    return Results.Ok(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        Key     = key,
        Message = message
    });
}).RequireAuthorization();

// KEY LİSTESİ
app.MapGet("/api/keys", (IJsonStringLocalizer localizer) =>
{
    var keys = localizer.GetAllKeys().OrderBy(k => k).ToList();
    return Results.Ok(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        Count   = keys.Count,
        Keys    = keys
    });
}).RequireAuthorization();

// CACHE TEMİZLE (sadece Admin rolü)
app.MapDelete("/api/cache", (string? culture, ClaimsPrincipal user) =>
{
    if (user.FindFirst(ClaimTypes.Role)?.Value != "Admin")
        return Results.Forbid();

    JsonStringLocalizer.ClearCache(culture);
    return Results.Ok(new
    {
        Message = culture != null
            ? $"'{culture}' için cache temizlendi."
            : "Tüm cache temizlendi.",
        ClearedBy = user.Identity?.Name
    });
}).RequireAuthorization();

// ESKİ ENDPOINTler (token korumalı)
app.MapGet("/api/welcome", (IJsonStringLocalizer localizer) =>
    Results.Ok(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        Message = localizer["Auth.Welcome", "Kullanıcı"]
    })
).RequireAuthorization();

app.MapGet("/api/error-test", (string? code, IJsonStringLocalizer localizer) =>
{
    var errorCode = code ?? "400";
    return Results.BadRequest(new
    {
        Culture      = CultureInfo.CurrentUICulture.Name,
        ErrorCode    = errorCode,
        ErrorMessage = localizer["Errors.Notice", errorCode]
    });
}).RequireAuthorization();

app.MapGet("/api/order-status", (string? orderId, string? status, IJsonStringLocalizer localizer) =>
{
    var id            = orderId ?? "1001";
    var currentStatus = status ?? "Hazırlanıyor";
    return Results.Ok(new
    {
        Culture          = CultureInfo.CurrentUICulture.Name,
        OrderId          = id,
        Status           = currentStatus,
        FormattedMessage = localizer["Orders.Updated", id, currentStatus]
    });
}).RequireAuthorization();

app.MapGet("/api/cart", (int? count, decimal? total, IJsonStringLocalizer localizer) =>
    Results.Ok(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        Message = localizer["Cart.Summary", count ?? 1, total ?? 100]
    })
).RequireAuthorization();

app.Run();

// ──────────────────────────────────────────────────────────────
// MODEL
// ──────────────────────────────────────────────────────────────
record LoginRequest(string Username, string Password);
