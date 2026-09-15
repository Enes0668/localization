using System.Globalization;
using Microsoft.AspNetCore.Localization;
using LocalizationApi.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. JSON Localizer servisimizi DI konteynerine ekliyoruz
builder.Services.AddSingleton<IJsonStringLocalizer, JsonStringLocalizer>();

var app = builder.Build();

// 2. Desteklenen Diller ve RequestLocalization Yapılandırması
var supportedCultures = new[]
{
    new CultureInfo("tr-TR"),
    new CultureInfo("en-US"),
    new CultureInfo("de-DE")
};

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("tr-TR"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};

// İsteklerden dili şu sıralamayla çözer:
// 1. Query String (?culture=en-US)
// 2. Cookie (c=en-US|uic=en-US)
// 3. Accept-Language Header (Accept-Language: en-US,en;q=0.9,tr;q=0.8)
app.UseRequestLocalization(localizationOptions);

// 3. MINIMAL API ENDPOINTLERİ (MVC kullanılmamıştır)

// API Bilgilendirme
app.MapGet("/", (IJsonStringLocalizer localizer) =>
{
    return Results.Ok(new
    {
        Message = "Localization API Running",
        ActiveCulture = CultureInfo.CurrentUICulture.Name,
        SupportedLanguages = new[] { "tr-TR", "en-US", "de-DE" },
        Endpoints = new[]
        {
            "/api/welcome",
            "/api/error-test?code=404",
            "/api/order-status?orderId=9876&status=Kargoya+Verildi",
            "/api/cart?count=3&total=450"
        }
    });
});

// Basit Yerelleştirme
app.MapGet("/api/welcome", (IJsonStringLocalizer localizer) =>
{
    return Results.Ok(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        Message = localizer["Welcome"]
    });
});

// Furkan Bey''in istediği Parametrik Hata Örneği: "Aldığınız Hata {0}. Dikkat ediniz."
app.MapGet("/api/error-test", (string? code, IJsonStringLocalizer localizer) =>
{
    var errorCode = code ?? "400";
    
    // Parametrik çağrı: {0} yerine errorCode oturur
    var localizedMessage = localizer["ErrorNotice", errorCode];

    return Results.BadRequest(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        ErrorCode = errorCode,
        ErrorMessage = localizedMessage
    });
});

// Çoklu Parametrik Yerelleştirme Örneği: {0} ve {1}
app.MapGet("/api/order-status", (string? orderId, string? status, IJsonStringLocalizer localizer) =>
{
    var id = orderId ?? "1001";
    var currentStatus = status ?? "Hazırlanıyor";

    // Parametrik çağrı: {0} -> id, {1} -> currentStatus
    var message = localizer["OrderUpdated", id, currentStatus];

    return Results.Ok(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        OrderId = id,
        Status = currentStatus,
        FormattedMessage = message
    });
});

// Sepet Örneği
app.MapGet("/api/cart", (int? count, decimal? total, IJsonStringLocalizer localizer) =>
{
    var itemCount = count ?? 1;
    var grandTotal = total ?? 100;

    var message = localizer["ItemCount", itemCount, grandTotal];

    return Results.Ok(new
    {
        Culture = CultureInfo.CurrentUICulture.Name,
        Message = message
    });
});

app.Run();
