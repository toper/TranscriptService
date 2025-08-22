namespace TranscriptService.Models
{
    public class TranscriptionResult
    {
        public string FileName { get; set; }
        public List<WordTimestamp> Transcription { get; set; }
        public string FullText { get; set; }
    }
    public class WordTimestamp
    {
        public string Word { get; set; }
        public float StartTime { get; set; }
        public float EndTime { get; set; }
    }

    public interface ITranscriber
    {
        Task<TranscriptionResult> TranscribeAsync(string audioFilePath);
    }
}

