using Microsoft.AspNetCore.Localization;
using LocalizationApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────────────────────
// 1. SERVİS KAYITLARI
// ──────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// Swagger desteği ekleniyor
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Swagger arayüzü (/swagger)
app.UseSwagger();
app.UseSwaggerUI();

// ──────────────────────────────────────────────────────────────
// 2. MIDDLEWARE BORU HATTI
// ──────────────────────────────────────────────────────────────
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("tr-TR")
});

app.UseCors();

// 1. PARAMETRE: Tüm diller (TR, EN) -> JSON metni
// 2. PARAMETRE: Çevrilecek alanlar (Field listesi)
string languagesJson = File.ReadAllText(Path.Combine("Localization", "localization.json"));

app.UseResponseLocalization(languagesJson, "lang","ResponseValue.Message", "Response.Texts.UserMessage");

// ──────────────────────────────────────────────────────────────
// 3. CONTROLLER YÖNLENDİRMESİ
// ──────────────────────────────────────────────────────────────
app.MapControllers();

app.Run();
