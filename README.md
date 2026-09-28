# Simplex.Localization.Middleware

ASP.NET Core uygulamaları için otomatik HTTP JSON yanıt yerelleştirme (Localization) ara yazılım kütüphanesi.

Bu kütüphane, Controller veya Minimal API'lerin döndüğü JSON yanıtlarındaki metinleri araya girerek yakalar ve istenen dile (`?culture=tr` veya `Accept-Language` başlığına göre) otomatik olarak çevirir.

---

## Özellikler

* **Disk/Dosya Bağımsızlığı:** Çeviriler ister fiziksel bir `.json` dosyasından, ister **Veritabanından (SQL/PostgreSQL)**, ister **Redis** önbelleğinden saf `string` olarak verilebilir.
* **Controller Bağımsızlığı:** Controller sınıflarınızda veya servislerinizde yerelleştirme için ekstra kod yazmanız gerekmez; Controller standart İngilizce yanıtını döner, middleware yanıtı kullanıcıya gitmeden önce otomatik çevirir.
* **Yüksek Performans:** Çeviri verileri RAM'de önbelleğe alınır, her istekte tekrar tekrar ayrıştırma yapılmaz.
* **Yalın Mimari:** Sadece 2 parametre ile tek satırda devreye alınır.

---

## Kurulum

Projeye NuGet üzerinden paketi ekleyin:

```bash
dotnet add package Simplex.Localization.Middleware
```

---

## Kullanım

`Program.cs` dosyanızda middleware boru hattına tek bir satır eklemeniz yeterlidir:

```csharp
using LocalizationApi.Middleware;

var app = builder.Build();

// 1. PARAMETRE: Tüm Dillerin JSON metni (Veritabanından veya dosyadan okunan string)
// 2. PARAMETRE: Çevrilmesini istediğiniz alanlar (Field listesi)
string languagesJson = File.ReadAllText("Localization/localization.json"); // veya dbService.GetJson();

app.UseResponseLocalization(languagesJson, "ResponseValue.Message", "Response.Texts.UserMessage");

app.MapControllers();
app.Run();
```

---

## Örnek İstek ve Yanıt

### Controller Kodu (Standart İngilizce):
```csharp
[HttpGet("order")]
public IActionResult GetOrder()
{
    return Ok(new
    {
        ResponseValue = new
        {
            Code = 200,
            Message = "Payment was successful."
        }
    });
}
```

### İstek:
`GET /api/sample/order?culture=tr`

### Kullanıcıya Dönen Yanıt (Otomatik Çevrilmiş):
```json
{
  "responseValue": {
    "code": 200,
    "message": "Ödeme başarıyla gerçekleştirildi."
  }
}
```

---

## Lisans
MIT License - SimplexBT
