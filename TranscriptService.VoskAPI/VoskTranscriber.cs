using Newtonsoft.Json;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TranscriptService.Models;
using Vosk;

namespace TranscriptService.VoskAPI
{
    public class VoskTranscriber : ITranscriber
    {
        private readonly Model model;

        public VoskTranscriber(string modelPath)
        {
            model = new Model(modelPath);
        }

        public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath)
        {
            var recognizer = new VoskRecognizer(model, 16000.0f);

            using var stream = File.OpenRead(audioFilePath);
            byte[] buffer = new byte[4096];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                recognizer.AcceptWaveform(buffer, bytesRead);
            }

            var resultJson = recognizer.FinalResult();

            var voskResult = JsonConvert.DeserializeObject<VoskResult>(resultJson);

            var transcriptionResult = new TranscriptionResult
            {
                Transcription = new List<WordTimestamp>(),
                FullText = voskResult.Text
            };

            foreach (var word in voskResult.Result)
            {
                transcriptionResult.Transcription.Add(new WordTimestamp
                {
                    Word = word.Word,
                    StartTime = word.Start,
                    EndTime = word.End
                });
            }

            return transcriptionResult;
        }
    }

    public class VoskResult
    {
        [JsonProperty("result")]
        public List<VoskWord> Result { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class VoskWord
    {
        [JsonProperty("word")]
        public string Word { get; set; }

        [JsonProperty("start")]
        public float Start { get; set; }

        [JsonProperty("end")]
        public float End { get; set; }
    }
}