using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.RabbitMq;
using TranscriptService.Audio;
using TranscriptService.Audio.Tests;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Callbacks;
using TranscriptService.Contracts.Http;
using TranscriptService.Worker.Core;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TranscriptService.IntegrationTests;

/// <summary>
/// The real API and a worker host talking through a real RabbitMQ broker. Only the speech engine
/// is fake, so the test proves the contract between the services, not recognition quality.
/// </summary>
public sealed class TranscriptionFlowTests : IAsyncLifetime
{
    private const string ApiKey = "integration-api-key-0123";
    private const string SigningSecret = "integration-signing-secret-0123456789";

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4-alpine").Build();
    private readonly WireMockServer _callbackReceiver = WireMockServer.Start();
    private readonly string _root = Directory.CreateTempSubdirectory("integration-").FullName;
    private WebApplicationFactory<Program> _api = null!;
    private WebApplication _worker = null!;

    public async ValueTask InitializeAsync()
    {
        await _rabbitMq.StartAsync(TestContext.Current.CancellationToken);
        var broker = $"host={_rabbitMq.Hostname};port={_rabbitMq.GetMappedPublicPort(5672)};username=rabbitmq;password=rabbitmq";

        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:RabbitMq", broker);
            builder.UseSetting("ConnectionStrings:Jobs", $"Data Source={Path.Combine(_root, "jobs.db")}");
            builder.UseSetting("Api:ApiKey", ApiKey);
            builder.UseSetting("Api:CallbackSigningSecret", SigningSecret);
            builder.UseSetting("Api:StoragePath", Path.Combine(_root, "audio"));
            builder.UseSetting("Api:AllowPrivateCallbackAddresses", "true");
        });
        _api.CreateClient().Dispose();

        var worker = WebApplication.CreateSlimBuilder();
        worker.WebHost.UseUrls("http://127.0.0.1:0");
        worker.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RabbitMq"] = broker,
            ["Engine:ModelPath"] = "fake",
        });
        worker.AddTranscriptionWorker<FakeTranscriber, EngineOptions>();
        _worker = worker.Build();
    }

    [Fact]
    public async Task Post_BeforeWorkerStarts_WorkerCompletes_SignedCallbackReceived_AndJobDeleted()
    {
        _callbackReceiver.Given(Request.Create().WithPath("/hook").UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        using var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", ApiKey);
        var telephoneCall = new ByteArrayContent(WavFixtures.MuLaw(WavFixtures.Sine(440, 8000, TimeSpan.FromSeconds(2)), 8000));
        telephoneCall.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var form = new MultipartFormDataContent
        {
            { telephoneCall, "audio", "call.wav" },
            { new StringContent($"{_callbackReceiver.Url}/hook"), "callbackUrl" },
        };

        var accepted = await client.PostAsync(new Uri("/api/v1/transcriptions", UriKind.Relative), form, TestContext.Current.CancellationToken);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var job = await accepted.Content.ReadFromJsonAsync<TranscriptionResponse>(TestContext.Current.CancellationToken);

        // A worker still loading its model must find the request waiting, not lost.
        await _worker.StartAsync(TestContext.Current.CancellationToken);

        var callback = await WaitForCallbackAsync();
        CallbackSigner.Verify(callback.BodyAsBytes, callback.Headers![CallbackSigner.SignatureHeader].Single(), SigningSecret).Should().BeTrue();
        callback.Body.Should().Contain("\"status\":\"Completed\"").And.Contain(FakeTranscriber.Text).And.Contain("\"engine\":\"fake\"");

        var afterDelivery = await client.GetAsync(new Uri($"/api/v1/transcriptions/{job!.Id}", UriKind.Relative), TestContext.Current.CancellationToken);
        afterDelivery.StatusCode.Should().Be(HttpStatusCode.NotFound, "delivered results are deleted");
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync();
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
        _callbackReceiver.Dispose();
        await _rabbitMq.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private async Task<WireMock.IRequestMessage> WaitForCallbackAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (_callbackReceiver.LogEntries.Count == 0)
        {
            await Task.Delay(100, timeout.Token);
        }

        return _callbackReceiver.LogEntries.Single().RequestMessage!;
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public const string Text = "hello from the fake engine";

        public string EngineName => "fake";

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken) =>
            Task.FromResult(new TranscriptionOutput(Text, [new TranscriptSegment(Text, TimeSpan.Zero, audio.Duration)]));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
