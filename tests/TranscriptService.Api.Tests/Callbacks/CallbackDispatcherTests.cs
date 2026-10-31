using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Tests.Support;
using TranscriptService.Contracts.Callbacks;
using TranscriptService.Contracts.Http;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace TranscriptService.Api.Tests.Callbacks;

public sealed class CallbackDispatcherTests : IDisposable
{
    private readonly WireMockServer _client = WireMockServer.Start();
    private readonly ApiTestFactory _factory = new();

    public CallbackDispatcherTests()
    {
        _factory.Settings["CallbackDelivery:Retry:Delay"] = "00:00:00.010";
        _factory.Settings["CallbackDelivery:Retry:MaxRetryAttempts"] = "2";
    }

    [Fact]
    public async Task Dispatch_Success_PostsSignedPayload_DeletesJob()
    {
        RespondWith(200);
        var job = await AddFinishedJobAsync(_factory);

        await WaitUntilAsync(_factory, async db => !await db.Jobs.AnyAsync(j => j.Id == job.Id));

        await WaitUntilLoggedAsync(1);
        var request = _client.LogEntries.Should().ContainSingle().Subject.RequestMessage!;
        request.Headers![CallbackSigner.JobIdHeader].Should().ContainSingle().Which.Should().Be(job.Id.ToString());
        var signature = request.Headers[CallbackSigner.SignatureHeader].Should().ContainSingle().Subject;
        CallbackSigner.Verify(request.BodyAsBytes, signature, ApiTestFactory.SigningSecret).Should().BeTrue();
        request.Body.Should().Contain("\"status\":\"Completed\"").And.Contain("dzień dobry");
    }

    [Fact]
    public async Task Dispatch_SameJobQueuedTwice_DeliversOnce_AndKeepsRunning()
    {
        _client.Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromMilliseconds(200)));
        var duplicated = await AddFinishedJobAsync(_factory);
        await DispatchAsync(_factory, duplicated.Id);

        await WaitUntilAsync(_factory, async db => !await db.Jobs.AnyAsync(j => j.Id == duplicated.Id));
        var next = await AddFinishedJobAsync(_factory);
        await WaitUntilAsync(_factory, async db => !await db.Jobs.AnyAsync(j => j.Id == next.Id));

        await WaitUntilLoggedAsync(2);
        _client.LogEntries.Select(e => e.RequestMessage!.Headers![CallbackSigner.JobIdHeader].Single())
            .Should().Equal(duplicated.Id.ToString(), next.Id.ToString());
    }

    [Fact]
    public async Task Dispatch_PrivateAddressNotAllowed_IsRefusedWithoutRequest()
    {
        RespondWith(200);
        _factory.Settings["Api:AllowPrivateCallbackAddresses"] = "false";
        var job = await AddFinishedJobAsync(_factory);

        await WaitUntilAsync(_factory, async db => (await db.Jobs.SingleAsync(j => j.Id == job.Id)).Status == JobStatus.CallbackFailed);

        _client.LogEntries.Should().BeEmpty("loopback and private networks are off limits by default");
    }

    [Fact]
    public async Task Dispatch_TransientFailureThenSuccess_Delivers()
    {
        _client.Given(Request.Create().WithPath("/hook").UsingPost()).InScenario("flaky").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));
        _client.Given(Request.Create().WithPath("/hook").UsingPost()).InScenario("flaky").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200));
        var job = await AddFinishedJobAsync(_factory);

        await WaitUntilAsync(_factory, async db => !await db.Jobs.AnyAsync(j => j.Id == job.Id));

        await WaitUntilLoggedAsync(2);
        _client.LogEntries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dispatch_EndpointDown_MarksCallbackFailedAfterRetries()
    {
        RespondWith(500);
        var job = await AddFinishedJobAsync(_factory);

        await WaitUntilAsync(_factory, async db => (await db.Jobs.SingleAsync(j => j.Id == job.Id)).Status == JobStatus.CallbackFailed);

        // WireMock records a request after answering it, so the log can trail the database update.
        await WaitUntilLoggedAsync(3);
        _client.LogEntries.Should().HaveCount(3, "one attempt plus two retries");
    }

    [Fact]
    public async Task Start_UndeliveredCallbackFromBeforeRestart_IsDelivered()
    {
        RespondWith(200);
        var directory = Directory.CreateTempSubdirectory("restart-");
        var database = $"Data Source={Path.Combine(directory.FullName, "jobs.db")}";
        try
        {
            using (var beforeRestart = new ApiTestFactory())
            {
                beforeRestart.Settings["ConnectionStrings:Jobs"] = database;
                beforeRestart.Settings["CallbackDelivery:Retry:Delay"] = "00:00:00.010";
                await AddFinishedJobAsync(beforeRestart, enqueue: false);
            }

            using var restarted = new ApiTestFactory();
            restarted.Settings["ConnectionStrings:Jobs"] = database;
            restarted.CreateClient().Dispose();

            await WaitUntilAsync(restarted, async db => !await db.Jobs.AnyAsync());
            await WaitUntilLoggedAsync(1);
            _client.LogEntries.Should().ContainSingle();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    public void Dispose()
    {
        _factory.Dispose();
        _client.Dispose();
    }

    private void RespondWith(int status) =>
        _client.Given(Request.Create().WithPath("/hook").UsingPost()).RespondWith(Response.Create().WithStatusCode(status));

    private async Task<TranscriptionJob> AddFinishedJobAsync(ApiTestFactory factory, bool enqueue = true)
    {
        var job = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.Completed,
            Language = "pl",
            AudioPath = Path.Combine(factory.StoragePath, "gone.wav"),
            AudioDuration = TimeSpan.FromSeconds(1),
            CallbackUrl = new Uri($"{_client.Url}/hook"),
            CreatedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Engine = "whispernet",
            FullText = "dzień dobry",
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscriptionDbContext>();
        db.Jobs.Add(job);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        if (enqueue)
        {
            await DispatchAsync(factory, job.Id);
        }

        return job;
    }

    private Task WaitUntilLoggedAsync(int requests) => Eventually.TrueAsync(() => _client.LogEntries.Count >= requests);

    private static ValueTask DispatchAsync(ApiTestFactory factory, Guid jobId) =>
        factory.Services.GetRequiredService<PendingCallbacks>().EnqueueAsync(jobId, TestContext.Current.CancellationToken);

    private static Task WaitUntilAsync(ApiTestFactory factory, Func<TranscriptionDbContext, Task<bool>> condition) =>
        Eventually.TrueAsync(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await condition(scope.ServiceProvider.GetRequiredService<TranscriptionDbContext>());
        });
}
