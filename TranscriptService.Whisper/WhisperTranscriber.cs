using TranscriptService.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace TranscriptService.Whisper
{
    public class WhisperTranscriber : ITranscriber, IDisposable
    {
        private readonly string _modelPath;
        private WhisperProcessor? _processor;
        private bool _disposed = false;

        public WhisperTranscriber(string whisperModelPath)
        {
            _modelPath = whisperModelPath;
        }

        public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WhisperTranscriber));

            // Initialize processor if not already initialized
            if (_processor == null)
            {
                _processor = CreateWhisperProcessor();
            }

            var transcriptionResult = new TranscriptionResult
            {
                Transcription = new List<WordTimestamp>(),
                FullText = ""
            };

            // Process audio file
            using var fileStream = File.OpenRead(audioFilePath);

            await foreach (var segment in _processor.ProcessAsync(fileStream))
            {
                transcriptionResult.FullText += segment.Text + " ";

                // Add segment as a single word entry with timestamps
                transcriptionResult.Transcription.Add(new WordTimestamp
                {
                    Word = segment.Text.Trim(),
                    StartTime = (float)segment.Start.TotalSeconds,
                    EndTime = (float)segment.End.TotalSeconds
                });
            }

            transcriptionResult.FullText = transcriptionResult.FullText.Trim();

            return transcriptionResult;
        }

        private WhisperProcessor CreateWhisperProcessor()
        {
            // Detect if this is a large model based on filename
            var modelFileName = Path.GetFileName(_modelPath).ToLowerInvariant();
            var isLargeModel = modelFileName.Contains("large") ||
                              modelFileName.Contains("medium");

            Console.WriteLine($"[WhisperTranscriber] Loading model: {modelFileName}");
            Console.WriteLine($"[WhisperTranscriber] Detected as large model: {isLargeModel}");

            WhisperProcessor? processor = null;
            Exception? lastException = null;

            // Strategy 1: Try loading model without CUDA runtime interference
            // For large models, we need to be very careful with memory
            if (processor == null && isLargeModel)
            {
                try
                {
                    Console.WriteLine("[WhisperTranscriber] Strategy 1: Loading large model with minimal config...");

                    // For large models, use absolute minimum configuration
                    // This avoids CUDA initialization which often causes AccessViolation
                    var factory = WhisperFactory.FromPath(_modelPath);

                    Console.WriteLine("[WhisperTranscriber] Factory created successfully");

                    processor = factory.CreateBuilder()
                        .WithLanguage("pl")
                        .WithThreads(1)  // Single thread for large models to minimize memory issues
                        .Build();

                    Console.WriteLine("[WhisperTranscriber] Processor built successfully with 1 thread");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WhisperTranscriber] Strategy 1 failed: {ex.GetType().Name} - {ex.Message}");
                    lastException = ex;
                    processor = null;
                }
            }

            // Strategy 2: Try with optimal thread count for small models
            if (processor == null && !isLargeModel)
            {
                try
                {
                    Console.WriteLine("[WhisperTranscriber] Strategy 2: Loading small model with optimal config...");

                    var factory = WhisperFactory.FromPath(_modelPath);
                    var threadCount = Environment.ProcessorCount;

                    processor = factory.CreateBuilder()
                        .WithLanguage("pl")
                        .WithThreads(threadCount)
                        .Build();

                    Console.WriteLine($"[WhisperTranscriber] Processor built successfully with {threadCount} threads");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WhisperTranscriber] Strategy 2 failed: {ex.GetType().Name} - {ex.Message}");
                    lastException = ex;
                    processor = null;
                }
            }

            // Strategy 3: Try with minimal threads (fallback)
            if (processor == null)
            {
                try
                {
                    Console.WriteLine("[WhisperTranscriber] Strategy 3: Minimal thread fallback...");

                    var factory = WhisperFactory.FromPath(_modelPath);
                    processor = factory.CreateBuilder()
                        .WithLanguage("pl")
                        .WithThreads(1)
                        .Build();

                    Console.WriteLine("[WhisperTranscriber] Processor built with fallback strategy");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WhisperTranscriber] Strategy 3 failed: {ex.GetType().Name} - {ex.Message}");
                    lastException = ex;
                    processor = null;
                }
            }

            // Strategy 4: Last resort - try without explicit thread configuration
            if (processor == null)
            {
                try
                {
                    Console.WriteLine("[WhisperTranscriber] Strategy 4: Default configuration (last resort)...");

                    var factory = WhisperFactory.FromPath(_modelPath);
                    processor = factory.CreateBuilder()
                        .WithLanguage("pl")
                        .Build();

                    Console.WriteLine("[WhisperTranscriber] Processor built with default configuration");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WhisperTranscriber] Strategy 4 failed: {ex.GetType().Name} - {ex.Message}");
                    lastException = ex;
                }
            }

            if (processor == null)
            {
                var errorMessage = $"Failed to initialize Whisper processor for model: {_modelPath}.\n" +
                    $"Model: {modelFileName} (Large: {isLargeModel})\n" +
                    $"This is likely caused by:\n" +
                    $"1. CUDA runtime conflict - try removing Whisper.net.Runtime.Cuda.Windows package\n" +
                    $"2. Insufficient memory for large models - use ggml-base.bin or ggml-medium.bin instead\n" +
                    $"3. Corrupted model file - re-download from https://huggingface.co/ggerganov/whisper.cpp\n" +
                    $"Original error: {lastException?.GetType().Name} - {lastException?.Message}";

                Console.WriteLine($"[WhisperTranscriber] ERROR: {errorMessage}");

                throw new InvalidOperationException(errorMessage, lastException);
            }

            Console.WriteLine("[WhisperTranscriber] Processor initialized successfully!");
            return processor;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _processor?.Dispose();
                }

                _disposed = true;
            }
        }

        ~WhisperTranscriber()
        {
            Dispose(false);
        }
    }
}