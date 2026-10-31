using Microsoft.AspNetCore.SignalR;
using TranscriptService.Demo.Web.Client.Transcriptions;

namespace TranscriptService.Demo.Web.Transcriptions;

/// <summary>
/// Pushes finished transcriptions to browsers. Each page joins the group named after its session,
/// so the backend keeps no routing state and results survive reconnects.
/// </summary>
public sealed class TranscriptionHub : Hub<ITranscriptionClient>
{
    [HubMethodName(DemoRoutes.JoinSession)]
    public Task JoinSessionAsync(Guid session) => Groups.AddToGroupAsync(Context.ConnectionId, session.ToString());
}
