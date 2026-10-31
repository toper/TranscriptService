namespace TranscriptService.Worker.Core;

/// <summary>
/// Counts transcriptions running inside native engine code, so an engine can wait for them before
/// freeing its model: native decoding cannot be interrupted, and freeing the weights under it crashes
/// the process instead of letting it exit.
/// </summary>
public sealed class InFlightTracker
{
    private readonly Lock _gate = new();
    private int _running;
    private bool _disposing;
    private TaskCompletionSource? _idle;

    /// <summary>Marks one transcription as running until the returned handle is disposed.</summary>
    /// <exception cref="ObjectDisposedException">The engine is freeing its model.</exception>
    public IDisposable Enter()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            _running++;
        }

        return new Exit(this);
    }

    /// <summary>How long an engine waits for running transcriptions before giving up on freeing its model.</summary>
    public static readonly TimeSpan DefaultDisposeWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Refuses new work, then frees an engine's native resources once nothing runs any more. If work is still running after
    /// the wait, the resources are left to the process exit rather than freed under a native call.
    /// </summary>
    public async ValueTask DisposeWhenIdleAsync(Action free, TimeSpan? wait = null)
    {
        ArgumentNullException.ThrowIfNull(free);
        lock (_gate)
        {
            _disposing = true;
        }

        if (await WaitIdleAsync(wait ?? DefaultDisposeWait).ConfigureAwait(false))
        {
            free();
        }
    }

    /// <returns><see langword="true"/> when nothing is running any more; <see langword="false"/> on timeout.</returns>
    public async Task<bool> WaitIdleAsync(TimeSpan timeout)
    {
        Task idle;
        lock (_gate)
        {
            if (_running == 0)
            {
                return true;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idle = _idle.Task;
        }

        return await Task.WhenAny(idle, Task.Delay(timeout)).ConfigureAwait(false) == idle;
    }

    private void Leave()
    {
        lock (_gate)
        {
            if (--_running == 0)
            {
                _idle?.TrySetResult();
                _idle = null;
            }
        }
    }

    private sealed class Exit(InFlightTracker tracker) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                tracker.Leave();
            }
        }
    }
}
