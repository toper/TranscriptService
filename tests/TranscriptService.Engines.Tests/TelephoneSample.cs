using TranscriptService.Audio;
using TranscriptService.Audio.Tests;
using TranscriptService.Worker.Core;

namespace TranscriptService.Engines.Tests;

/// <summary>The shared engine check: the repository sample as a call recording (8 kHz G.711 μ-law).</summary>
internal static class TelephoneSample
{
    /// <summary>
    /// Transcribes the sample in Polish and checks the words and segment timing. The sample is
    /// synthetic speech (eSpeak NG), so the expected words are ones that survive 8 kHz μ-law reliably.
    /// </summary>
    public static async Task AssertTranscribesAsync(ITranscriber transcriber, params string[] expectedWords)
    {
        var call = WavFixtures.WriteTemp(WavFixtures.SampleAt8kHzMuLaw());
        try
        {
            var audio = await AudioPreprocessor.LoadAsync(call, TestContext.Current.CancellationToken);

            var output = await transcriber.TranscribeAsync(audio, "pl", TestContext.Current.CancellationToken);

            foreach (var word in expectedWords)
            {
                output.FullText.Should().ContainEquivalentOf(word);
            }

            output.Segments.Should().NotBeEmpty().And.BeInAscendingOrder(segment => segment.Start);
            output.Segments.Should().OnlyContain(segment => segment.End > segment.Start && segment.End <= audio.Duration);
        }
        finally
        {
            File.Delete(call);
        }
    }
}
