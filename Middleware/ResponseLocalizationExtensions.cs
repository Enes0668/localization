using Microsoft.AspNetCore.Builder;

namespace LocalizationApi.Middleware;

public static class ResponseLocalizationExtensions
{
    /// <summary>
    /// Boru hattına (Pipeline) otomatik HTTP yanıt çeviri ara yazılımını ekler.
    /// </summary>
    /// <param name="app">Uygulama boru hattı</param>
    /// <param name="languagesJson">1. PARAMETRE: Tüm dillerin (TR, EN) JSON metni</param>
    /// <param name="fields">2. PARAMETRE: Çevrilmesi istenen alanlar (Örn: "ResponseValue.Message")</param>
    public static IApplicationBuilder UseResponseLocalization(
        this IApplicationBuilder app, 
        string languagesJson, 
        params string[] fields)
    {
        return app.UseMiddleware<ResponseLocalizationMiddleware>(languagesJson, fields); // 1. PARAMETRE TÜM DİLLER(TR,EN) 2. PARAMETRE FİELD
    }
}
