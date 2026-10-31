using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TranscriptService.Api.Tests.Support;
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
    public async Task Utterance_SubmitsToTheApiWithCallbackUrl_AndRegistersTheConnection()
    {
        var jobId = Guid.NewGuid();
        _api.Given(Request.Create().WithPath("/api/v1/transcriptions").UsingPost().WithHeader("X-API-KEY", ApiKey))
            .RespondWith(Response.Create().WithStatusCode(202).WithBodyAsJson(Job(jobId, JobStatus.Queued)));
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Utterances, UriKind.Relative), Utterance("connection-1"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var forwarded = _api.LogEntries.Should().ContainSingle().Subject.RequestMessage!;
        forwarded.Body.Should().Contain("http://demo-web:8080/callbacks/transcriptions").And.Contain("name=language");

        await client.PostAsync(new Uri(DemoRoutes.Callbacks, UriKind.Relative), Callback(Job(jobId, JobStatus.Completed)), TestContext.Current.CancellationToken);
        _hub.Pushed.Should().ContainSingle().Which.Connection.Should().Be("connection-1");
    }

    [Fact]
    public async Task Utterance_RejectedByTheApi_ReturnsTheApiProblem()
    {
        _api.Given(Request.Create().WithPath("/api/v1/transcriptions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithHeader("Content-Type", "application/problem+json")
                .WithBody("""{"title":"Unsupported recording","status":400,"detail":"Expected PCM 16-bit"}"""));
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Utterances, UriKind.Relative), Utterance("connection-1"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Expected PCM 16-bit");
    }

    [Fact]
    public async Task Callback_WithInvalidSignature_Returns401_AndPushesNothing()
    {
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Callbacks, UriKind.Relative), Callback(Job(Guid.NewGuid(), JobStatus.Completed), "sha256=00"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _hub.Pushed.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_WithValidSignature_PushesToTheRegisteredConnection()
    {
        var job = Job(Guid.NewGuid(), JobStatus.Completed, "Ile kosztuje paleta?");
        await _demo.Services.GetRequiredService<TranscriptionRelay>().RegisterAsync(job.Id, "connection-7");
        using var client = _demo.CreateClient();

        var response = await client.PostAsync(new Uri(DemoRoutes.Callbacks, UriKind.Relative), Callback(job), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (connection, transcription) = _hub.Pushed.Should().ContainSingle().Subject;
        connection.Should().Be("connection-7");
        transcription.FullText.Should().Be("Ile kosztuje paleta?");
    }

    [Fact]
    public async Task Callback_ArrivingBeforeTheUploadRegistersItsJob_IsDeliveredOnRegistration()
    {
        // A fast GPU worker can finish before the API's 202 reaches the demo backend.
        var job = Job(Guid.NewGuid(), JobStatus.Completed, "Szybciej niż odpowiedź");
        using var client = _demo.CreateClient();
        await client.PostAsync(new Uri(DemoRoutes.Callbacks, UriKind.Relative), Callback(job), TestContext.Current.CancellationToken);

        await _demo.Services.GetRequiredService<TranscriptionRelay>().RegisterAsync(job.Id, "connection-3");
        await Eventually.TrueAsync(() => _hub.Pushed.Count == 1);

        _hub.Pushed.Single().Connection.Should().Be("connection-3");
    }

    [Fact]
    public async Task Callback_ForAJobNobodyRegistered_IsAcknowledged()
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

    private static MultipartFormDataContent Utterance(string connectionId)
    {
        var audio = new ByteArrayContent([1, 2, 3]);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return new MultipartFormDataContent
        {
            { audio, "audio", "utterance.wav" },
            { new StringContent(connectionId), DemoRoutes.ConnectionIdField },
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

        public List<(string Connection, TranscriptionResponse Transcription)> Pushed { get; } = [];

        public IHubClients<ITranscriptionClient> Clients { get; }

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingClients(RecordingHubContext context) : IHubClients<ITranscriptionClient>
    {
        public ITranscriptionClient All => throw new NotSupportedException();

        public ITranscriptionClient Client(string connectionId) => new Recorder(context, connectionId);

        public ITranscriptionClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ITranscriptionClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public ITranscriptionClient Group(string groupName) => throw new NotSupportedException();

        public ITranscriptionClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public ITranscriptionClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public ITranscriptionClient User(string userId) => throw new NotSupportedException();

        public ITranscriptionClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class Recorder(RecordingHubContext context, string connectionId) : ITranscriptionClient
    {
        public Task TranscriptionFinished(TranscriptionResponse transcription)
        {
            context.Pushed.Add((connectionId, transcription));
            return Task.CompletedTask;
        }
    }
}
