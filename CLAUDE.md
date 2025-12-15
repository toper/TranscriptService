# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TranscriptService is a .NET 8.0 console application that transcribes audio files using either OpenAI Whisper or Vosk speech recognition engines. The application processes WAV files from a folder and outputs timestamped transcriptions to JSON.

## Solution Structure

The solution consists of 4 projects:

- **TranscriptService.API** - Main console application (entry point in `Program.cs`)
- **TranscriptService.Models** - Shared models and interfaces
  - `TranscriptionResult` - Output model with word-level timestamps
  - `ITranscriber` - Interface implemented by all transcription engines
- **TranscriptService.Whisper** - OpenAI Whisper API implementation
- **TranscriptService.VoskAPI** - Vosk (offline) transcription implementation

### Key Architecture

The project uses a **strategy pattern** via the `ITranscriber` interface. Both transcription engines implement the same interface:

```csharp
Task<TranscriptionResult> TranscribeAsync(string audioFilePath)
```

The main application (`TranscriptService.API/Program.cs`) selects the transcriber based on command-line arguments and processes all WAV files in the specified folder sequentially.

## Building and Running

### Build the solution
```bash
dotnet build TranscriptService.sln
```

### Run the application
```bash
dotnet run --project TranscriptService.API -- <audioFolderPath> <voskModelPath> <openAiApiKey> <engine>
```

Arguments:
- `audioFolderPath` - Path to folder containing WAV files
- `voskModelPath` - Path to Vosk model (required even if using Whisper)
- `openAiApiKey` - OpenAI API key (required even if using Vosk)
- `engine` - Either "whisper" or "vosk"

Example:
```bash
dotnet run --project TranscriptService.API -- ./audio ./vosk-model sk-xxx123 whisper
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

Currently no test projects exist in the solution. When adding tests, follow the naming convention:
- `TranscriptService.Tests` - for unit tests
- `TranscriptService.IntegrationTests` - for integration tests

## Dependencies

- **.NET 8.0** - Target framework (solution built with SDK 9.0.306+)
- **Newtonsoft.Json 13.0.3** - JSON serialization (used across all projects)
- **Vosk 0.3.38** - Offline speech recognition (VoskAPI project only)
- **Microsoft.VisualStudio.Azure.Containers.Tools.Targets** - Docker support

## Key Implementation Details

### Whisper Implementation
- Calls OpenAI's `/v1/audio/transcriptions` endpoint
- Uses `verbose_json` response format to get segment and word-level timestamps
- Handles both segment-level and word-level responses

### Vosk Implementation
- Uses local Vosk models (requires model path)
- Processes audio in 4096-byte chunks
- Expects 16kHz sample rate
- Returns JSON with word-level timestamps

### Output Format
Both engines produce a `TranscriptionResult` containing:
- `FullText` - Complete transcription as a single string
- `Transcription` - List of `WordTimestamp` objects with `Word`, `StartTime`, `EndTime`

Results are saved to `transcriptions.json` in the working directory as an array of `(FileName, TranscriptionResult)` tuples.

## Working with the Codebase

### Adding a new transcription engine
1. Create a new project: `TranscriptService.YourEngine`
2. Reference `TranscriptService.Models`
3. Implement `ITranscriber` interface
4. Add project reference in `TranscriptService.API`
5. Update `Program.cs` to support the new engine name
