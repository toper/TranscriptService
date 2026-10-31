namespace TranscriptService.Worker.Core.Health;

/// <summary>Flips once the engine has loaded its model.</summary>
public sealed class ModelReadiness
{
    private volatile bool _isReady;

    public bool IsReady => _isReady;

    public void MarkReady() => _isReady = true;
}
