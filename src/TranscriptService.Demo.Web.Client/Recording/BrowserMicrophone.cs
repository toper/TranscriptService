using Microsoft.JSInterop;

namespace TranscriptService.Demo.Web.Client.Recording;

/// <summary>The browser microphone, captured and split into utterances by <c>js/recorder.js</c>.</summary>
public sealed class BrowserMicrophone(IJSRuntime js) : IMicrophone
{
    private IJSObjectReference? _recorder;
    private DotNetObjectReference<BrowserMicrophone>? _callbacks;

    public event Func<byte[], Task>? UtteranceRecorded;

    public async Task StartAsync()
    {
        _recorder ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/recorder.js").ConfigureAwait(false);
        _callbacks ??= DotNetObjectReference.Create(this);
        await _recorder.InvokeVoidAsync("start", _callbacks).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (_recorder is not null)
        {
            await _recorder.InvokeVoidAsync("stop").ConfigureAwait(false);
        }
    }

    /// <summary>Called by the recorder module for every utterance.</summary>
    [JSInvokable]
    public Task OnUtterance(byte[] wav) => UtteranceRecorded?.Invoke(wav) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_recorder is not null)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
                await _recorder.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // The page is already gone; the browser released the microphone with it.
            }
        }

        _callbacks?.Dispose();
    }
}
