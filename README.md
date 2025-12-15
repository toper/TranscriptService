# TranscriptService

Serwis do automatycznej transkrypcji plików audio z wykorzystaniem dwóch silników rozpoznawania mowy: OpenAI Whisper (online) oraz Vosk (offline).

## Opis projektu

TranscriptService to aplikacja konsolowa napisana w .NET 8.0, która przetwarza pliki audio w formacie WAV i generuje transkrypcje z dokładnymi znacznikami czasowymi dla każdego słowa. Projekt został zaprojektowany z wykorzystaniem wzorca strategii, co pozwala na łatwą wymianę silnika transkrypcji bez zmian w głównej logice aplikacji.

## Możliwości

- **Dwa silniki transkrypcji:**
  - **Whisper** - wykorzystuje API OpenAI, oferuje wysoką jakość rozpoznawania, wymaga klucza API i połączenia z internetem
  - **Vosk** - działa offline, wymaga lokalnego modelu językowego, idealne dla danych wrażliwych

- **Przetwarzanie wsadowe** - automatycznie przetwarza wszystkie pliki WAV z wskazanego katalogu

- **Znaczniki czasowe** - każde słowo otrzymuje znacznik czasu rozpoczęcia i zakończenia

- **Format wyjściowy JSON** - łatwy do dalszego przetwarzania i integracji z innymi systemami

## Architektura

Projekt składa się z 4 modułów:

```
TranscriptService/
├── TranscriptService.API/          # Główna aplikacja konsolowa
├── TranscriptService.Models/       # Wspólne modele i interfejsy
├── TranscriptService.Whisper/      # Implementacja OpenAI Whisper
└── TranscriptService.VoskAPI/      # Implementacja Vosk
```

Wszystkie silniki transkrypcji implementują wspólny interfejs `ITranscriber`, co zapewnia spójność i możliwość łatwego rozszerzania o nowe silniki.

## Wymagania

- .NET 8.0 SDK lub nowszy
- Docker (opcjonalnie, do uruchomienia w kontenerze)
- Klucz API OpenAI (dla silnika Whisper)
- Model Vosk (dla silnika Vosk) - pobierz z https://alphacephei.com/vosk/models

## Instalacja i uruchomienie

### Uruchomienie lokalne

1. Sklonuj repozytorium:
```bash
git clone <url-repozytorium>
cd TranscriptService
```

2. Zbuduj projekt:
```bash
dotnet build TranscriptService.sln
```

3. Uruchom aplikację:
```bash
dotnet run --project TranscriptService.API -- <ścieżka_do_audio> <ścieżka_do_modelu_vosk> <klucz_api_openai> <silnik>
```

Przykład użycia Whisper:
```bash
dotnet run --project TranscriptService.API -- ./audio ./vosk-model sk-xxx123 whisper
```

Przykład użycia Vosk:
```bash
dotnet run --project TranscriptService.API -- ./audio ./vosk-model-pl dummy vosk
```

### Uruchomienie z Docker

1. Skopiuj przykładowy plik konfiguracji:
```bash
cp .env.example .env
```

2. Edytuj plik `.env` i ustaw swoje wartości:
```env
OPENAI_API_KEY=twoj-klucz-api
VOSK_MODEL_PATH=/models/vosk-model-pl
AUDIO_FOLDER_PATH=/audio
TRANSCRIPTION_ENGINE=whisper
```

3. Uruchom z docker-compose:
```bash
docker-compose up
```

## Parametry uruchomienia

| Parametr | Opis | Wymagany |
|----------|------|----------|
| `audioFolderPath` | Ścieżka do katalogu z plikami WAV | Tak |
| `voskModelPath` | Ścieżka do modelu Vosk | Tak* |
| `openAiApiKey` | Klucz API OpenAI | Tak* |
| `engine` | Silnik transkrypcji: `whisper` lub `vosk` | Tak |

\* Mimo że parametry są wymagane, tylko odpowiedni dla wybranego silnika jest używany

## Format wyjściowy

Wyniki transkrypcji są zapisywane do pliku `transcriptions.json` w formacie:

```json
[
  {
    "Item1": "plik1.wav",
    "Item2": {
      "FileName": null,
      "Transcription": [
        {
          "Word": "witam",
          "StartTime": 0.5,
          "EndTime": 0.8
        },
        {
          "Word": "wszystkich",
          "StartTime": 0.9,
          "EndTime": 1.3
        }
      ],
      "FullText": "witam wszystkich "
    }
  }
]
```

## Rozwój projektu

### Dodawanie nowego silnika transkrypcji

1. Utwórz nowy projekt: `TranscriptService.NowysSilnik`
2. Dodaj referencję do `TranscriptService.Models`
3. Zaimplementuj interfejs `ITranscriber`
4. Dodaj referencję w `TranscriptService.API`
5. Zaktualizuj logikę wyboru silnika w `Program.cs`

### Struktura kodu

- **ITranscriber** - wspólny interfejs dla wszystkich silników
- **TranscriptionResult** - model wyniku zawierający pełny tekst i listę słów z timestampami
- **WordTimestamp** - pojedyncze słowo z czasem rozpoczęcia i zakończenia

## Licencja

Projekt dostępny na licencji zawartej w pliku `LICENSE.txt`.

## Autor

Projekt stworzony do przetwarzania nagrań audio na tekst z wykorzystaniem nowoczesnych technologii rozpoznawania mowy.
