using Microsoft.Extensions.DependencyInjection;
using LocalizationApi.Services;

namespace LocalizationApi.Middleware;

public static class ResponseLocalizationExtensions
{
    /// <summary>
    /// Servis koleksiyonuna JSON Localizer servisini ekler.
    /// </summary>
    public static IServiceCollection AddResponseLocalization(this IServiceCollection services)
    {
        services.AddSingleton<IJsonStringLocalizer, JsonStringLocalizer>();
        return services;
    }

    /// <summary>
    /// Boru hattına (Pipeline) otomatik HTTP yanıt çeviri ara yazılımını ekler.
    /// </summary>
    public static IApplicationBuilder UseResponseLocalization(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ResponseLocalizationMiddleware>();
    }
}
