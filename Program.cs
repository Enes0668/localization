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
// Furkan Bey'in istediği parametrik kütüphane konfigürasyonu:
// Kullanıcı header'da hangi key'e bakılacağını ("selectedLanguage", "lang", "Accept-Language") kendisi belirler.
// ─────────────────────────────────────────────────────────────────────────────
app.UseResponseLocalization(options =>
{
    options.TargetPaths = new[] { "", "Message", "Log.LogMessage", "Log.LogMessage2" };
    options.HeaderName = "selectedLanguage"; // Parametrik başlık: "selectedLanguage", "lang" veya "Accept-Language"
    options.DefaultCulture = "tr";
});

app.UseAuthorization();

app.MapControllers();

app.Run();
