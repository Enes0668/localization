using Microsoft.AspNetCore.Builder;

namespace LocalizationApi.Middleware;

public static class ResponseLocalizationExtensions
{
    /// <summary>
    /// Boru hattına (Pipeline) otomatik HTTP yanıt çeviri ara yazılımını ekler.
    /// </summary>
    /// <param name="app">Uygulama boru hattı</param>
    /// <param name="languagesJson">1. PARAMETRE: Tüm dillerin (TR, EN) JSON metni</param>
    /// <param name="languageKey">2. PARAMETRE: Dilin hangi parametreden/header'dan okunacağı (Örn: "lang")</param>
    /// <param name="fields">3. PARAMETRE: Çevrilmesi istenen alanlar (Örn: "ResponseValue.Message")</param>
    public static IApplicationBuilder UseResponseLocalization(
        this IApplicationBuilder app, 
        string languagesJson, 
        string languageKey,
        params string[] fields)
    {
        return app.UseMiddleware<ResponseLocalizationMiddleware>(languagesJson, languageKey, fields);
    }

    /// <summary>
    /// languageKey belirtilmezse varsayılan olarak "lang" kabul eden aşırı yükleme (overload).
    /// </summary>
    public static IApplicationBuilder UseResponseLocalization(
        this IApplicationBuilder app, 
        string languagesJson, 
        params string[] fields)
    {
        return app.UseMiddleware<ResponseLocalizationMiddleware>(languagesJson, "lang", fields);
    }
}
