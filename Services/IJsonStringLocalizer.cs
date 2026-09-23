namespace Enes3.Services;

public interface IJsonStringLocalizer
{
    string this[string key] { get; }
    string this[string key, params object[] arguments] { get; }
    string GetString(string key);
    string GetString(string key, params object[] arguments);
    IEnumerable<string> GetAllKeys();
    IEnumerable<string> GetAllKeysForCulture(string culture);
    string GetWithCulture(string key, string culture);
    string GetWithCulture(string key, string culture, params object[] arguments);
    IEnumerable<string> GetSupportedCultures();
}
