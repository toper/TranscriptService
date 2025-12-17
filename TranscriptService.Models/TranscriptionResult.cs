namespace TranscriptService.Models
{
    /// <summary>
    /// Represents the result of a transcription operation
    /// </summary>
    public class TranscriptionResult
    {
        public string FileName { get; set; }
        /// <summary>
        /// List of timestamps. Contains individual words for Vosk, segments (sentences/phrases) for Whisper.
        /// </summary>
        public List<WordTimestamp> Transcription { get; set; }
        public string FullText { get; set; }
    }

    /// <summary>
    /// Represents a timestamped piece of text.
    /// Despite the name, this can contain either an individual word (Vosk) or a segment/phrase (Whisper).
    /// </summary>
    public class WordTimestamp
    {
        /// <summary>
        /// The text content. Contains an individual word for Vosk engine, or a segment (sentence/phrase) for Whisper engine.
        /// </summary>
        public string Word { get; set; }
        /// <summary>
        /// Start time in seconds
        /// </summary>
        public float StartTime { get; set; }
        /// <summary>
        /// End time in seconds
        /// </summary>
        public float EndTime { get; set; }
    }

    /// <summary>
    /// Interface for transcription engines
    /// </summary>
    public interface ITranscriber
    {
        /// <summary>
        /// Transcribes an audio file and returns the result with timestamps
        /// </summary>
        /// <param name="audioFilePath">Path to the audio file to transcribe</param>
        /// <returns>Transcription result with text and timestamps</returns>
        Task<TranscriptionResult> TranscribeAsync(string audioFilePath);
    }
}

