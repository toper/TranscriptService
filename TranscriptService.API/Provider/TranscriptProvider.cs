using Microsoft.Extensions.Options;
using TranscriptService.Models;
using TranscriptService.Whisper;
using TranscriptService.VoskAPI;
using TranscriptService.API.Models.Responses;

namespace TranscriptService.API.Provider
{
    public interface ITranscriptProvider
    {
        Task<Result<TranscriptResponse>> TranscribeAsync(string filePath, string engine);
        Task<Result<TranscriptResponse>> TranscribeFromStreamAsync(Stream audioStream, string fileName, string engine);
    }

    public class TranscriptProvider : ITranscriptProvider
    {
        private readonly ILogger<TranscriptProvider> _logger;
        private readonly IConfiguration _configuration;

        public TranscriptProvider(ILogger<TranscriptProvider> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<Result<TranscriptResponse>> TranscribeAsync(string filePath, string engine)
        {
            var result = new Result<TranscriptResponse>();

            try
            {
                var transcriber = CreateTranscriber(engine);
                var transcription = await transcriber.TranscribeAsync(filePath);

                result.Data = MapToResponse(transcription, Path.GetFileName(filePath), engine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error transcribing file {filePath} with engine {engine}");
                result.Errors.Add($"Transcription failed: {ex.Message}");
            }

            return result;
        }

        public async Task<Result<TranscriptResponse>> TranscribeFromStreamAsync(Stream audioStream, string fileName, string engine)
        {
            var result = new Result<TranscriptResponse>();
            string tempPath = null;

            try
            {
                // Save stream to temp file
                tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

                using (var fileStream = File.Create(tempPath))
                {
                    await audioStream.CopyToAsync(fileStream);
                }

                // Transcribe
                var transcriber = CreateTranscriber(engine);
                var transcription = await transcriber.TranscribeAsync(tempPath);

                result.Data = MapToResponse(transcription, fileName, engine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error transcribing uploaded file {fileName} with engine {engine}");
                result.Errors.Add($"Transcription failed: {ex.Message}");
            }
            finally
            {
                // Cleanup temp file
                if (tempPath != null && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to delete temp file {tempPath}");
                    }
                }
            }

            return result;
        }

        private ITranscriber CreateTranscriber(string engine)
        {
            return engine.ToLower() switch
            {
                "whisper" => new WhisperTranscriber(_configuration["AppSettings:OpenAiApiKey"]),
                "vosk" => new VoskTranscriber(_configuration["AppSettings:VoskModelPath"]),
                _ => throw new ArgumentException($"Unknown engine: {engine}")
            };
        }

        private TranscriptResponse MapToResponse(TranscriptionResult transcription, string fileName, string engine)
        {
            return new TranscriptResponse
            {
                FileName = fileName,
                FullText = transcription.FullText,
                Words = transcription.Transcription.Select(w => new WordTimestampDto
                {
                    Word = w.Word,
                    StartTime = w.StartTime,
                    EndTime = w.EndTime
                }).ToList(),
                Engine = engine,
                ProcessedAt = DateTime.UtcNow
            };
        }
    }
}
