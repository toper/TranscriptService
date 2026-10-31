using System.Text.Json;
using System.Text.Json.Serialization;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Demo.Web.Client.Transcriptions;

/// <summary>
/// Compile-time JSON metadata for what the browser receives. The WebAssembly app is trimmed, which
/// can remove what reflection-based serialization needs; generated metadata survives trimming.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TranscriptionResponse))]
[JsonSerializable(typeof(Problem))]
internal sealed partial class DemoJsonContext : JsonSerializerContext;

/// <summary>The parts of an RFC 9457 problem response the page shows.</summary>
internal sealed record Problem(string? Title, string? Detail);
