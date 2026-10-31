using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TranscriptService.Contracts.Callbacks;
using TranscriptService.Contracts.Http;
using TranscriptService.Demo.Web.Client.Transcriptions;
using TranscriptService.Demo.Web.Transcriptions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TranscriptService.Demo.Web.Tests;

public sealed class DemoBackendTests : IDisposable
{
    private const string Secret = "demo-test-signing-secret-0123456789abcdef";
    private const string ApiKey = "demo-test-api-key-0123";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WireMockServer _api = WireMockServer.Start();
    private readonly RecordingHubContext _hub = new();
    private readonly WebApplicationFactory<TranscriptionHub> _demo;

    public DemoBackendTests()
    {
        _demo = new WebApplicationFactory<TranscriptionHub>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("TranscriptApi:BaseUrl", _api.Url);
            builder.UseSetting("TranscriptApi:ApiKey", ApiKey);
            builder.UseSetting("TranscriptApi:CallbackSigningSecret", Secret);
            builder.UseSetting("TranscriptApi:PublicBaseUrl", "http://demo-web:8080");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHubContext<TranscriptionHub, ITranscriptionClient>>();
                services.AddSingleton<IHubContext<TranscriptionHub, ITranscriptionClient>>(_hub);
            });
        });
    }

    [Fact]
    public async Task Utterance_SubmitsToTheApi_WithACallbackUrlCarryingTheSession()
    {
        var session = Guid.NewGuid();
        _api.Given(Request.Create().WithPath("/api/v1/transcriptions").UsingPost().WithHeader("X-API-KEY", ApiKey))
            .RespondWith(Response.Create().WithStatusCode(202).WithBodyAsJson(Job(Guid.NewGuid(), JobStatus.Queued)));
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Utterances, UriKind.Relative), Utterance(session), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var forwarded = _api.LogEntries.Should().ContainSingle().Subject.RequestMessage!;
        forwarded.Body.Should().Contain($"http://demo-web:8080/callbacks/transcriptions?session={session}").And.Contain("name=language");
    }

    [Fact]
    public async Task Utterance_RejectedByTheApi_ReturnsTheApiProblem()
    {
        _api.Given(Request.Create().WithPath("/api/v1/transcriptions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithHeader("Content-Type", "application/problem+json")
                .WithBody("""{"title":"Unsupported recording","status":400,"detail":"Expected PCM 16-bit"}"""));
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Utterances, UriKind.Relative), Utterance(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Expected PCM 16-bit");
    }

    [Fact]
    public async Task Utterance_ApiUnreachable_Returns503Problem()
    {
        _api.Stop();
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Utterances, UriKind.Relative), Utterance(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Callback_WithInvalidSignature_Returns401_AndPushesNothing()
    {
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(CallbackUri(Guid.NewGuid()), Callback(Job(Guid.NewGuid(), JobStatus.Completed), "sha256=00"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _hub.Pushed.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_WithValidSignature_PushesToTheSessionGroup()
    {
        var session = Guid.NewGuid();
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(CallbackUri(session), Callback(Job(Guid.NewGuid(), JobStatus.Completed, "Ile kosztuje paleta?")), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (group, transcription) = _hub.Pushed.Should().ContainSingle().Subject;
        group.Should().Be(session.ToString());
        transcription.FullText.Should().Be("Ile kosztuje paleta?");
    }

    [Fact]
    public async Task Callback_WithoutASession_IsAcknowledged_AndPushesNothing()
    {
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Callbacks, UriKind.Relative), Callback(Job(Guid.NewGuid(), JobStatus.Failed)), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the API must not retry a delivered result");
        _hub.Pushed.Should().BeEmpty();
    }

    public void Dispose()
    {
        _demo.Dispose();
        _api.Dispose();
    }

    private static TranscriptionResponse Job(Guid id, JobStatus status, string? text = null) =>
        new(id, status, "pl", DateTimeOffset.UtcNow, null, "whispernet", text, [], null, TimeSpan.FromSeconds(1), null);

    private static Uri CallbackUri(Guid session) => new($"{DemoRoutes.Callbacks}?{DemoRoutes.SessionField}={session}", UriKind.Relative);

    private static MultipartFormDataContent Utterance(Guid session)
    {
        var audio = new ByteArrayContent([1, 2, 3]);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return new MultipartFormDataContent
        {
            { audio, "audio", "utterance.wav" },
            { new StringContent(session.ToString()), DemoRoutes.SessionField },
        };
    }

    private static ByteArrayContent Callback(TranscriptionResponse job, string? signature = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(job, Json);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add(CallbackSigner.SignatureHeader, signature ?? CallbackSigner.Sign(body, Secret));
        return content;
    }

    /// <summary>Captures what the server pushes to browsers.</summary>
    private sealed class RecordingHubContext : IHubContext<TranscriptionHub, ITranscriptionClient>
    {
        public RecordingHubContext() => Clients = new RecordingClients(this);

        public List<(string Group, TranscriptionResponse Transcription)> Pushed { get; } = [];

        public IHubClients<ITranscriptionClient> Clients { get; }

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingClients(RecordingHubContext context) : IHubClients<ITranscriptionClient>
    {
        public ITranscriptionClient All => throw new NotSupportedException();

        public ITranscriptionClient Client(string connectionId) => throw new NotSupportedException();

        public ITranscriptionClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ITranscriptionClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public ITranscriptionClient Group(string groupName) => new Recorder(context, groupName);

        public ITranscriptionClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ITranscriptionClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public ITranscriptionClient User(string userId) => throw new NotSupportedException();

        public ITranscriptionClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class Recorder(RecordingHubContext context, string group) : ITranscriptionClient
    {
        public Task TranscriptionFinished(TranscriptionResponse transcription)
        {
            context.Pushed.Add((group, transcription));
            return Task.CompletedTask;
        }
    }
}
