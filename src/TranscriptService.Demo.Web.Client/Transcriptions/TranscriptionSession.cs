using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Demo.Web.Client.Transcriptions;

/// <summary>
/// SignalR connection to the demo backend plus uploads that carry this connection's id, so the
/// backend can route each callback from the API to this browser.
/// </summary>
public sealed class TranscriptionSession(HttpClient http, NavigationManager navigation) : ITranscriptionSession
{
    private HubConnection? _hub;

    public event Func<TranscriptionResponse, Task>? Finished;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(DemoRoutes.Hub))
            .AddJsonProtocol(json => json.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, DemoJsonContext.Default))
            .WithAutomaticReconnect()
            .Build();
        _hub.On<TranscriptionResponse>(nameof(ITranscriptionClient.TranscriptionFinished), async transcription =>
        {
            if (Finished is { } handler)
            {
                await handler(transcription).ConfigureAwait(false);
            }
        });
        await _hub.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> SubmitAsync(Stream wav, string fileName, CancellationToken cancellationToken)
    {
        var connectionId = _hub?.ConnectionId ?? throw new InvalidOperationException("Not connected to the transcription hub.");

        using var audio = new StreamContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var form = new MultipartFormDataContent
        {
            { audio, "audio", fileName },
            { new StringContent(connectionId), DemoRoutes.ConnectionIdField },
        };

        using var response = await http.PostAsync(new Uri(DemoRoutes.Utterances, UriKind.Relative), form, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var accepted = await response.Content.ReadFromJsonAsync(DemoJsonContext.Default.TranscriptionResponse, cancellationToken).ConfigureAwait(false);
        return accepted!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync().ConfigureAwait(false);
        }
    }
}
