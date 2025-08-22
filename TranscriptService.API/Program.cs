using Newtonsoft.Json;
using TranscriptService.Models;
using TranscriptService.VoskAPI;
using TranscriptService.Whisper;

namespace TranscriptService
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: MainApp <audioFolderPath> <voskModelPath> <openAiApiKey> <engine>");
                Console.WriteLine("engine = whisper or vosk");
                return;
            }

            string audioFolder = args[0];
            string voskModelPath = args[1];
            string openAiApiKey = args[2];
            string engine = args[3].ToLower();

            ITranscriber transcriber;

            if (engine == "whisper")
            {
                transcriber = new WhisperTranscriber(openAiApiKey);
            }
            else if (engine == "vosk")
            {
                transcriber = new VoskTranscriber(voskModelPath);
            }
            else
            {
                Console.WriteLine("Engine must be 'whisper' or 'vosk'");
                return;
            }

            var results = new List<(string FileName, TranscriptionResult Transcription)>();

            foreach (var file in Directory.GetFiles(audioFolder, "*.wav"))
            {
                Console.WriteLine($"Transcribing {file}");

                var transcription = await transcriber.TranscribeAsync(file);

                results.Add((Path.GetFileName(file), transcription));

                Console.WriteLine($"Done: {file}");
            }

            string jsonOut = JsonConvert.SerializeObject(results, Formatting.Indented);
            await File.WriteAllTextAsync("transcriptions.json", jsonOut);

            Console.WriteLine("Transcriptions saved to transcriptions.json");
        }
    }
}