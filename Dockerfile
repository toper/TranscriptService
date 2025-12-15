# Dockerfile dla TranscriptService
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Kopiuj pliki csproj i przywróć zależności
COPY TranscriptService.Models/TranscriptService.Models.csproj TranscriptService.Models/
COPY TranscriptService.VoskAPI/TranscriptService.VoskAPI.csproj TranscriptService.VoskAPI/
COPY TranscriptService.Whisper/TranscriptService.Whisper.csproj TranscriptService.Whisper/
COPY TranscriptService.API/TranscriptService.csproj TranscriptService.API/
COPY TranscriptService.sln .

RUN dotnet restore TranscriptService.sln

# Kopiuj cały kod źródłowy
COPY TranscriptService.Models/ TranscriptService.Models/
COPY TranscriptService.VoskAPI/ TranscriptService.VoskAPI/
COPY TranscriptService.Whisper/ TranscriptService.Whisper/
COPY TranscriptService.API/ TranscriptService.API/

# Zbuduj aplikację
WORKDIR /src/TranscriptService.API
RUN dotnet build -c Release -o /app/build

# Publikuj aplikację
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Finalna warstwa runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

# Utwórz katalogi dla danych
RUN mkdir -p /audio /models /output

COPY --from=publish /app/publish .

# Zmienne środowiskowe z wartościami domyślnymi
ENV AUDIO_FOLDER_PATH=/audio
ENV VOSK_MODEL_PATH=/models/vosk-model
ENV OPENAI_API_KEY=""
ENV TRANSCRIPTION_ENGINE=whisper

# Uruchom aplikację z parametrami ze zmiennych środowiskowych
ENTRYPOINT dotnet TranscriptService.dll \
    "${AUDIO_FOLDER_PATH}" \
    "${VOSK_MODEL_PATH}" \
    "${OPENAI_API_KEY}" \
    "${TRANSCRIPTION_ENGINE}"
