using Enes3.Middlewares;
using Enes3.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Yerel In-Memory Çeviri Servisi (Dış API yok, doğrudan localization.json okur)
builder.Services.AddSingleton<IJsonStringLocalizer, JsonStringLocalizer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ─────────────────────────────────────────────────────────────────────────────
// Furkan Bey'in istediği esnek kütüphane konfigürasyonu:
// 1. Header parametresi ("selectedLanguage", "lang", "Accept-Language")
// 2. JSON veri kaynağı:
//    - Doğrudan veritabanı/Redis/string'den: options.JsonContent = jsonFromDatabase;
//    - Veya dinamik sağlayıcıdan: options.JsonContentProvider = () => db.GetTranslationsAsJson();
//    - Veya fiziksel dosyadan: options.JsonFilePath = "Localization/localization.json";
// ─────────────────────────────────────────────────────────────────────────────
app.UseResponseLocalization(options =>
{
    options.TargetPaths = new[] { "", "Message", "Log.LogMessage", "Log.LogMessage2" };
    options.HeaderName = "selectedLanguage"; // Parametrik başlık: "selectedLanguage", "lang" veya "Accept-Language"
    options.DefaultCulture = "tr";
    options.JsonFilePath = "Localization/localization.json";

    // İPUCU: Veritabanından veya Redis'ten veri alınıyorsa doğrudan JSON string atanabilir:
    // options.JsonContent = await db.GetTranslationsJsonAsync();
});

app.UseAuthorization();

app.MapControllers();

app.Run();
