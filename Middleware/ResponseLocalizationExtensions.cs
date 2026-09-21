namespace LocalizationApi.Middleware;

public static class ResponseLocalizationExtensions
{
    /// <summary>
    /// Boru hattına (Pipeline) otomatik HTTP yanıt çeviri ara yazılımını ekler.
    /// </summary>
    public static IApplicationBuilder UseResponseLocalization(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ResponseLocalizationMiddleware>();
    }
}
