# ── STAGE 1: BUILD ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Önce sadece csproj kopyala → NuGet restore cache'lensin
COPY LocalizationApi.csproj .
RUN dotnet restore

# Kalan tüm dosyaları kopyala ve publish et
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── STAGE 2: RUNTIME ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Publish çıktısını kopyala
COPY --from=build /app/publish .

# Port
EXPOSE 8080

# Başlat
ENTRYPOINT ["dotnet", "LocalizationApi.dll"]
