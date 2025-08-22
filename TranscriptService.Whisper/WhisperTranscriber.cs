using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using TranscriptService.Models;

namespace TranscriptService.Whisper
{
    public class WhisperTranscriber : ITranscriber
    {
        private readonly string apiKey;

        public WhisperTranscriber(string openAiApiKey)
        {
            apiKey = openAiApiKey;
        }

        public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath)
        {
            byte[] audioBytes = await System.IO.File.ReadAllBytesAsync(audioFilePath);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var content = new MultipartFormDataContent
            {
                { new ByteArrayContent(audioBytes), "file", "audio.wav" },
                { new StringContent("whisper-1"), "model" },
                { new StringContent("verbose_json"), "response_format" }
            };

            var response = await http.PostAsync("https://api.openai.com/v1/audio/transcriptions", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var j = JObject.Parse(json);
            var segments = j["segments"];

            var transcriptionResult = new TranscriptionResult
            {
                Transcription = new List<WordTimestamp>(),
                FullText = ""
            };

            if (segments != null)
            {
                foreach (var segment in segments)
                {
                    string segmentText = segment["text"].ToString();
                    transcriptionResult.FullText += segmentText + " ";

                    var words = segment["words"];
                    if (words != null)
                    {
                        foreach (var word in words)
                        {
                            transcriptionResult.Transcription.Add(new WordTimestamp
                            {
                                Word = word["word"].ToString(),
                                StartTime = (float)word["start"],
                                EndTime = (float)word["end"]
                            });
                        }
                    }
                }
            }
            else
            {
                transcriptionResult.FullText = j["text"]?.ToString() ?? "";
            }

            return transcriptionResult;
        }
    }
}