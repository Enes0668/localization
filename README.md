# ASP.NET Core 8 JSON-Based Parametric Localization API

Bu proje, **MVC kullanmadan (Minimal API)**, bağımsız bir dizindeki (`Localization/`) JSON dosyalarından dil verilerini okuyan, `Accept-Language` başlığı veya sorgu parametresine göre istemciye doğru dilde ve **parametrik yer tutucularla (`{0}`, `{1}`)** yanıt dönen bir referans projedir.

---

## Proje Yapısı

```
LocalizationApi/
│
├── Localization/                 # Bağımsız localization dosyaları
│   └── localization.json         # Tüm diller (en, tr) tek JSON dosyasında
│
├── Services/
│   ├── IJsonStringLocalizer.cs   # Yerelleştirme servis sözleşmesi
│   └── JsonStringLocalizer.cs    # JSON okuyan, cache''leyen ve {0} parametrelerini formatlayan servis
│
├── Program.cs                    # Minimal API endpoint''leri ve Middleware yapılandırması
├── LocalizationApi.csproj        # Proje ayarları
└── README.md                     # Dokümantasyon
```

---

## Nasıl Çalıştırılır?

Proje dizininde terminali açın:
```bash
dotnet run
```
Uygulama varsayılan olarak `http://localhost:5000` (veya belirtilen port) üzerinde ayağa kalkacaktır.

---

## Örnek İstekler ve Yanıtlar

### 1. Parametrik Hata Mesajı Testi
- **Şablon:** `"Aldığınız Hata: {0}. Dikkat ediniz."`
- **Endpoint:** `GET /api/error-test?code=404`

#### Türkçe İstek:
```bash
curl -H "Accept-Language: tr-TR" http://localhost:5000/api/error-test?code=404
```
**Yanıt (HTTP 400):**
```json
{
  "culture": "tr-TR",
  "errorCode": "404",
  "errorMessage": "Aldığınız Hata: 404. Dikkat ediniz."
}
```

#### İngilizce İstek:
```bash
curl -H "Accept-Language: en-US" http://localhost:5000/api/error-test?code=404
```
**Yanıt (HTTP 400):**
```json
{
  "culture": "en-US",
  "errorCode": "404",
  "errorMessage": "The error you received: 404. Please pay attention."
}
```

---

### 2. Çoklu Parametrik Yerelleştirme Testi
- **Şablon:** `"Sipariş #{0} durumu ''{1}'' olarak güncellendi."`
- **Endpoint:** `GET /api/order-status?orderId=9876&status=Shipped`

#### İngilizce İstek:
```bash
curl -H "Accept-Language: en-US" "http://localhost:5000/api/order-status?orderId=9876&status=Shipped"
```
**Yanıt:**
```json
{
  "culture": "en-US",
  "orderId": "9876",
  "status": "Shipped",
  "formattedMessage": "Order #9876 status has been updated to 'Shipped'."
}
```

---

### 3. URL Query Parametresi ile Dil Belirleme
Header gönderilmediğinde URL üzerinden de dil belirlenebilir:
```bash
curl "http://localhost:5000/api/welcome?culture=de-DE"
```
**Yanıt:**
```json
{
  "culture": "de-DE",
  "message": "Willkommen!"
}
```
