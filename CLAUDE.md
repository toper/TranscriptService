# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TranscriptService is a .NET 10.0 ASP.NET Core Web API that transcribes audio files using three offline speech recognition engines: Whisper.net, FastWhisper (CTranslate2), and Vosk. The application provides REST endpoints for transcribing audio files with support for GPU acceleration via CUDA.

## Solution Structure

The solution consists of 6 projects:

- **TranscriptService.API** - ASP.NET Core Web API (entry point in `Program.cs`)
- **TranscriptService.Models** - Shared models and interfaces
  - `TranscriptionResult` - Output model with timestamps (segments for Whisper/FastWhisper, words for Vosk)
  - `ITranscriber` - Interface implemented by all transcription engines
- **TranscriptService.Whisper** - Local Whisper.net implementation with CUDA support (GGML format)
- **TranscriptService.FastWhisper** - Faster-whisper implementation using CTranslate2 (4x faster)
- **TranscriptService.VoskAPI** - Vosk (offline) transcription implementation
- **TranscriptService.Tests** - Unit and integration tests

### Key Architecture

The project uses a **strategy pattern** via the `ITranscriber` interface. Both transcription engines implement the same interface:

```csharp
Task<TranscriptionResult> TranscribeAsync(string audioFilePath)
```

The main application (`TranscriptService.API/Program.cs`) provides REST endpoints for transcription. The `TranscriptProvider` selects the appropriate transcriber based on the `engine` parameter in API requests.

## Building and Running

### Build the solution
```bash
dotnet build TranscriptService.sln
```

### Run the API
```bash
dotnet run --project TranscriptService.API
```

The API will start on:
- HTTP: http://localhost:8080
- HTTPS: https://localhost:8443

### Configuration
Update `appsettings.json` or `appsettings.Development.json`:
```json
{
  "AppSettings": {
    "ApiKey": "your-api-key",
    "WhisperModelPath": "D:\\Models\\ggml-base.bin",
    "FastWhisperModelPath": "D:\\Models\\faster-whisper-large-v3-turbo-ct2",
    "FastWhisperExecutablePath": "whisper-ctranslate2",
    "VoskModelPath": "D:\\Models\\vosk-model-small-en-us-0.15"
  }
}
```

### Build for release
```bash
dotnet build TranscriptService.sln -c Release
```

### Publish standalone executable
```bash
dotnet publish TranscriptService.API/TranscriptService.csproj -c Release -o ./publish
```

## Docker Support

The API project includes a Dockerfile for containerization:

```bash
docker build -t transcriptservice -f TranscriptService.API/Dockerfile .
```

## Testing

The solution includes a test project:
- `TranscriptService.Tests` - Unit and integration tests with real audio file testing

Run tests:
```bash
dotnet test TranscriptService.sln
```

## Dependencies

- **.NET 10.0** - Target framework for all projects
- **Newtonsoft.Json 13.0.4** - JSON serialization (used across all projects)
- **Whisper.net 1.9.0** - Local Whisper speech recognition (Whisper project)
- **Whisper.net.Runtime.Cuda.Windows 1.8.1** - CUDA runtime support for GPU acceleration
- **Soenneker.Libraries.Whisper.CTranslate 4.0.387** - Bundles whisper_ctranslate2.exe for FastWhisper engine
- **Vosk 0.3.38** - Offline speech recognition (VoskAPI project only)
- **NLog** - Logging framework
- **FluentValidation** - Request validation
- **Asp.Versioning** - API versioning
- **Swashbuckle.AspNetCore** - Swagger/OpenAPI documentation
- **Microsoft.VisualStudio.Azure.Containers.Tools.Targets** - Docker support

## Key Implementation Details

### Whisper Implementation (GGML)
- Uses local Whisper.net library for offline transcription
- Supports CUDA 13.0 GPU acceleration for improved performance
- Requires GGML format models (.bin file) - downloadable from Hugging Face
- Configurable language support (default: Polish)
- Thread pooling using all available CPU cores
- Lazy initialization of WhisperProcessor for efficient resource usage
- Implements IDisposable for proper resource cleanup
- **Returns segments (sentences/phrases) with timestamps** - NOT individual words
- See `WHISPER_LOCAL_SETUP.md` for detailed setup instructions

### FastWhisper Implementation (CTranslate2) ⭐
- **4x faster** than Whisper.net with the same accuracy
- Uses NuGet package `Soenneker.Libraries.Whisper.CTranslate` which bundles `whisper_ctranslate2.exe`
- Executable automatically included in `Resources/` folder after build
- No manual installation required - everything bundled via NuGet
- Requires CTranslate2 format models (directory, not single file)
- Supports CUDA GPU acceleration
- Lower memory consumption (-30%)
- Ideal for production environments requiring high performance
- **Returns segments (sentences/phrases) with timestamps** - NOT individual words
- Model download: https://huggingface.co/Systran/faster-whisper-large-v3-turbo-ct2
- NuGet package: https://www.nuget.org/packages/Soenneker.Libraries.Whisper.CTranslate

### Vosk Implementation
- Uses local Vosk models (requires model path)
- Processes audio in 4096-byte chunks
- Expects 16kHz sample rate
- **Returns individual words with timestamps** - high word-level precision

### Output Format
Both engines produce a `TranscriptionResult` containing:
- `FullText` - Complete transcription as a single string
- `Transcription` - List of `WordTimestamp` objects with `Word`, `StartTime`, `EndTime`
  - **Important:** Despite the name, `WordTimestamp.Word` contains:
    - Individual words for Vosk engine
    - Segments (sentences/phrases) for Whisper engine

The API returns JSON responses with:
- `FileName` - Name of the transcribed file
- `FullText` - Complete transcription
- `Words` - Array of timestamps (individual words for Vosk, segments for Whisper)
- `Engine` - Engine used for transcription ("whisper" or "vosk")
- `ProcessedAt` - UTC timestamp of processing

## Working with the Codebase

### Adding a new transcription engine
1. Create a new project: `TranscriptService.YourEngine`
2. Reference `TranscriptService.Models`
3. Implement `ITranscriber` interface
4. Add project reference in `TranscriptService.API`
5. Update `TranscriptProvider.CreateTranscriber()` to support the new engine name
6. Add necessary configuration to `appsettings.json`

### API Endpoints

#### POST /api/v1/transcript/file
Upload and transcribe an audio file
```bash
# Using Whisper.net (GGML)
curl -X POST "http://localhost:8080/api/v1/transcript/file" \
  -H "X-API-KEY: your-api-key" \
  -F "audioFile=@test.wav" \
  -F "engine=whisper"

# Using FastWhisper (CTranslate2) - 4x faster!
curl -X POST "http://localhost:8080/api/v1/transcript/file" \
  -H "X-API-KEY: your-api-key" \
  -F "audioFile=@test.wav" \
  -F "engine=fastwhisper"

# Using Vosk
curl -X POST "http://localhost:8080/api/v1/transcript/file" \
  -H "X-API-KEY: your-api-key" \
  -F "audioFile=@test.wav" \
  -F "engine=vosk"
```

#### POST /api/v1/transcript/path
Transcribe an audio file from server path
```bash
curl -X POST "http://localhost:8080/api/v1/transcript/path" \
  -H "X-API-KEY: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"filePath": "/path/to/audio.wav", "engine": "whisper"}'
```

### GPU Acceleration
For optimal performance with Whisper.net on servers with NVIDIA GPUs:
- Ensure CUDA Toolkit 13.0 is installed
- The `Whisper.net.Runtime.Cuda.Windows` package provides CUDA support
- GPU acceleration is automatic when CUDA is available
- Monitor GPU usage with `nvidia-smi` during transcription
