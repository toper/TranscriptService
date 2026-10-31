# TranscriptService — projekt wzorcowy (design)

Data: 2026-10-31 · Status: do akceptacji

## 1. Intencja

Repozytorium ma być **projektem do pokazania przy rekrutacji** na stanowisko .NET.
Tech lead albo rekruter techniczny otworzy je na GitHubie i w kilka minut oceni
rzemiosło autora:

- jak buduje **mikroserwisy** i jak komunikują się ze sobą,
- czy struktura katalogów i klas jest **przewidywalna** (bez chaosu, bez placeholderów),
- jakich rozwiązań .NET używa i czy **async/await** jest poprawny od wejścia po silnik,
- czy **obrazy Dockera** są zbudowane zgodnie z zalecanymi praktykami,
- czy **API** jest czytelne i dobrze opisane (Scalar).

Funkcjonalnie system ma **działać naprawdę** na nagraniach telefonicznych
(WAV 8 kHz, PCM16, mono lub stereo). Wzorcem przetwarzania audio jest ASR z
projektu ToperTriton (`asr/adapter.py`): przepróbkowanie 8→16 kHz, stereo→mono,
jawna odmowa dla nieobsługiwanych formatów zamiast cichego śmiecia.

Obecny kod (audyt 2026-10-31) nie spełnia żadnego z tych celów: żaden silnik nie
obsługuje 8 kHz, silniki ładowane są per żądanie (wyciek VRAM), Dockerfile są
nieaktualne. Projekt jest przebudowywany od nowa; z istniejącego kodu
przenosimy tylko to, co pasuje do nowego układu.

## 2. Cele

1. Transkrypcja nagrań telefonicznych przez asynchroniczne zadania (API → kolejka → worker).
2. Trzy silniki, **każdy jako osobna usługa** (osobny projekt i obraz Dockera):
   Whisper.net, FasterWhisper.NET, Vosk. Wdrożeniowiec wybiera silnik przy wdrożeniu.
3. Każdy silnik konfigurowalny na **CPU albo GPU (CUDA)** — decyzja wdrożeniowca
   w konfiguracji (Vosk: tylko CPU).
4. Wynik dostarczany **callbackiem** (HTTP POST) albo do pobrania przez krótki czas.
5. **Krótka retencja**: zadanie i nagranie usuwane zaraz po dostarczeniu wyniku.
6. **Strona demo** w Blazorze: nagrywanie mikrofonem, wypowiedzi transkrybowane
   na bieżąco przez to samo API, wynik wypychany przez SignalR.
7. Całość uruchamiana jednym `docker compose up` (z wyborem profilu silnika).

## 3. Non-goals

- Prawdziwy streaming słowo po słowie (WebSocket do silnika). Demo transkrybuje
  kolejne **wypowiedzi** z opóźnieniem ~1–3 s.
- Długie przechowywanie nagrań i historii zadań.
- Wielodostępność (tenanci, użytkownicy, role). Uwierzytelnienie = klucz API.
- Kubernetes/Helm, OpenTelemetry, rozproszone śledzenie.
- Diaryzacja (rozpoznawanie mówców).
- Kierowanie zadania do konkretnego silnika z poziomu klienta API.

## 4. Anty-cele (porażka, nawet jeśli działa)

- Kod „demo-jakości”: `Console.WriteLine`, `new` zamiast DI, puste placeholdery,
  `async void`, `.Result`/`.Wait()`, połykane wyjątki.
- Usługa raportująca gotowość, a padająca na pierwszym żądaniu (zły model, brak
  CUDA) — błąd konfiguracji ma wywrócić **start**.
- Cicha konwersja albo cicha porażka na nieobsługiwanym audio.
- Obraz Dockera działający jako root, z SDK w warstwie runtime, z modelem wpieczonym w obraz.
- Testy, które przy braku modelu raportują porażkę zamiast pominięcia.

## 5. Architektura

```
            ┌──────────────┐  HTTP (klucz API)   ┌──────────────────────┐
 klient ───▶│ Transcript   │◀────────────────────│ Demo.Web (Blazor)    │
  (curl)    │ API          │── callback POST ───▶│ + hub SignalR        │──▶ przeglądarka
            │ SQLite       │                     └──────────────────────┘
            └──────┬───────┘
     komenda       │ ▲ zdarzenie wyniku           wolumen /data/audio
   (EasyNetQ)      ▼ │                            (API zapisuje, worker czyta)
            ┌──────────────┐
            │  RabbitMQ    │
            └──────┬───────┘
                   ▼
   ┌───────────────┬────────────────┬───────────────┐
   │ Worker.       │ Worker.        │ Worker.Vosk   │   ← wdrożeniowiec
   │ WhisperNet    │ FasterWhisper  │ (CPU)         │     uruchamia jeden
   │ (CPU|CUDA)    │ (CPU|CUDA)     │               │
   └───────────────┴────────────────┴───────────────┘
            modele: wolumen /models (read-only)
```

### 5.1 Cykl życia zadania

1. Klient: `POST /api/v1/transcriptions` (multipart: `audio`, opcjonalnie
   `language`, `callbackUrl`). API waliduje plik, zapisuje go w `/data/audio/{jobId}.wav`,
   tworzy zadanie w SQLite (`Queued`), publikuje `TranscribeAudio`, zwraca
   `202 Accepted` + nagłówek `Location` + `jobId`.
2. Worker konsumuje komendę, przygotowuje audio, transkrybuje, publikuje
   `TranscriptionCompleted` albo `TranscriptionFailed`. Worker **nie** ma dostępu do bazy.
3. API konsumuje zdarzenie, zapisuje wynik/błąd (`Completed`/`Failed`).
4. Jeśli jest `callbackUrl`: API wysyła POST z wynikiem, z ponowieniami i
   podpisem HMAC. **Po udanym callbacku zadanie i nagranie są usuwane.**
5. Bez callbacku: wynik dostępny przez `GET /api/v1/transcriptions/{id}` przez
   konfigurowalny czas (domyślnie 15 min). Usługa sprzątająca w tle usuwa
   przeterminowane zadania, nagrania i zadania z nieudanym callbackiem.
6. Nagranie usuwane jest także po stanie `Failed` (wraz z zadaniem, wg TTL).

### 5.2 Własność danych

- **SQLite należy wyłącznie do API.** Jeden proces pisze, brak problemów ze
  współbieżnym zapisem z wielu kontenerów.
- Nagrania: współdzielony wolumen. API zapisuje i usuwa; worker tylko czyta.
  Audio **nie** jedzie w treści wiadomości RabbitMQ.

## 6. Komponenty

Układ repozytorium:

```
src/
  TranscriptService.Contracts/        wiadomości EasyNetQ (rekordy), wspólne DTO wyniku
  TranscriptService.Api/              ASP.NET Core minimal API, SQLite, callbacki
  TranscriptService.Worker.Core/      host workera: konsumpcja, przygotowanie audio, ITranscriber
  TranscriptService.Worker.WhisperNet/
  TranscriptService.Worker.FasterWhisper/
  TranscriptService.Worker.Vosk/
  TranscriptService.Demo.Web/         Blazor + hub SignalR + endpoint callbacku
tests/
  …Tests per projekt (unit) + TranscriptService.IntegrationTests
deploy/
  docker-compose.yml (+ override GPU), .env.example
docs/
  specs/, plans/, adr/ (krótkie ADR dla kluczowych decyzji)
```

Wspólne: `Directory.Build.props` (net10.0, nullable, `TreatWarningsAsErrors`,
analizatory), `Directory.Packages.props` (Central Package Management),
`.editorconfig`, plik rozwiązania `.slnx`.

### 6.1 Contracts

- `TranscribeAudio(Guid JobId, string AudioPath, string Language)` — komenda.
- `TranscriptionCompleted(Guid JobId, string Engine, string FullText,
  IReadOnlyList<TranscriptSegment> Segments, TimeSpan AudioDuration, TimeSpan ProcessingTime)`.
- `TranscriptionFailed(Guid JobId, string Engine, string Reason)`.
- `TranscriptSegment(string Text, TimeSpan Start, TimeSpan End)` — dla Whisperów
  segment = fraza, dla Vosk = słowo. **Pole i dokumentacja mówią to jawnie**
  (koniec z polem `Word` zawierającym zdania).

Transport: **EasyNetQ** (decyzja użytkownika). Komenda idzie do jednej kolejki
(konkurujący konsumenci), zdarzenia przez pub/sub do API.

### 6.2 Transcript API

- **Minimal API** z grupami endpointów, wersjonowanie `/api/v1` (Asp.Versioning).
- **Scalar** zamiast Swaggera (decyzja użytkownika): opisy, przykłady żądań i
  odpowiedzi, schemat bezpieczeństwa klucza API. Swashbuckle usunięty.
- Endpointy:
  - `POST /api/v1/transcriptions` → `202` | `400` (ProblemDetails) | `413`
  - `GET  /api/v1/transcriptions/{id}` → `200` (status + wynik) | `404`
  - `DELETE /api/v1/transcriptions/{id}` → `204` | `404`
  - `GET /health/live`, `GET /health/ready` (ready = SQLite + RabbitMQ).
- Endpoint `transcript/path` (czytanie dowolnego pliku z serwera) **usunięty**.
- Błędy jako **ProblemDetails** (RFC 9457), błędy serwera = 5xx, nie 400.
- Walidacja wejścia: limit rozmiaru, poprawny nagłówek WAV, obsługiwany format
  (PCM16, G.711 μ-law/A-law), 1–2 kanały, limit długości nagrania.
  Odmowa = `400` z czytelnym powodem.
- `callbackUrl`: tylko http/https, dostarczany z ponowieniami
  (`Microsoft.Extensions.Http.Resilience`), podpis `X-Signature: sha256=…`
  (HMAC tajnym kluczem współdzielonym), nagłówek z `jobId`.
- Uwierzytelnienie: klucz API w nagłówku `X-API-KEY` (jak dotąd).
- Dostęp do SQLite: EF Core + migracje.
- Usługa sprzątająca: `BackgroundService` z `PeriodicTimer`.

### 6.3 Worker.Core (wspólny host silników)

- Generic Host (Worker Service), subskrypcja EasyNetQ, prefetch/równoległość
  konfigurowalna (domyślnie 1 na GPU).
- **Przygotowanie audio** (`AudioPreprocessor`): WAV → `float[]` 16 kHz mono.
  PCM16 i G.711 μ-law/A-law; stereo → mono (uśrednienie); przepróbkowanie
  dobrej jakości (nie liniowe). Wzór: ToperTriton `asr/adapter.py`.
  Nieobsługiwany format → `TranscriptionFailed` z czytelnym powodem.
- `ITranscriber`: `Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio,
  string language, CancellationToken ct)`. Implementacja jest **singletonem**,
  model ładowany **raz przy starcie**, `IAsyncDisposable`.
- Ładowanie modelu w starcie hosta; błąd (brak pliku, brak CUDA przy
  `Device=Cuda`) wywraca start. `/health/ready` = model załadowany + RabbitMQ.
- Opcje: `Engine:ModelPath`, `Engine:Device` (`Cpu`|`Cuda`), `Engine:Language`
  (domyślnie `pl`), opcje specyficzne silnika — `IOptions` z walidacją przy starcie.
- Logowanie strukturalne NLog (bez `Console.WriteLine`).

### 6.4 Workery silników

| Usługa | Biblioteka | Model | CPU | GPU |
|---|---|---|---|---|
| Worker.WhisperNet | Whisper.net | GGML `.bin` | tak | CUDA |
| Worker.FasterWhisper | FasterWhisper.NET (+ `.Gpu`) | katalog CTranslate2 | tak | CUDA |
| Worker.Vosk | Vosk | katalog Vosk | tak | — |

- Każdy worker: tylko implementacja `ITranscriber` + rejestracja + Dockerfile.
- Vosk: model jest jednojęzyczny; żądanie z innym językiem niż model →
  `TranscriptionFailed` (bez udawania obsługi).
- FasterWhisper.NET to młoda biblioteka (6 gwiazdek, 10.2026). Jeśli nie zadziała
  na Linuksie z CUDA — zgłosić użytkownikowi, nie obchodzić po cichu.

### 6.5 Demo.Web (Blazor)

- Blazor Web App. Strona: przycisk „nagrywaj”, lista wypowiedzi z tekstem
  pojawiającym się na bieżąco; dodatkowo upload pliku (np. nagrania rozmowy).
- Przeglądarka nagrywa mikrofon (JS interop, AudioWorklet), tnie na
  wypowiedzi po ciszy (prosty detektor energii) albo co N sekund, koduje WAV
  16 kHz mono, wysyła do backendu Demo.Web.
- Backend Demo.Web woła Transcript API (typowany `HttpClient`) z
  `callbackUrl` wskazującym na siebie; trzyma mapę `jobId → połączenie SignalR`.
- Endpoint callbacku weryfikuje podpis HMAC i wypycha wynik przez **jawny hub
  SignalR** (`TranscriptionHub`) do właściwego klienta (decyzja użytkownika).

### 6.6 Docker i wdrożenie

- Jeden Dockerfile na usługę, multi-stage: `restore` w osobnej warstwie
  (cache), `publish` w SDK, runtime bez SDK.
- API i Demo.Web: obraz `aspnet:10.0` w wariancie chiseled (nonroot).
- Workery: wariant CPU (obraz runtime .NET) i CUDA (baza NVIDIA CUDA runtime +
  .NET runtime), wybór przez `--build-arg`/osobny target. Użytkownik nonroot.
- `.dockerignore`, `HEALTHCHECK`/healthcheck w compose, modele jako wolumen read-only.
- `deploy/docker-compose.yml`: `rabbitmq`, `api`, `demo-web` + workery w
  profilach `whispernet`, `fasterwhisper`, `vosk`; override `docker-compose.gpu.yml`
  z rezerwacją GPU. `.env.example` z opisem zmiennych.

## 7. Jakość i proces

- **Testy**: xUnit; unit dla przygotowania audio (fixture 8 kHz generowany z
  `sample.wav` — repo jest publiczne, nagrań z ToperTriton nie commitujemy),
  dla logiki zadań, callbacków (podpis, retencja) i walidacji; integracyjne:
  `WebApplicationFactory` + Testcontainers (RabbitMQ). Testy silników z modelem
  uruchamiane, gdy model jest dostępny (`MODELS_DIR`); inaczej **pominięte**.
- **CI**: GitHub Actions — build, testy, budowa obrazów Dockera dla każdego PR.
- **Proces**: faza = gałąź → PR na GitHubie → code-review + simplify → merge.
  Commity i opisy PR bez atrybucji narzędzi.
- **README**: diagram architektury, szybki start (`docker compose --profile …`),
  opis API, wybór silnika i CPU/GPU, decyzje (ADR).

## 7a. Ustalenia po przeglądzie wykonawcy

- **Modele (decyzja użytkownika):** usługa `models-init` w compose pobiera
  domyślny model wybranego silnika do wolumenu `/models` (przypięty URL + suma
  SHA-256, pobranie pomijane, gdy plik już jest); worker startuje po jej
  sukcesie (`depends_on: condition: service_completed_successfully`).
  Domyślne modele (nadpisywalne zmienną): Whisper.net — `ggml-large-v3-turbo`
  w kwantyzacji q5_0 (rozsądny na CPU, szybki na GPU); FasterWhisper —
  `large-v3-turbo` w formacie CTranslate2; Vosk — `vosk-model-small-pl-0.22`.
- **Demo tylko lokalnie (decyzja użytkownika):** `localhost`, bez TLS,
  rate limitingu i ochrony przed nadużyciami. README mówi to wprost.
- **Limity (decyzja użytkownika):** do ~30 min nagrania / 100 MB, oba
  konfigurowalne. Worker trzyma całe audio w pamięci (`float[]`); timeout
  potwierdzenia RabbitMQ (`consumer_timeout`) ustawiony z zapasem na najdłuższą
  transkrypcję na CPU.
- **Język repozytorium (decyzja użytkownika):** kod, komentarze, README,
  opisy API (Scalar), commity i PR **po angielsku**. Specyfikacja i plan mogą
  zostać po polsku.
- **Priorytet (moja decyzja):** komenda niesie priorytet RabbitMQ — krótkie
  nagrania (≤ 30 s, np. wypowiedzi z demo) wyżej niż długie rozmowy, żeby
  demo nie czekało minutami za plikiem 30-minutowym.
- **FasterWhisper.NET (moja decyzja):** faza silnika zaczyna się od spike'u
  wykonalności na Linuksie (CPU i CUDA). Worker.Core nie zakłada niczego o tej
  bibliotece. Porażka spike'u = wstrzymanie tej fazy i zgłoszenie użytkownikowi;
  pozostałe silniki idą dalej.
- **Środowisko GPU:** lokalna karta RTX 5070 Ti (Blackwell, sterownik 617)
  przez Docker Desktop/WSL2. Obrazy CUDA muszą wspierać sm_120. CI buduje
  obrazy CUDA, ale ich nie uruchamia; weryfikacja GPU jest ręczna, lokalna.

## 8. Decyzje użytkownika

API + worker przez RabbitMQ; **EasyNetQ** (nie MassTransit); trzy silniki jako
osobne usługi, wybór przy wdrożeniu; CPU/GPU z konfiguracji; **SQLite**;
callback + krótka retencja; strona **Blazor** z **SignalR**; **Scalar**;
nowa gałąź od `master`, stare zmiany odrzucone.

## 9. Moje decyzje (do sprawdzenia)

- Worker nie dotyka bazy; wynik wraca zdarzeniem do API.
- Audio przez współdzielony wolumen, nie w wiadomości.
- Podpis HMAC callbacku; TTL wyniku bez callbacku 15 min.
- EF Core dla SQLite (rozpoznawalny dla rekrutera; alternatywa: Linq2DB).
- NLog zostaje; bez OpenTelemetry.
- Minimal API zamiast kontrolerów.
- Obsługa G.711 μ-law/A-law (częsty format nagrań rozmów).
- Usunięcie endpointu `transcript/path`.

## 10. Pozostawione wykonawcy

Dokładne wersje pakietów i obrazów bazowych (CUDA/cuDNN zgodne z
Whisper.net i CTranslate2 4.7), biblioteka resamplingu, nazwy kolejek, kształt
JSON statusu zadania, podział na fazy w planie.
