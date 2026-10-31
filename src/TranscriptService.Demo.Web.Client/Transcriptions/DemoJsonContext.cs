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
internal sealed partial class DemoJsonContext : JsonSerializerContext;
