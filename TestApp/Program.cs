using LocalizationApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Swagger desteğini ekliyoruz
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger arayüzünü açıyoruz (/swagger)
app.UseSwagger();
app.UseSwaggerUI();

// ──────────────────────────────────────────────────────────────
// 1. "JSON DOSYASI AL"
// ──────────────────────────────────────────────────────────────
string languagesJson = File.ReadAllText("localization.json");

// ──────────────────────────────────────────────────────────────
// 2. "PARAMETRE OLARAK VER VE KULLAN"
// ──────────────────────────────────────────────────────────────
app.UseResponseLocalization(languagesJson, "Message", "UserMessage");

// ──────────────────────────────────────────────────────────────
// 3. API ENDPOINT'LERİ (Swagger'dan kolayca tetiklenebilir)
// ──────────────────────────────────────────────────────────────

// GET /order?culture=tr
app.MapGet("/order", (string? culture) => Results.Ok(new
{
    Code = 200,
    Message = "Payment was successful."
}))
.WithSummary("Sipariş Başarılı Testi (Otomatik Çevrilir)")
.WithDescription("culture parametresine 'tr' veya 'en' yazarak test edebilirsiniz.");

// GET /error?culture=tr
app.MapGet("/error", (string? culture) => Results.NotFound(new
{
    Code = 404,
    UserMessage = "Order not found."
}))
.WithSummary("Hata Mesajı Testi (Otomatik Çevrilir)")
.WithDescription("culture parametresine 'tr' veya 'en' yazarak test edebilirsiniz.");

app.Run();
