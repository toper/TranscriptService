namespace TranscriptService.Audio;

/// <summary>Mono samples in the range [-1, 1], ready to be fed to a speech engine.</summary>
public sealed record AudioSamples(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}
