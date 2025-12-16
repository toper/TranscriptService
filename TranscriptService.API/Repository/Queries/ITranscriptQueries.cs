namespace TranscriptService.API.Repository.Queries
{
    public interface ITranscriptQueries
    {
        // Placeholder for future queries (e.g., get transcription history)
    }

    public class TranscriptQueries : ITranscriptQueries
    {
        private readonly ILogger<TranscriptQueries> _logger;

        public TranscriptQueries(ILogger<TranscriptQueries> logger)
        {
            _logger = logger;
        }
    }
}
