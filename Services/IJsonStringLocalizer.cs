namespace LocalizationApi.Services;

public interface IJsonStringLocalizer
{
    string this[string key] { get; }
    string this[string key, params object[] arguments] { get; }
    string GetString(string key);
    string GetString(string key, params object[] arguments);

    /// <summary>
    /// Aktif kültür için tüm key'leri döner.
    /// </summary>
    IEnumerable<string> GetAllKeys();

    /// <summary>
    /// Belirli bir kültür için tüm key'leri döner.
    /// </summary>
    IEnumerable<string> GetAllKeysForCulture(string culture);

    /// <summary>
    /// Belirli bir kültür için key çevirisini döner.
    /// Key bulunamazsa key'in kendisini döner.
    /// </summary>
    string GetWithCulture(string key, string culture);

    /// <summary>
    /// Belirli bir kültür için key çevirisini parametrelerle formatlayarak döner.
    /// Key bulunamazsa key'in kendisini döner.
    /// </summary>
    string GetWithCulture(string key, string culture, params object[] arguments);
}
