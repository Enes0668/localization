namespace LocalizationApi.Services;

/// <summary>
/// Çeviri işlemlerini yürüten servisin arayüzü (Interface).
/// Controller veya Endpoint'ler doğrudan somut sınıfa değil, bu arayüze bağımlı olur (Dependency Inversion prensibi).
/// </summary>
public interface IJsonStringLocalizer
{
    /// <summary>
    /// Indexer kullanımı: _localizer["Hosgeldiniz"]
    /// O anki aktif dile göre çeviriyi döner.
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// Parametreli Indexer: _localizer["HosgeldinMesaji", "Ahmet"]
    /// </summary>
    string this[string key, params object[] arguments] { get; }

    /// <summary>
    /// Verilen anahtarın o anki dildeki karşılığını döner.
    /// </summary>
    string GetString(string key);

    /// <summary>
    /// Verilen anahtarı parametrelerle formatlayarak döner (Örn: "Merhaba {0}").
    /// </summary>
    string GetString(string key, params object[] arguments);

    /// <summary>
    /// Belirli bir dil için çeviriyi getirir (Örn: GetWithCulture("Giris", "en")).
    /// Anahtar bulunamazsa anahtarın kendi adını döner.
    /// </summary>
    string GetWithCulture(string key, string culture);

    /// <summary>
    /// Belirli bir dil için parametreli çeviriyi getirir.
    /// </summary>
    string GetWithCulture(string key, string culture, params object[] arguments);

    /// <summary>
    /// Belirli bir dilde tanımlı olan tüm anahtarların listesini döner.
    /// </summary>
    IEnumerable<string> GetAllKeysForCulture(string culture);

    /// <summary>
    /// localization.json içinde tanımlanmış olan desteklenen tüm dilleri döner (Örn: "tr", "en").
    /// </summary>
    IEnumerable<string> GetSupportedCultures();
}
