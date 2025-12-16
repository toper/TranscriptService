namespace TranscriptService.API.Repository.Commands
{
    public interface ITranscriptCommands
    {
        /// <summary>
        /// Log transcription activity (placeholder for future database logging)
        /// </summary>
        Task LogTranscriptionAsync(string fileName, string engine, bool success, string errorMessage = null);
    }

    public class TranscriptCommands : ITranscriptCommands
    {
        private readonly ILogger<TranscriptCommands> _logger;

        public TranscriptCommands(ILogger<TranscriptCommands> logger)
        {
            _logger = logger;
        }

        public async Task LogTranscriptionAsync(string fileName, string engine, bool success, string errorMessage = null)
        {
            // Placeholder for future database logging
            _logger.LogInformation($"Transcription: File={fileName}, Engine={engine}, Success={success}, Error={errorMessage}");
            await Task.CompletedTask;
        }
    }
}
