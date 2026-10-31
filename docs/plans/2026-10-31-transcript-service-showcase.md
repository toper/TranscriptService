# TranscriptService Showcase — plan implementacji

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Przebudować TranscriptService w zestaw mikroserwisów .NET 10 (API + trzy workery silników + demo Blazor), który transkrybuje nagrania telefoniczne 8 kHz i nadaje się do pokazania przy rekrutacji.

**Architecture:** API (minimal API, SQLite, Scalar) przyjmuje nagranie, zapisuje je na współdzielonym wolumenie i publikuje komendę przez EasyNetQ/RabbitMQ. Worker wybranego silnika (Whisper.net / FasterWhisper.NET / Vosk, CPU lub CUDA) przygotowuje audio (8→16 kHz, mono), transkrybuje i publikuje zdarzenie wyniku. API zapisuje wynik, dostarcza go podpisanym callbackiem i usuwa zadanie; demo Blazor korzysta z API przez callback i wypycha tekst przez SignalR.

**Tech Stack:** .NET 10, ASP.NET Core minimal API, EF Core SQLite, EasyNetQ 8, Scalar, NAudio.Core, Whisper.net 1.9, FasterWhisper.NET 1.0, Vosk 0.3.38, Blazor, SignalR, NLog, xUnit v3, AwesomeAssertions, Testcontainers, WireMock.Net, Docker (multi-stage, chiseled, CUDA), GitHub Actions.

**Spec:** `docs/specs/2026-10-31-transcript-service-showcase-design.md`

## Global Constraints

- Target: `net10.0` we wszystkich projektach; `Nullable=enable`, `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`, analizatory .NET (`AnalysisLevel=latest-recommended`).
- Central Package Management (`Directory.Packages.props`); wersje bazowe: EasyNetQ 8.1.11 (+ `EasyNetQ.Serialization.SystemTextJson`), Scalar.AspNetCore 2.18.7, Whisper.net 1.9.1 (+ `.Runtime`, `.Runtime.Cuda.Linux`), FasterWhisper.NET 1.0.8 (+ `.Gpu`), Vosk 0.3.38, NAudio.Core 3.3.0, Microsoft.EntityFrameworkCore.Sqlite 10.0.x, Microsoft.Extensions.Http.Resilience 10.x, NLog.Web.AspNetCore 6.2.2, Asp.Versioning.Http 10.x, xunit.v3, AwesomeAssertions, Testcontainers.RabbitMq 4.x.
- Język repozytorium: **angielski** — kod, komentarze, README, opisy OpenAPI/Scalar, commity, PR. Specyfikacja i ten plan po polsku.
- Commity i PR **bez atrybucji narzędzi AI** (żadnych `Co-Authored-By`, „Generated with…”).
- Brak `Console.WriteLine`, `async void`, `.Result`, `.Wait()`; każda metoda I/O przyjmuje `CancellationToken`.
- Konfiguracja przez `IOptions<T>` z `ValidateDataAnnotations().ValidateOnStart()` — zła konfiguracja wywraca start.
- Limity: nagranie ≤ **100 MB** i ≤ **30 min**; TTL wyniku bez callbacku **15 min**; priorytet wysoki dla nagrań ≤ **30 s**. Wszystkie konfigurowalne.
- Formaty audio: WAV PCM16, G.711 μ-law, G.711 A-law; 1 lub 2 kanały; dowolna częstotliwość → model dostaje `float[]` **16 kHz mono**.
- Worker nie ma dostępu do SQLite; audio nie jedzie w treści wiadomości (współdzielony wolumen `/data/audio`).
- Repo publiczne: żadnych nagrań z ToperTriton w repo; fixture 8 kHz generowany w testach z `sample.wav`.
- Obrazy Dockera: multi-stage, użytkownik nonroot, bez SDK w runtime, modele jako wolumen read-only `/models`.
- Testy wymagające modelu: pomijane (`Assert.Skip`), gdy brak `MODELS_DIR`/pliku — nigdy porażka.

## Review Focus

1. **WAV z dodatkowymi chunkami (`LIST`, `fact`) albo nagłówkiem `WAVE_FORMAT_EXTENSIBLE`, wysłany z `Content-Type: application/octet-stream`** — oczekiwane: przyjęty; format rozpoznawany z zawartości, nie z nagłówka HTTP. Testy: Task 2.1 (`Inspect_AcceptsExtensibleHeaderAndExtraChunks`), Task 3.3 (`Post_AcceptsOctetStreamWav`).
2. **Nagranie samej ciszy** — oczekiwane: `Completed` z pustym tekstem i pustą listą segmentów, nie `Failed`. Test: Task 2.2 (`Handle_SilentAudio_PublishesCompletedWithEmptyText`).
3. **Callback stale niedostępny** — oczekiwane: skończona liczba ponowień, zadanie oznaczone `CallbackFailed`, sprzątnięte po TTL; brak nieskończonej pętli. Test: Task 3.5 (`Dispatch_EndpointDown_MarksCallbackFailedAfterRetries`).
4. **Zdarzenie wyniku dla zadania już usuniętego (DELETE w trakcie przetwarzania)** — oczekiwane: zignorowane z logiem, bez wyjątku i bez ponownego dostarczania wiadomości. Test: Task 3.4 (`OnCompleted_UnknownJob_IsIgnored`).
5. **Zduplikowane zdarzenie `TranscriptionCompleted` (redelivery)** — oczekiwane: idempotentne, callback wysłany raz. Test: Task 3.4 (`OnCompleted_Duplicate_DoesNotEnqueueSecondCallback`).

---

## Docelowy układ plików

```
TranscriptService.slnx
global.json, Directory.Build.props, Directory.Packages.props, .editorconfig, .dockerignore, .gitignore
src/
  TranscriptService.Contracts/          Messages/*.cs, TranscriptSegment.cs, MessagePriority.cs
  TranscriptService.Audio/              AudioInspector.cs, AudioFormatInfo.cs, AudioPreprocessor.cs,
                                        AudioSamples.cs, UnsupportedAudioException.cs
  TranscriptService.Api/                Program.cs, Endpoints/, Jobs/ (entity, DbContext, migrations),
                                        Messaging/, Callbacks/, Retention/, Security/, OpenApi/, Options/, Dockerfile
  TranscriptService.Worker.Core/        ITranscriber.cs, EngineOptions.cs, TranscriptionJobHandler.cs,
                                        Messaging/, Health/, WorkerHostExtensions.cs
  TranscriptService.Worker.WhisperNet/  Program.cs, WhisperNetTranscriber.cs, WhisperNetOptions.cs, Dockerfile
  TranscriptService.Worker.FasterWhisper/ (analogicznie)
  TranscriptService.Worker.Vosk/        (analogicznie)
  TranscriptService.Demo.Web/           Program.cs, Components/, Hubs/, Callbacks/, TranscriptApi/, wwwroot/js/recorder.js, Dockerfile
tests/
  TranscriptService.Audio.Tests/, TranscriptService.Api.Tests/, TranscriptService.Worker.Core.Tests/,
  TranscriptService.Engines.Tests/ (wymagają modeli), TranscriptService.IntegrationTests/ (Testcontainers),
  TranscriptService.Demo.Web.Tests/
  TestData/sample.wav  (istniejący, 16 kHz mono)
deploy/
  docker-compose.yml, docker-compose.gpu.yml, .env.example, models/manifest.json, models/fetch-models.sh
docs/  specs/, plans/, adr/
.github/workflows/ci.yml
```

## Proces dla każdej fazy (PR)

Każda faza N to gałąź `phase-N-<nazwa>` od aktualnego `master`. Po ostatnim zadaniu fazy:

- [ ] `dotnet build TranscriptService.slnx -c Release` — 0 ostrzeżeń, 0 błędów.
- [ ] `dotnet test TranscriptService.slnx` — wszystko zielone (testy modeli mogą być „skipped”).
- [ ] Skill `code-review` (poziom `high`) na diffie gałęzi względem `master`; poprawki jako osobny commit.
- [ ] Skill `simplify` na diffie gałęzi; poprawki jako osobny commit; ponowny build + testy.
- [ ] `git push -u origin <gałąź>` i `gh pr create --base master` — tytuł i opis po angielsku (Summary / Changes / How to verify), bez atrybucji.
- [ ] Merge po zielonym CI (zgodnie z ustaleniem z użytkownikiem); `git checkout master && git pull`.

---

## Faza 0 — specyfikacja i plan (gałąź `f_showcase_spec`, istniejąca)

### Task 0.1: PR ze specyfikacją i planem

- [ ] Commit `docs/plans/2026-10-31-transcript-service-showcase.md`.
- [ ] Push + PR „Add design spec and implementation plan for the showcase rewrite”, merge.

---

## Faza 1 — fundament repozytorium (`phase-1-foundation`)

### Task 1.1: Nowy szkielet rozwiązania, usunięcie starego kodu

**Files:**
- Delete: `TranscriptService.API/`, `TranscriptService.Models/`, `TranscriptService.Whisper/`, `TranscriptService.VoskAPI/`, `TranscriptService.Tests/` (poza `TestData/Audio/sample.wav` → przenieść do `tests/TestData/sample.wav`), `TranscriptService.csproj`, `TranscriptService.sln`, `Dockerfile`, `docker-compose.yml`, `WHISPER_LOCAL_SETUP.md`
- Create: `TranscriptService.slnx`, `global.json` (SDK 10.0.x, `rollForward: latestFeature`), `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.dockerignore`
- Modify: `.gitignore` (dodać `models/`, `data/`, `*.db`; usunąć stare wpisy ścieżek modeli)

- [ ] **Step 1:** Usuń stare projekty i pliki, przenieś `sample.wav`.
- [ ] **Step 2:** Utwórz pliki solution-level wg Global Constraints. `.dockerignore`: `**/bin`, `**/obj`, `models/`, `data/`, `.git`, `**/*.md` poza README, `tests/`.
- [ ] **Step 3:** `dotnet build TranscriptService.slnx` → „Build succeeded” (pusta solucja buduje się).
- [ ] **Step 4:** Commit `chore: replace legacy projects with new solution skeleton`.

### Task 1.2: Projekt Contracts

**Files:**
- Create: `src/TranscriptService.Contracts/TranscriptService.Contracts.csproj`, `Messages/TranscribeAudio.cs`, `Messages/TranscriptionCompleted.cs`, `Messages/TranscriptionFailed.cs`, `TranscriptSegment.cs`, `MessagePriority.cs`
- Test: `tests/TranscriptService.Contracts.Tests/MessageSerializationTests.cs`, `MessagePriorityTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record TranscribeAudio(Guid JobId, string AudioPath, string Language, TimeSpan AudioDuration);
public sealed record TranscriptionCompleted(Guid JobId, string Engine, string FullText,
    IReadOnlyList<TranscriptSegment> Segments, TimeSpan AudioDuration, TimeSpan ProcessingTime);
public sealed record TranscriptionFailed(Guid JobId, string Engine, string Reason);
/// <summary>Phrase for Whisper engines, single word for Vosk.</summary>
public sealed record TranscriptSegment(string Text, TimeSpan Start, TimeSpan End);
public static class MessagePriority
{
    public const byte High = 5; public const byte Normal = 1; public const byte Max = 10;
    public static byte For(TimeSpan audioDuration, TimeSpan shortThreshold);
}
public static class QueueNames { public const string TranscriptionRequests = "transcription.requests"; }
```
Kontrakty bez zależności od EasyNetQ (atrybuty kolejki ustawiane konwencją po stronie hosta).

- [ ] **Step 1: Testy**
  - `TranscriptionCompleted_RoundTripsThroughSystemTextJson` — serializacja i deserializacja `System.Text.Json` (web defaults) daje rekord równy oryginałowi, łącznie z listą segmentów i `TimeSpan`.
  - `For_ShortAudio_ReturnsHigh` — `For(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)) == High`; `For(31 s, 30 s) == Normal`.
- [ ] **Step 2:** `dotnet test tests/TranscriptService.Contracts.Tests` → FAIL (brak typów).
- [ ] **Step 3:** Implementacja.
- [ ] **Step 4:** Testy → PASS.
- [ ] **Step 5:** Commit `feat: add message contracts`.

### Task 1.3: CI

**Files:** Create `.github/workflows/ci.yml`

- [ ] **Step 1:** Workflow na `pull_request` i `push` do `master`: `actions/setup-dotnet` (10.0.x, z `global.json`), `dotnet restore`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build --logger trx`; publikacja wyników testów. (Budowanie obrazów dochodzi w fazie 4.)
- [ ] **Step 2:** Commit `ci: add build and test workflow`. Weryfikacja: zielony check na PR fazy 1.

**→ Proces fazy (review, simplify, PR).**

---

## Faza 2 — audio i rdzeń workera (`phase-2-worker-core`)

### Task 2.1: Biblioteka Audio — inspekcja i przygotowanie

**Files:**
- Create: `src/TranscriptService.Audio/` — `AudioFormatInfo.cs`, `AudioInspector.cs`, `AudioSamples.cs`, `AudioPreprocessor.cs`, `UnsupportedAudioException.cs`
- Test: `tests/TranscriptService.Audio.Tests/AudioInspectorTests.cs`, `AudioPreprocessorTests.cs`, `WavFixtures.cs`

**Interfaces — Produces:**
```csharp
public enum AudioEncoding { Pcm16, MuLaw, ALaw }
public sealed record AudioFormatInfo(AudioEncoding Encoding, int SampleRate, int Channels, TimeSpan Duration);
public static class AudioInspector
{   // throws UnsupportedAudioException with human-readable message
    public static AudioFormatInfo Inspect(Stream wav);
}
public sealed record AudioSamples(float[] Samples, int SampleRate) { public TimeSpan Duration { get; } }
public sealed class AudioPreprocessor
{
    public const int TargetSampleRate = 16_000;
    public Task<AudioSamples> LoadAsync(string path, CancellationToken ct);   // → 16 kHz mono float [-1,1]
}
public sealed class UnsupportedAudioException(string message) : Exception(message);
```
Podejście: NAudio.Core — `WaveFileReader` (obsługuje chunki dodatkowe i extensible), dekodowanie G.711 przez `MuLawDecoder`/`ALawDecoder`, stereo→mono uśrednieniem, resampling `WdlResamplingSampleProvider` (nie liniowy — wzór: ToperTriton `asr/adapter.py`). Inne szerokości próbki, float WAV, >2 kanały → `UnsupportedAudioException` („expected PCM 16-bit, μ-law or A-law; got …”).

`WavFixtures` (helper testów) generuje w pamięci: sinus o zadanej częstotliwości/próbkowaniu/kanałach/kodowaniu, plik z chunkiem `LIST`, plik extensible, oraz `SampleAt8kHzMuLaw()` — `tests/TestData/sample.wav` przepróbkowany do 8 kHz i zakodowany μ-law.

- [ ] **Step 1: Testy**
  - `Inspect_Pcm16Mono8kHz_ReturnsFormat` — `Encoding=Pcm16, SampleRate=8000, Channels=1`, `Duration` = długość fixture ±1 ms.
  - `Inspect_MuLaw_And_ALaw_AreRecognized`.
  - `Inspect_AcceptsExtensibleHeaderAndExtraChunks` (Review Focus 1).
  - `Inspect_Rejects24Bit_Float_ThreeChannels_NotWav_Empty` — `UnsupportedAudioException`, komunikat zawiera wykryty format.
  - `Load_8kHzMono_Returns16kHzWithDoubleSampleCount` — `SampleRate==16000`, liczba próbek = 2× ±1.
  - `Load_Stereo_AveragesChannels` — L=+0.5, R=−0.5 stałe → próbki ≈ 0.
  - `Load_Resample_PreservesTone` — sinus 1 kHz @ 8 kHz → po resamplingu dominujący bin FFT/Goertzel przy 1 kHz, amplituda w ±10%.
  - `Load_SampleAt8kHzMuLaw_DurationMatchesSource` — czas trwania = `sample.wav` (11 s) ±20 ms.
- [ ] **Step 2:** Testy → FAIL.
- [ ] **Step 3:** Implementacja.
- [ ] **Step 4:** Testy → PASS.
- [ ] **Step 5:** Commit `feat(audio): add WAV inspection and 16 kHz mono preprocessing`.

### Task 2.2: Rdzeń przetwarzania zadania w workerze

**Files:**
- Create: `src/TranscriptService.Worker.Core/ITranscriber.cs`, `TranscriptionOutput.cs`, `EngineOptions.cs`, `TranscriptionJobHandler.cs`, `Messaging/ITranscriptionResultPublisher.cs`
- Test: `tests/TranscriptService.Worker.Core.Tests/TranscriptionJobHandlerTests.cs`

**Interfaces — Consumes:** `TranscribeAudio`, `TranscriptionCompleted`, `TranscriptionFailed`, `TranscriptSegment` (1.2); `AudioPreprocessor`, `AudioSamples`, `UnsupportedAudioException` (2.1).
**Produces:**
```csharp
public interface ITranscriber : IAsyncDisposable
{
    string EngineName { get; }                         // "whispernet" | "fasterwhisper" | "vosk"
    Task InitializeAsync(CancellationToken ct);        // loads model once; throws on bad config
    Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken ct);
}
public sealed record TranscriptionOutput(string FullText, IReadOnlyList<TranscriptSegment> Segments);
public enum ComputeDevice { Cpu, Cuda }
public sealed class EngineOptions                      // section "Engine"
{ [Required] public string ModelPath { get; set; } = "";
  public ComputeDevice Device { get; set; } = ComputeDevice.Cpu;
  [Required] public string DefaultLanguage { get; set; } = "pl";
  [Range(1, 16)] public int MaxConcurrency { get; set; } = 1; }
public interface ITranscriptionResultPublisher
{ Task PublishAsync(TranscriptionCompleted m, CancellationToken ct);
  Task PublishAsync(TranscriptionFailed m, CancellationToken ct); }
public sealed class TranscriptionJobHandler(ITranscriber, AudioPreprocessor, ITranscriptionResultPublisher, TimeProvider, ILogger<TranscriptionJobHandler>)
{ public Task HandleAsync(TranscribeAudio command, CancellationToken ct); }
```
Reguły: `UnsupportedAudioException`, `FileNotFoundException`, `NotSupportedException` (np. język) → publikuj `Failed` (wiadomość zostaje potwierdzona); `OperationCanceledException` przy anulowaniu hosta → propaguj (wiadomość wraca do kolejki); inny wyjątek → publikuj `Failed` z ogólnym powodem i zaloguj Error. `ProcessingTime` mierzone `TimeProvider`.

- [ ] **Step 1: Testy** (fałszywy `ITranscriber`, fałszywy publisher, `FakeTimeProvider`)
  - `Handle_ValidAudio_PublishesCompletedWithEngineAndDurations`.
  - `Handle_SilentAudio_PublishesCompletedWithEmptyText` (Review Focus 2).
  - `Handle_UnsupportedAudio_PublishesFailedWithReason`.
  - `Handle_MissingFile_PublishesFailed`.
  - `Handle_HostCancellation_Propagates_AndPublishesNothing`.
  - `Handle_TranscriberThrows_PublishesFailedWithoutLeakingStackTrace`.
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(worker): add transcription job handler`.

### Task 2.3: Host workera (EasyNetQ, inicjalizacja modelu, health)

**Files:**
- Create: `src/TranscriptService.Worker.Core/WorkerHostExtensions.cs`, `Messaging/TranscriptionRequestConsumer.cs`, `Messaging/EasyNetQResultPublisher.cs`, `Health/ModelReadyHealthCheck.cs`, `ModelInitializationService.cs`, `nlog.config` (shared, link)
- Test: `tests/TranscriptService.Worker.Core.Tests/WorkerHostTests.cs`

**Interfaces — Produces:**
```csharp
public static class WorkerHostExtensions
{   // registers options (ValidateOnStart), ITranscriber singleton, handler, EasyNetQ, consumer, health, NLog
    public static WebApplicationBuilder AddTranscriptionWorker<TTranscriber, TEngineOptions>(this WebApplicationBuilder b)
        where TTranscriber : class, ITranscriber where TEngineOptions : EngineOptions;
    public static WebApplication MapWorkerHealth(this WebApplication app);   // /health/live, /health/ready
}
```
Worker to `WebApplication.CreateSlimBuilder` (tylko health przez HTTP, port 8080). `ModelInitializationService : IHostedLifecycleService` woła `InitializeAsync` w `StartingAsync` — wyjątek wywraca start. Konsument: `bus.SendReceive.ReceiveAsync<TranscribeAudio>(QueueNames.TranscriptionRequests, …)` z `prefetchCount = MaxConcurrency`, kolejka z `x-max-priority = MessagePriority.Max`. Connection string: `ConnectionStrings:RabbitMq`.

- [ ] **Step 1: Testy**
  - `Start_TranscriberInitializationFails_HostFailsToStart`.
  - `Start_InvalidEngineOptions_HostFailsToStart` (pusty `ModelPath`).
  - `Ready_BeforeModelLoaded_IsUnhealthy_AfterwardsHealthy`.
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(worker): add worker host with model warm-up and health checks`.

**→ Proces fazy.**

---

## Faza 3 — Transcript API (`phase-3-api`)

### Task 3.1: Szkielet API — opcje, klucz API, ProblemDetails, OpenAPI + Scalar, health

**Files:**
- Create: `src/TranscriptService.Api/Program.cs`, `Options/ApiOptions.cs`, `Security/ApiKeyAuthenticationHandler.cs`, `OpenApi/ApiKeySecuritySchemeTransformer.cs`, `nlog.config`, `appsettings.json`, `appsettings.Development.json`
- Test: `tests/TranscriptService.Api.Tests/ApiKeyAuthenticationTests.cs`, `OpenApiDocumentTests.cs`, `ApiTestFactory.cs`

**Interfaces — Produces:**
```csharp
public sealed class ApiOptions   // section "Api"
{ [Required, MinLength(16)] public string ApiKey { get; set; } = "";
  [Required, MinLength(32)] public string CallbackSigningSecret { get; set; } = "";
  [Required] public string StoragePath { get; set; } = "/data/audio";
  public long MaxUploadBytes { get; set; } = 100 * 1024 * 1024;
  public TimeSpan MaxAudioDuration { get; set; } = TimeSpan.FromMinutes(30);
  public TimeSpan ResultTtl { get; set; } = TimeSpan.FromMinutes(15);
  public TimeSpan HighPriorityThreshold { get; set; } = TimeSpan.FromSeconds(30);
  public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1); }
```
Schemat uwierzytelnienia `ApiKey` (nagłówek `X-API-KEY`, porównanie `CryptographicOperations.FixedTimeEquals`); grupa `/api/v1` wymaga autoryzacji, health i `/scalar` nie. `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()`. `AddOpenApi()` (Microsoft.AspNetCore.OpenApi) + `MapScalarApiReference()` z tytułem, opisem i schematem klucza. Kestrel `MaxRequestBodySize = MaxUploadBytes`.

- [ ] **Step 1: Testy** (`WebApplicationFactory`)
  - `ApiGroup_WithoutKey_Returns401ProblemDetails`; `WithWrongKey_Returns401`; `HealthLive_WithoutKey_Returns200`.
  - `OpenApiDocument_ContainsApiKeySchemeAndTranscriptionsPaths` (GET `/openapi/v1.json`).
  - `Start_MissingApiKey_FailsToStart`.
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(api): add API host with key auth, problem details and Scalar`.

### Task 3.2: Trwałość zadań (EF Core SQLite)

**Files:**
- Create: `src/TranscriptService.Api/Jobs/TranscriptionJob.cs`, `JobStatus.cs`, `TranscriptionDbContext.cs`, `Migrations/*` (`dotnet ef migrations add Initial`), `Jobs/AudioStorage.cs`
- Test: `tests/TranscriptService.Api.Tests/Jobs/AudioStorageTests.cs`, `TranscriptionDbContextTests.cs`

**Interfaces — Produces:**
```csharp
public enum JobStatus { Queued, Completed, Failed, CallbackFailed }   // w Contracts/Http (część TranscriptionResponse)
public sealed class TranscriptionJob
{ Guid Id; JobStatus Status; string Language; Uri? CallbackUrl; string AudioPath; TimeSpan AudioDuration;
  DateTimeOffset CreatedAt; DateTimeOffset? FinishedAt; string? Engine; string? FullText;
  List<TranscriptSegment> Segments /* JSON column */; string? FailureReason; bool CallbackDelivered;
  TimeSpan? ProcessingTime; }
public sealed class AudioStorage(IOptions<ApiOptions>)
{ Task<string> SaveAsync(Guid jobId, Stream audio, CancellationToken ct);   // "{StoragePath}/{jobId}.wav"
  void Delete(string path); }                                                // idempotent
```
Migracje stosowane przy starcie (`Database.MigrateAsync` w hosted service przed przyjmowaniem ruchu). Connection string `ConnectionStrings:Jobs` = `Data Source=/data/jobs.db`.

- [ ] **Step 1: Testy:** `Save_WritesFileNamedByJobId`; `Delete_MissingFile_DoesNotThrow`; `Job_WithSegments_RoundTripsThroughSqlite` (SQLite in-memory).
- [ ] **Step 2–4:** FAIL → implementacja + migracja → PASS.
- [ ] **Step 5:** Commit `feat(api): add job persistence and audio storage`.

### Task 3.3: Endpointy `/api/v1/transcriptions`

**Files:**
- Create: `src/TranscriptService.Api/Endpoints/TranscriptionEndpoints.cs`, `Endpoints/CreateTranscriptionRequest.cs`, `Messaging/ITranscriptionRequestPublisher.cs`, `src/TranscriptService.Contracts/Http/TranscriptionResponse.cs` (w Contracts — ten sam kształt dla GET i callbacku, używany też przez Demo.Web)
- Test: `tests/TranscriptService.Api.Tests/Endpoints/TranscriptionEndpointsTests.cs`

**Interfaces — Consumes:** `AudioInspector` (2.1), `AudioStorage`, `TranscriptionDbContext` (3.2), `MessagePriority` (1.2).
**Produces:**
```csharp
public interface ITranscriptionRequestPublisher { Task PublishAsync(TranscribeAudio command, byte priority, CancellationToken ct); }
public sealed record TranscriptionResponse(Guid Id, JobStatus Status, string Language, DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt, string? Engine, string? FullText, IReadOnlyList<TranscriptSegment>? Segments,
    string? FailureReason, TimeSpan AudioDuration, TimeSpan? ProcessingTime);
```
- `POST` (multipart: `audio` (IFormFile), `language` (opcj., `^[a-z]{2}$`, domyślnie `pl`), `callbackUrl` (opcj., absolutny http/https)) → `202` + `Location: /api/v1/transcriptions/{id}` + body `TranscriptionResponse`. Kolejność: inspekcja → zapis pliku → zapis zadania → publikacja (przy porażce publikacji: usunięcie pliku i zadania, `503`).
- `GET /{id}` → `200` | `404`; `DELETE /{id}` → `204` (usuwa zadanie i plik) | `404`.
- Każdy endpoint: `.WithSummary/.WithDescription`, `.Produces<…>`, `.ProducesProblem(…)`, przykłady w OpenAPI.

- [ ] **Step 1: Testy** (fałszywy publisher, tymczasowy katalog storage)
  - `Post_Valid8kHzWav_Returns202WithLocation_StoresFile_PublishesCommand`.
  - `Post_ShortAudio_PublishesHighPriority`; `Post_LongAudio_PublishesNormalPriority`.
  - `Post_AcceptsOctetStreamWav` (Review Focus 1).
  - `Post_UnsupportedFormat_Returns400ProblemDetailsWithReason`.
  - `Post_TooLong_Returns400`; `Post_TooLarge_Returns413`.
  - `Post_InvalidCallbackUrl_Returns400` (`ftp://…`, ścieżka względna).
  - `Post_PublishFails_Returns503_AndCleansUp`.
  - `Get_Unknown_Returns404`; `Delete_RemovesJobAndFile`.
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(api): add transcription endpoints`.

### Task 3.4: Messaging API — publikacja komend, obsługa zdarzeń wyniku

**Files:**
- Create: `src/TranscriptService.Api/Messaging/EasyNetQRequestPublisher.cs`, `TranscriptionResultSubscriber.cs` (hosted), `TranscriptionResultHandler.cs`, `Callbacks/CallbackQueue.cs`
- Test: `tests/TranscriptService.Api.Tests/Messaging/TranscriptionResultHandlerTests.cs`

**Interfaces — Produces:**
```csharp
public sealed class CallbackQueue   // Channel<Guid>, unbounded, single reader
{ ValueTask EnqueueAsync(Guid jobId, CancellationToken ct); IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct); }
public sealed class TranscriptionResultHandler(TranscriptionDbContext, CallbackQueue, TimeProvider, ILogger<…>)
{ Task HandleAsync(TranscriptionCompleted m, CancellationToken ct);
  Task HandleAsync(TranscriptionFailed m, CancellationToken ct); }
```
Wysyłka komendy: `bus.SendReceive.SendAsync(QueueNames.TranscriptionRequests, cmd, c => c.WithPriority(priority))`. Subskrypcja zdarzeń: `bus.PubSub.SubscribeAsync<…>("api", …)`. Health `ready` API: SQLite (`AddDbContextCheck`) + połączenie RabbitMQ (`IBus.Advanced.IsConnected`) — test `Ready_RabbitMqDown_IsUnhealthy` w 3.7. Przy starcie API kolejka zadań z callbackiem w stanie `Completed/Failed` bez `CallbackDelivered` trafia ponownie do `CallbackQueue` (restart nie gubi callbacków).

- [ ] **Step 1: Testy**
  - `OnCompleted_UpdatesJobAndEnqueuesCallback`; `OnCompleted_NoCallbackUrl_DoesNotEnqueue`.
  - `OnFailed_StoresReason_AndDeletesAudioFile`.
  - `OnCompleted_UnknownJob_IsIgnored` (Review Focus 4).
  - `OnCompleted_Duplicate_DoesNotEnqueueSecondCallback` (Review Focus 5).
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(api): publish transcription requests and handle results`.

### Task 3.5: Dostarczanie callbacków (HMAC, resilience, usuwanie)

**Files:**
- Create: `src/TranscriptService.Api/Callbacks/CallbackDispatcher.cs` (BackgroundService), `CallbackSigner.cs`, `CallbackPayload.cs`
- Test: `tests/TranscriptService.Api.Tests/Callbacks/CallbackSignerTests.cs`, `CallbackDispatcherTests.cs` (WireMock.Net)

**Interfaces — Produces:**
```csharp
public static class CallbackSigner
{ public const string SignatureHeader = "X-Signature"; public const string JobIdHeader = "X-Transcription-Id";
  public static string Sign(ReadOnlySpan<byte> body, string secret);            // "sha256=" + lowercase hex HMAC-SHA256
  public static bool Verify(ReadOnlySpan<byte> body, string signature, string secret); }  // constant-time
// CallbackPayload == TranscriptionResponse (ten sam kształt co GET)
```
`CallbackSigner` żyje w **Contracts** (używa go też Demo.Web) — przenieść do `src/TranscriptService.Contracts/Callbacks/CallbackSigner.cs`. `HttpClient` nazwany `callbacks` z `AddStandardResilienceHandler` (retry 5 z backoff wykładniczym, timeout próby 10 s). Sukces (2xx) → usuń plik i zadanie. Porażka po ponowieniach → `Status = CallbackFailed` (sprząta retencja).

- [ ] **Step 1: Testy**
  - `Sign_KnownVector_MatchesExpectedHex` (body `{"a":1}`, secret `secret` → oczekiwany hex policzony w teście przez `HMACSHA256`); `Verify_TamperedBody_ReturnsFalse`.
  - `Dispatch_Success_PostsSignedPayload_DeletesJobAndFile`.
  - `Dispatch_TransientFailureThenSuccess_Delivers`.
  - `Dispatch_EndpointDown_MarksCallbackFailedAfterRetries` (Review Focus 3).
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(api): deliver signed callbacks with retries`.

### Task 3.6: Retencja

**Files:** Create `src/TranscriptService.Api/Retention/RetentionCleanupService.cs`, `RetentionCleaner.cs`; Test `tests/TranscriptService.Api.Tests/Retention/RetentionCleanerTests.cs`

**Interfaces:** `RetentionCleaner.CleanAsync(CancellationToken) : Task<int>` (liczba usuniętych); usługa woła go co `CleanupInterval` (`PeriodicTimer`).
Reguła: usuń zadania w stanie `Completed`/`Failed`/`CallbackFailed` z `FinishedAt + ResultTtl < now` (plik + rekord). Zadania `Queued` starsze niż `MaxAudioDuration × 4` → `Failed` („timed out”).

- [ ] **Step 1: Testy** (`FakeTimeProvider`): `Clean_RemovesExpiredFinishedJobs_KeepsFresh`; `Clean_StaleQueuedJob_MarkedFailed`.
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(api): add retention cleanup`.

### Task 3.7: Test integracyjny przepływu + Dockerfile API

**Files:**
- Create: `tests/TranscriptService.IntegrationTests/TranscriptionFlowTests.cs`, `FakeEngineWorker.cs`, `src/TranscriptService.Api/Dockerfile`

`FakeEngineWorker`: host z `AddTranscriptionWorker<FakeTranscriber, EngineOptions>` (FakeTranscriber zwraca „hello” i jeden segment) — sprawdza prawdziwy kontrakt API ↔ worker przez RabbitMQ z Testcontainers.

Dockerfile API: stage `build` (`mcr.microsoft.com/dotnet/sdk:10.0`, COPY tylko `*.csproj` + `Directory.*.props` → `dotnet restore` → COPY reszty → `dotnet publish -c Release -o /app --no-restore`), stage `final` (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, `USER $APP_UID`, `EXPOSE 8080`). Healthcheck w compose (chiseled nie ma powłoki): mały endpoint + `HEALTHCHECK NONE`, sprawdzanie przez compose z obrazu workera albo `wget` z kontenera pomocniczego — wykonawca wybiera i opisuje w ADR.

- [ ] **Step 1: Test** `Post_ThenWorkerCompletes_CallbackReceivedWithSignature_AndJobDeleted` (WireMock jako odbiorca callbacku; czas ≤ 30 s).
- [ ] **Step 2:** `dotnet test tests/TranscriptService.IntegrationTests` → PASS (wymaga Dockera; w CI ubuntu-latest działa).
- [ ] **Step 3:** `docker build -f src/TranscriptService.Api/Dockerfile .` → sukces; `docker run --rm <img> id -u` niemożliwe w chiseled — zamiast tego `docker inspect` pokazuje `User` ≠ root.
- [ ] **Step 4:** Commit `test: add end-to-end flow test; build: add API Dockerfile`.

**→ Proces fazy.**

---

## Faza 4 — Worker Whisper.net + wdrożenie (`phase-4-whispernet`)

### Task 4.1: `WhisperNetTranscriber`

**Files:** Create `src/TranscriptService.Worker.WhisperNet/Program.cs`, `WhisperNetTranscriber.cs`, `WhisperNetOptions.cs`, `appsettings.json`; Test `tests/TranscriptService.Engines.Tests/WhisperNetTranscriberTests.cs`, `ModelRequirement.cs`

**Interfaces:** `WhisperNetTranscriber : ITranscriber` (EngineName `"whispernet"`); `WhisperNetOptions : EngineOptions { int Threads = Environment.ProcessorCount; }`.
`InitializeAsync`: `RuntimeOptions.RuntimeLibraryOrder` = `[Cuda]` dla `Device=Cuda` (bez cichego spadku na CPU — brak CUDA = wyjątek), `[Cpu]` dla CPU; `WhisperFactory.FromPath` + jeden `WhisperProcessor` na żądanie z fabryki (procesor nie jest thread-safe; fabryka i model współdzielone). `TranscribeAsync`: `processor.ProcessAsync(float[] samples, ct)`, segmenty → `TranscriptSegment`. Pakiety: `Whisper.net`, `Whisper.net.Runtime`, `Whisper.net.Runtime.Cuda.Linux`.

`ModelRequirement.PathOrSkip(string envVar, string relative)` → ścieżka albo `Assert.Skip`.

- [ ] **Step 1: Testy** (`MODELS_DIR`): `Transcribe_Sample8kHzMuLaw_ReturnsPolishText` — tekst zawiera „zasilania” (case-insensitive), segmenty niepuste i rosnące; `Initialize_MissingModel_Throws`.
- [ ] **Step 2–4:** FAIL → implementacja → PASS lokalnie z `MODELS_DIR=D:\GIT\TranscriptService\models` (ggml).
- [ ] **Step 5:** Commit `feat(whispernet): add Whisper.net engine worker`.

### Task 4.2: Dockerfile workera (CPU i CUDA)

**Files:** Create `src/TranscriptService.Worker.WhisperNet/Dockerfile`

Stages: `build` (SDK 10.0, restore z cache), `runtime-cpu` (`mcr.microsoft.com/dotnet/aspnet:10.0-noble`, nonroot `app`), `runtime-cuda` (`nvidia/cuda:<wersja zgodna z Whisper.net.Runtime.Cuda.Linux 1.9.1 i sm_120>-runtime-ubuntu24.04` + instalacja ASP.NET Core runtime 10 z `dotnet-install.sh`/pakietu, użytkownik nonroot). Wybór: `docker build --target runtime-cpu|runtime-cuda`.

- [ ] **Step 1:** `docker build --target runtime-cpu …` i `--target runtime-cuda …` → sukces.
- [ ] **Step 2:** Commit `build(whispernet): add CPU and CUDA images`.

### Task 4.3: Compose, pobieranie modeli, CI obrazów

**Files:** Create `deploy/docker-compose.yml`, `deploy/docker-compose.gpu.yml`, `deploy/.env.example`, `deploy/models/manifest.json`, `deploy/models/fetch-models.sh`, ADR `docs/adr/0001-*.md`…; Modify `.github/workflows/ci.yml`

- `manifest.json`: per silnik lista plików `{ url, sha256, target }` — Whisper.net `ggml-large-v3-turbo-q5_0.bin` (HF `ggerganov/whisper.cpp`), FasterWhisper `large-v3-turbo` CT2 (pliki `model.bin`, `config.json`, `tokenizer.json`, `vocabulary.json`, `preprocessor_config.json`), Vosk `vosk-model-small-pl-0.22.zip` (+ rozpakowanie). SHA-256 policzone przy tworzeniu manifestu.
- `fetch-models.sh ENGINE`: POSIX sh (`curl`, `sha256sum`, `unzip`), pomija plik z poprawną sumą, kończy ≠0 przy złej sumie. Obraz `models-init`: `alpine` + te narzędzia.
- Compose: `rabbitmq` (`rabbitmq:4-management`, healthcheck, `consumer_timeout` w `rabbitmq.conf` = 2 h), `api` (wolumeny `audio-data:/data`), `models-init-<engine>` + `worker-<engine>` w profilu (`depends_on: models-init… service_completed_successfully`, `rabbitmq service_healthy`), wolumen `models` (read-only w workerze). `docker-compose.gpu.yml`: `build.target: runtime-cuda`, `Engine__Device=Cuda`, `deploy.resources.reservations.devices: [{driver: nvidia, count: 1, capabilities: [gpu]}]`.
- CI: job `docker` z matrix (api, worker-whispernet cpu/cuda, …) — `docker/build-push-action` bez push, cache GHA.
- ADR: 0001 EasyNetQ i topologia kolejek, 0002 SQLite tylko w API, 0003 audio przez wolumen, 0004 podpisane callbacki i retencja, 0005 obrazy chiseled/CUDA i healthchecki.

- [ ] **Step 1:** `docker compose -f deploy/docker-compose.yml --profile whispernet up --build` → `api` i `worker-whispernet` healthy.
- [ ] **Step 2:** Ręczny E2E: `curl -F audio=@D:/GIT/_triton/zapytanie-0002.wav` (8 kHz) → `202`; `GET` → `Completed` z polskim tekstem. To samo z `-f docker-compose.gpu.yml` na RTX 5070 Ti; `nvidia-smi` pokazuje proces. (Wymaga włączonego Docker Desktop z GPU.)
- [ ] **Step 3:** Commit `build: add compose deployment, model fetching and image CI`.

**→ Proces fazy.**

---

## Faza 5 — Worker FasterWhisper.NET (`phase-5-fasterwhisper`)

### Task 5.1: Spike wykonalności (wynik raportowany użytkownikowi)

- [ ] Minimalny projekt w `spikes/` (nie w solucji): `WhisperModel.LoadAsync(<katalog CT2>, "cpu"|"cuda", …)` + `Transcribe(float[])` dla `sample.wav`, uruchomiony w kontenerze Linux CPU i CUDA na RTX 5070 Ti.
- [ ] Znane ryzyko: README biblioteki wymaga **CUDA 12.4 + cuDNN 8.9**, a Blackwell (sm_120) potrzebuje CUDA ≥ 12.8 — możliwy brak kerneli GPU. Sprawdzić też, czy katalog modelu z `models/faster-whisper-large-v3-turbo-ct2` się ładuje.
- [ ] Wynik (CPU ok / CUDA ok / porażka + log) → wiadomość do użytkownika. **Porażka CPU = stop fazy.** Porażka tylko CUDA = pytanie do użytkownika (CPU-only albo wstrzymanie). Spike usunięty przed PR.

### Task 5.2: `FasterWhisperTranscriber` + Dockerfile + profil compose

**Files:** Create `src/TranscriptService.Worker.FasterWhisper/{Program.cs, FasterWhisperTranscriber.cs, FasterWhisperOptions.cs, Dockerfile}`; Test `tests/TranscriptService.Engines.Tests/FasterWhisperTranscriberTests.cs`; Modify `deploy/docker-compose*.yml`, `ci.yml`

**Interfaces:** `FasterWhisperTranscriber : ITranscriber` (EngineName `"fasterwhisper"`); `FasterWhisperOptions : EngineOptions { string ComputeType = "default"; int BeamSize = 1; bool VadEnabled = true; }` (beam 1 i VAD — wzór ToperTriton pod telefonię). `Transcribe` jest synchroniczne → `Task.Run` z respektowaniem `ct` między segmentami. Runtime CUDA: baza z CUDA/cuDNN wymaganymi przez wynik spike'u.

- [ ] **Step 1: Testy** analogiczne do 4.1 (`Transcribe_Sample8kHzMuLaw_ReturnsPolishText`, `Initialize_MissingModel_Throws`).
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** `docker compose --profile fasterwhisper up` + ręczny E2E z plikiem 8 kHz.
- [ ] **Step 6:** Commit `feat(fasterwhisper): add FasterWhisper.NET engine worker`.

**→ Proces fazy.**

---

## Faza 6 — Worker Vosk (`phase-6-vosk`)

### Task 6.1: `VoskTranscriber` + Dockerfile (tylko CPU) + profil compose

**Files:** Create `src/TranscriptService.Worker.Vosk/{Program.cs, VoskTranscriber.cs, VoskOptions.cs, Dockerfile}`; Test `tests/TranscriptService.Engines.Tests/VoskTranscriberTests.cs`; Modify compose, CI

**Interfaces:** `VoskTranscriber : ITranscriber` (EngineName `"vosk"`); `VoskOptions : EngineOptions { [Required] string ModelLanguage = "pl"; }` + walidacja `Device == Cpu` (CUDA → błąd startu z czytelnym komunikatem). `Model` ładowany raz; `VoskRecognizer` per żądanie (`using`), `SetWords(true)`, `AcceptWaveform(short[] …)` z próbek 16 kHz (konwersja float→short), `FinalResult()` → słowa jako segmenty. Język żądania ≠ `ModelLanguage` → `NotSupportedException` (handler publikuje `Failed`). `Vosk.Vosk.SetLogLevel(-1)`, logi przez ILogger.

- [ ] **Step 1: Testy:** `Transcribe_Sample8kHzMuLaw_ReturnsWords` (słowa z rosnącymi czasami); `Transcribe_OtherLanguage_ThrowsNotSupported`; `Options_CudaDevice_FailsValidation`.
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** `docker compose --profile vosk up` + E2E 8 kHz.
- [ ] **Step 6:** Commit `feat(vosk): add Vosk engine worker`.

**→ Proces fazy.**

---

## Faza 7 — Demo Blazor (`phase-7-demo`)

### Task 7.1: Backend demo — klient API, hub SignalR, endpoint callbacku

**Files:** Create `src/TranscriptService.Demo.Web/{Program.cs, TranscriptApi/TranscriptApiClient.cs, TranscriptApi/TranscriptApiOptions.cs, Hubs/TranscriptionHub.cs, Hubs/PendingJobs.cs, Callbacks/CallbackEndpoint.cs, Uploads/UtteranceEndpoint.cs}`; Test `tests/TranscriptService.Demo.Web.Tests/CallbackEndpointTests.cs`, `UtteranceEndpointTests.cs`

**Interfaces:**
```csharp
public sealed class TranscriptApiClient(HttpClient)        // typed client, X-API-KEY from options
{ Task<Guid> SubmitAsync(Stream wav, string fileName, string language, Uri callbackUrl, CancellationToken ct); }
public sealed class PendingJobs { void Register(Guid jobId, string connectionId); bool TryTake(Guid jobId, out string connectionId); }
public interface ITranscriptionClient { Task TranscriptReady(TranscriptionResponse r); Task TranscriptFailed(Guid id, string reason); }
public sealed class TranscriptionHub : Hub<ITranscriptionClient>;
```
`POST /utterances` (multipart `audio` + `connectionId`) → `SubmitAsync` z `callbackUrl = {PublicCallbackBaseUrl}/callbacks/transcriptions` → `PendingJobs.Register`. `POST /callbacks/transcriptions`: weryfikacja `CallbackSigner.Verify` (401 przy złym podpisie), `TryTake` → `Clients.Client(id).TranscriptReady(...)`; zawsze `200` dla poprawnego podpisu (także nieznany job — API nie ponawia). `TranscriptionResponse` pochodzi z Contracts (Task 3.3) — Demo.Web nie referuje API.

- [ ] **Step 1: Testy:** `Callback_InvalidSignature_Returns401`; `Callback_ValidSignature_PushesToRegisteredConnection` (fałszywy `IHubContext`); `Callback_UnknownJob_Returns200`; `Utterance_SubmitsWithCallbackUrl_AndRegistersConnection` (WireMock jako API).
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Commit `feat(demo): add demo backend with signed callback and SignalR hub`.

### Task 7.2: Strona — mikrofon, cięcie na wypowiedzi, upload pliku

**Files:** Create `src/TranscriptService.Demo.Web/Components/Pages/Home.razor`, `Components/Utterance*.razor`, `wwwroot/js/recorder.js`, `Dockerfile`; Modify compose (`demo-web`, port 8081), README sekcja demo

`recorder.js`: `getUserMedia` → `AudioWorklet` → bufor float; detektor ciszy (RMS < próg przez 700 ms kończy wypowiedź; maks. 15 s wypowiedzi) → koduje WAV PCM16 **16 kHz mono** → `POST /utterances` z `connectionId`. Strona (render mode Interactive WebAssembly lub Server — wykonawca wybiera, `HubConnection` z `Microsoft.AspNetCore.SignalR.Client`): przycisk Start/Stop, lista wypowiedzi (stan „transkrybuję…” → tekst + silnik + czas przetwarzania), upload pliku WAV (np. nagranie 8 kHz).

- [ ] **Step 1:** Test bUnit `Home_TranscriptReady_RendersUtteranceText` (fałszywy hub client).
- [ ] **Step 2–4:** FAIL → implementacja → PASS.
- [ ] **Step 5:** Ręcznie: `docker compose --profile whispernet up`, `http://localhost:8081`, nagranie zdania po polsku → tekst pojawia się ≤ 3 s po końcu wypowiedzi (GPU); upload `zapytanie-0002.wav` → tekst.
- [ ] **Step 6:** Commit `feat(demo): add Blazor microphone demo page`.

**→ Proces fazy.**

---

## Faza 8 — dokumentacja i wykończenie (`phase-8-docs`)

### Task 8.1: README, CLAUDE.md, porządki

**Files:** Modify `README.md` (pełna przebudowa), `CLAUDE.md`; Delete pozostałości

README (EN): jedno zdanie o projekcie, diagram architektury (Mermaid), Quick start (`docker compose --profile whispernet up`, wariant GPU), tabela silników (CPU/GPU, model, segmenty vs słowa), przykład `curl` (POST → GET), callback z weryfikacją podpisu (snippet C#), konfiguracja (tabela zmiennych z `.env.example`), struktura repo, testy (`MODELS_DIR`), linki do ADR. Zrzut ekranu demo w `docs/images/`.

- [ ] **Step 1:** Każde polecenie z README wykonane na czysto (świeży klon, `docker compose … up`) — działa.
- [ ] **Step 2:** `CLAUDE.md` zaktualizowany do nowej architektury.
- [ ] **Step 3:** Commit `docs: rewrite README and project docs`.

**→ Proces fazy.**
