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

// Baş mühendisin hedeflediği satır:
var localization = app.Services.GetRequiredService<IJsonStringLocalizer>();
app.UseMiddleware<ResponseLocalizationMiddleware>(localization, new[] { "", "Message", "Log.LogMessage", "Log.LogMessage2" });

app.UseAuthorization();

app.MapControllers();

app.Run();
