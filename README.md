# Simplex.Localization.Middleware

ASP.NET Core uygulamaları için yüksek performanslı, bellek içi (in-memory) ve esnek **Otomatik JSON Yanıt Yerelleştirme (Response Localization)** middleware kütüphanesi.

---

## 🚀 Temel Özellikler

- **Veri Kaynağından Bağımsız (Decoupled):** Çevirileriniz fiziksel dosyada olmak zorunda değildir. Doğrudan **Veritabanından (SQL / PostgreSQL)**, **Redis Önbelleğinden** veya harici bir servisten çektiğiniz JSON verisini kütüphaneye verebilirsiniz.
- **Dosya Desteği:** İstenirse fiziksel `localization.json` dosya yolundan da çalışabilir.
- **Esnek Header Yapılandırması:** Dil bilgisini `Accept-Language`, `selectedLanguage`, `lang`, `X-Language` gibi dilediğiniz özel HTTP Header üzerinden okuyabilir.
- **Derin Nesne ve Liste Desteği:** İster düz metin (`""`), ister tekil alan (`"Message"`), ister iç içe geçmiş nesne (`"Log.LogMessage"`), ister dizi elemanları olsun tüm JSON yapılarını destekler.
- **Yüksek Performans:** Bellek içi (in-memory) önbellekleme ile her istekte diske veya veritabanına tekrar gitmez, sıfır gecikmeyle çalışır.

---

## 📦 Kurulum

```bash
dotnet add package Simplex.Localization.Middleware
```

---

## 🛠️ Kullanım Senaryoları

### Senaryo 1: Veritabanından / Redis'ten Gelen JSON (Fiziksel Dosya Yok!)

Veritabanından çektiğiniz çevirileri doğrudan `JsonContent` parametresi ile middleware'e verebilirsiniz:

```csharp
// Program.cs
var app = builder.Build();

// Veritabanından veya Redis'ten JSON string elde edildiğini varsayalım:
string dbTranslationsJson = await dbContext.GetTranslationsAsJsonAsync();

app.UseResponseLocalization(options =>
{
    // Çevrilecek alanlar
    options.TargetPaths = new[] { "", "Message", "Log.LogMessage", "Log.LogMessage2" };
    
    // Dilin aranacağı HTTP Header'ı
    options.HeaderName = "selectedLanguage"; // veya "lang", "Accept-Language"
    options.DefaultCulture = "tr";
    
    // Doğrudan veritabanı / redis JSON verisi atanır:
    options.JsonContent = dbTranslationsJson;
});
```

---

### Senaryo 2: Dinamik Sağlayıcı Fonksiyon (Provider Func)

Çevirileri çalışma zamanında ihtiyaç oldukça bir fonksiyon üzerinden dinamik çekmek isterseniz:

```csharp
app.UseResponseLocalization(options =>
{
    options.TargetPaths = new[] { "", "Message" };
    options.HeaderName = "selectedLanguage";
    options.DefaultCulture = "tr";
    
    // Her çağrıldığında Redis veya veritabanı önbelleğinden getirir:
    options.JsonContentProvider = () => redisCache.GetString("global:translations");
});
```

---

### Senaryo 3: Fiziksel Dosya Yolu (`localization.json`)

Klasik dosya yapısı kullanmak isteyen projeler için:

```csharp
app.UseResponseLocalization(options =>
{
    options.TargetPaths = new[] { "", "Message", "Log.LogMessage" };
    options.HeaderName = "selectedLanguage";
    options.DefaultCulture = "tr";
    
    // Fiziksel dosya yolu
    options.JsonFilePath = "Localization/localization.json";
});
```

---

## 📋 JSON Format Örneği

```json
{
  "tr": {
    "Hello": "Merhaba",
    "Welcome": "Hoş Geldiniz",
    "Customer": {
      "NotFound": "Müşteri bulunamadı."
    }
  },
  "en": {
    "Hello": "Hello",
    "Welcome": "Welcome",
    "Customer": {
      "NotFound": "Customer not found."
    }
  }
}
```

---

## ⚙️ Yapılandırma Seçenekleri (`ResponseLocalizationOptions`)

| Özellik | Tip | Varsayılan | Açıklama |
| :--- | :--- | :--- | :--- |
| `JsonContent` | `string?` | `null` | Doğrudan JSON metni (Veritabanı, Redis vb.). Doluysa dosya aranmaz. |
| `JsonContentProvider` | `Func<string>?` | `null` | JSON metnini dinamik sağlayan fonksiyon. |
| `JsonFilePath` | `string?` | `Localization/localization.json` | Diskteki fiziksel dosya yolu. |
| `HeaderName` | `string` | `"Accept-Language"` | Dil kodunun okunacağı HTTP Header adı (`selectedLanguage`, `lang` vb.). |
| `DefaultCulture` | `string` | `"tr"` | Header gelmediğinde kullanılacak varsayılan dil kodu. |
| `TargetPaths` | `string[]` | `[]` | Çevrilmesi hedeflenen JSON property path'leri (`""`, `"Message"`, `"Log.LogMessage"`). |
| `QueryParamName` | `string` | `"culture"` | URL üzerinden dil parametresi okunacak query adı (`?culture=en`). |

---

## 📄 Lisans

MIT - SimplexBT
