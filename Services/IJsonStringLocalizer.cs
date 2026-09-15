namespace LocalizationApi.Services;

public interface IJsonStringLocalizer
{
    string this[string key] { get; }
    string this[string key, params object[] arguments] { get; }
    string GetString(string key);
    string GetString(string key, params object[] arguments);

    /// <summary>
    /// Aktif kültür için mevcut tüm key'leri döner.
    /// Örnek: "Auth.Login", "Errors.NotFound", "Cart.Summary"
    /// </summary>
    IEnumerable<string> GetAllKeys();
}
