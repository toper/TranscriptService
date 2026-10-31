namespace TranscriptService.Worker.Core.Health;

/// <summary>Completes once the engine has loaded its model.</summary>
public sealed class ModelReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    public void MarkReady() => _ready.TrySetResult();

    /// <summary>Waits until the model is loaded.</summary>
    public Task WaitAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);
}
