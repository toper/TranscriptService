using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Demo.Web.Client.Transcriptions;

/// <summary>
/// The browser's link to the demo backend. Every upload carries this page's session id, and the
/// hub connection joins that session's group, so results arrive even after a reconnect.
/// </summary>
public sealed class TranscriptionSession(HttpClient http, NavigationManager navigation) : ITranscriptionSession
{
    private readonly string _session = Guid.NewGuid().ToString();
    private HubConnection? _hub;

    public event Func<TranscriptionResponse, Task>? Finished;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(DemoRoutes.Hub))
            .AddJsonProtocol(json => json.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, DemoJsonContext.Default))
            .WithAutomaticReconnect()
            .Build();
        hub.On<TranscriptionResponse>(nameof(ITranscriptionClient.TranscriptionFinished), transcription => Finished?.Invoke(transcription) ?? Task.CompletedTask);
        // A reconnect brings a new connection id; joining again keeps the session's results coming.
        hub.Reconnected += _ => hub.InvokeAsync(DemoRoutes.JoinSession, _session);

        await hub.StartAsync(cancellationToken).ConfigureAwait(false);
        await hub.InvokeAsync(DemoRoutes.JoinSession, _session, cancellationToken).ConfigureAwait(false);
        _hub = hub;
    }

    public async Task<Guid> SubmitAsync(Stream wav, string fileName, CancellationToken cancellationToken)
    {
        using var audio = new StreamContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var form = new MultipartFormDataContent
        {
            { audio, "audio", fileName },
            { new StringContent(_session), DemoRoutes.SessionField },
        };

        using var response = await http.PostAsync(new Uri(DemoRoutes.Utterances, UriKind.Relative), form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The backend forwards the API's problem details; show their explanation, not just a status code.
            var problem = await response.Content.ReadFromJsonAsync(DemoJsonContext.Default.Problem, cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(problem?.Detail ?? problem?.Title ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        }

        var accepted = await response.Content.ReadFromJsonAsync(DemoJsonContext.Default.TranscriptionResponse, cancellationToken).ConfigureAwait(false);
        return accepted!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync().ConfigureAwait(false);
            _hub = null;
        }
    }
}
