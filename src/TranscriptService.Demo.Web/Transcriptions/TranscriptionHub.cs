using Microsoft.AspNetCore.SignalR;
using TranscriptService.Demo.Web.Client.Transcriptions;

namespace TranscriptService.Demo.Web.Transcriptions;

/// <summary>Pushes finished transcriptions to the browser that submitted them.</summary>
public sealed class TranscriptionHub(TranscriptionRelay relay) : Hub<ITranscriptionClient>
{
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        relay.Forget(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
