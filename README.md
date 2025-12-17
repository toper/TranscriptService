# TranscriptService

Serwis do automatycznej transkrypcji plików audio z wykorzystaniem dwóch silników rozpoznawania mowy offline: Whisper.net (lokalny) oraz Vosk.

## Opis projektu

TranscriptService to ASP.NET Core Web API napisane w .NET 8.0, które przetwarza pliki audio w formacie WAV i generuje transkrypcje z dokładnymi znacznikami czasowymi dla każdego słowa. Projekt został zaprojektowany z wykorzystaniem wzorca strategii, co pozwala na łatwą wymianę silnika transkrypcji bez zmian w głównej logice aplikacji.

## Możliwości

- **Dwa silniki transkrypcji offline:**
  - **Whisper.net** - lokalna implementacja modelu Whisper, oferuje wysoką jakość rozpoznawania, działa całkowicie offline, wspiera akcelerację GPU przez CUDA 13.0. **Zwraca segmenty (zdania/frazy) z timestampami.**
  - **Vosk** - działa offline, wymaga lokalnego modelu językowego, idealne dla danych wrażliwych. **Zwraca pojedyncze słowa z timestampami.**

- **REST API** - dostęp przez HTTP/HTTPS z dokumentacją Swagger

- **Akceleracja GPU** - wsparcie dla kart NVIDIA przez CUDA 13.0 (Whisper.net)

- **Znaczniki czasowe** - timestampy dla segmentów (Whisper) lub pojedynczych słów (Vosk)

- **Format wyjściowy JSON** - łatwy do dalszego przetwarzania i integracji z innymi systemami

- **Swagger UI** - interaktywna dokumentacja API dostępna bez wymagania autoryzacji

## Architektura

Projekt składa się z 5 modułów:

```
TranscriptService/
├── TranscriptService.API/          # ASP.NET Core Web API
├── TranscriptService.Models/       # Wspólne modele i interfejsy
├── TranscriptService.Whisper/      # Implementacja lokalnego Whisper.net
├── TranscriptService.VoskAPI/      # Implementacja Vosk
└── TranscriptService.Tests/        # Testy jednostkowe i integracyjne
```

Wszystkie silniki transkrypcji implementują wspólny interfejs `ITranscriber`, co zapewnia spójność i możliwość łatwego rozszerzania o nowe silniki.

## Wymagania

- .NET 8.0 SDK lub nowszy
- Docker (opcjonalnie, do uruchomienia w kontenerze)
- Model Whisper w formacie GGML (dla silnika Whisper.net) - pobierz z Hugging Face
- Model Vosk (dla silnika Vosk) - pobierz z https://alphacephei.com/vosk/models
- CUDA Toolkit 13.0 (opcjonalnie, dla akceleracji GPU w Whisper.net)

## Instalacja i uruchomienie

### Uruchomienie lokalne

1. Sklonuj repozytorium:
```bash
git clone <url-repozytorium>
cd TranscriptService
```

2. Skonfiguruj aplikację edytując `appsettings.json` lub `appsettings.Development.json`:
```json
{
  "AppSettings": {
    "ApiKey": "your-api-key",
    "WhisperModelPath": "D:\\Models\\ggml-base.bin",
    "VoskModelPath": "D:\\Models\\vosk-model-small-en-us-0.15"
  }
}
```

3. Zbuduj projekt:
```bash
dotnet build TranscriptService.sln
```

4. Uruchom aplikację:
```bash
dotnet run --project TranscriptService.API
```

API będzie dostępne pod adresami:
- HTTP: http://localhost:8080
- HTTPS: https://localhost:8443
- **Swagger UI: http://localhost:8080/swagger** (bez wymagania autoryzacji)

### Uruchomienie z Docker

```bash
docker build -t transcriptservice -f TranscriptService.API/Dockerfile .
docker run -p 8080:8080 -p 8443:8443 transcriptservice
```

### Publikacja standalone

```bash
dotnet publish TranscriptService.API/TranscriptService.csproj -c Release -o ./publish
```

## Konfiguracja

Plik `appsettings.json` zawiera następujące ustawienia:

| Parametr | Opis | Wymagany |
|----------|------|----------|
| `ApiKey` | Klucz API do autoryzacji żądań | Tak |
| `WhisperModelPath` | Ścieżka do modelu Whisper GGML | Tak (dla Whisper) |
| `VoskModelPath` | Ścieżka do modelu Vosk | Tak (dla Vosk) |

## Użycie API

### Endpointy

#### POST /api/v1/transcript/file
Prześlij i transkrybuj plik audio.

```bash
curl -X POST "http://localhost:8080/api/v1/transcript/file" \
  -H "X-API-KEY: your-api-key" \
  -F "audioFile=@test.wav" \
  -F "engine=whisper"
```

#### POST /api/v1/transcript/path
Transkrybuj plik audio ze ścieżki serwera.

```bash
curl -X POST "http://localhost:8080/api/v1/transcript/path" \
  -H "X-API-KEY: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"filePath": "/path/to/audio.wav", "engine": "whisper"}'
```

### Format odpowiedzi

API zwraca JSON w następującym formacie:

**Dla Vosk (pojedyncze słowa):**
```json
{
  "fileName": "test.wav",
  "fullText": "witam wszystkich",
  "words": [
    {
      "word": "witam",
      "startTime": 0.5,
      "endTime": 0.8
    },
    {
      "word": "wszystkich",
      "startTime": 0.9,
      "endTime": 1.3
    }
  ],
  "engine": "vosk",
  "processedAt": "2025-12-17T10:30:00Z"
}
```

**Dla Whisper (segmenty - zdania/frazy):**
```json
{
  "fileName": "test.wav",
  "fullText": "witam wszystkich dzisiaj jest piękny dzień",
  "words": [
    {
      "word": "witam wszystkich",
      "startTime": 0.0,
      "endTime": 1.5
    },
    {
      "word": "dzisiaj jest piękny dzień",
      "startTime": 1.6,
      "endTime": 3.8
    }
  ],
  "engine": "whisper",
  "processedAt": "2025-12-17T10:30:00Z"
}
```

**Uwaga:** Pole `words` jest technicznie nazwane "words", ale zawiera:
- **Pojedyncze słowa** dla silnika Vosk
- **Segmenty (zdania/frazy)** dla silnika Whisper

## Swagger UI

Dokumentacja API dostępna jest pod adresem **http://localhost:8080/swagger** i **nie wymaga autoryzacji**.

Swagger UI pozwala:
- Przeglądać wszystkie dostępne endpointy
- Testować API bezpośrednio z przeglądarki
- Sprawdzać schematy żądań i odpowiedzi
- Poznać szczegóły każdego endpointu

**Uwaga:** Testując API przez Swagger, pamiętaj o dodaniu nagłówka `X-API-KEY` w żądaniach do chrononych endpointów.

## Testowanie

Uruchom testy jednostkowe i integracyjne:

```bash
dotnet test TranscriptService.sln
```

Testy obejmują:
- Testy jednostkowe komponentów
- Testy integracyjne z rzeczywistymi plikami audio
- Testy middleware autoryzacji
- Testy validacji

## Rozwój projektu

### Dodawanie nowego silnika transkrypcji

1. Utwórz nowy projekt: `TranscriptService.NowySilnik`
2. Dodaj referencję do `TranscriptService.Models`
3. Zaimplementuj interfejs `ITranscriber`
4. Dodaj referencję w `TranscriptService.API`
5. Zaktualizuj `TranscriptProvider.CreateTranscriber()` aby wspierał nowy silnik
6. Dodaj niezbędną konfigurację do `appsettings.json`

### Struktura kodu

- **ITranscriber** - wspólny interfejs dla wszystkich silników
- **TranscriptionResult** - model wyniku zawierający pełny tekst i listę słów z timestampami
- **WordTimestamp** - pojedyncze słowo z czasem rozpoczęcia i zakończenia

## Szczegóły implementacji

### Whisper.net
- Używa lokalnej biblioteki Whisper.net do transkrypcji offline
- Wspiera akcelerację GPU przez CUDA 13.0
- Wymaga modeli w formacie GGML (dostępne na Hugging Face)
- Konfigurowalne wsparcie językowe (domyślnie: polski)
- Lazy initialization dla efektywnego wykorzystania zasobów
- **Zwraca segmenty (zdania/frazy) z timestampami** - nie pojedyncze słowa
- Zobacz `WHISPER_LOCAL_SETUP.md` dla szczegółów konfiguracji

### Vosk
- Używa lokalnych modeli Vosk
- Przetwarza audio w 4096-bajtowych blokach
- Wymaga próbkowania 16kHz
- **Zwraca pojedyncze słowa z timestampami** - wysoka precyzja na poziomie słów

## Licencja

Projekt dostępny na licencji zawartej w pliku `LICENSE.txt`.

## Autor

Toper System Jarosław Karlik - na potrzeby własne.
