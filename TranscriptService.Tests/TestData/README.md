# Test Data

Ten folder zawiera przykładowe pliki używane w testach.

## Audio

Umieść tutaj przykładowe pliki WAV do testowania transkrypcji:

- `sample.wav` - podstawowy plik testowy
- `short.wav` - krótka próbka (do szybkich testów)
- `polish.wav` - próbka z polskim tekstem
- inne pliki według potrzeb

## Wymagania dla plików audio

Vosk wymaga plików w formacie:
- Format: WAV
- Sample rate: 16000 Hz (16 kHz)
- Kanały: mono
- Bit depth: 16-bit PCM

## Konwersja plików (ffmpeg)

Jeśli masz plik w innym formacie, możesz go przekonwertować za pomocą ffmpeg:

```bash
ffmpeg -i input.mp3 -ar 16000 -ac 1 -c:a pcm_s16le output.wav
```

## Użycie w testach

Użyj klasy `TestHelper` aby uzyskać dostęp do plików:

```csharp
var audioFilePath = TestHelper.GetTestFilePath("Audio/sample.wav");
var result = await transcriber.TranscribeAsync(audioFilePath);
```
