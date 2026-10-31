using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TranscriptService.Audio;
using TranscriptService.Worker.Core.Health;
using TranscriptService.Worker.Core.Messaging;

namespace TranscriptService.Worker.Core.Tests;

public sealed class WorkerHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Start_TranscriberInitializationFails_StopsHost()
    {
        await using var app = BuildWorker<FailingTranscriber>(new() { ["Engine:ModelPath"] = "/models/missing.bin" });
        var stopping = new TaskCompletionSource();
        app.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());

        await app.StartAsync(TestContext.Current.CancellationToken);

        await stopping.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Start_InvalidEngineOptions_HostFailsToStart()
    {
        await using var app = BuildWorker<SlowTranscriber>(new() { ["Engine:ModelPath"] = string.Empty });

        await FluentActions.Awaiting(() => app.StartAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<OptionsValidationException>().WithMessage("*ModelPath*");
    }

    [Fact]
    public async Task Live_WhileModelLoading_RespondsOk_ReadyDoesNot()
    {
        await using var app = BuildWorker<SlowTranscriber>(new() { ["Engine:ModelPath"] = "/models/model.bin" });
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Readiness_FlipsOnlyAfterModelLoaded()
    {
        var readiness = new ModelReadiness();
        var check = new ModelReadyHealthCheck(readiness);
        var transcriber = new SlowTranscriber();
        using var initialization = new ModelInitializationService(
            transcriber, readiness, new ApplicationLifetimeStub(), NullLogger<ModelInitializationService>.Instance);

        await initialization.StartAsync(TestContext.Current.CancellationToken);
        (await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken))
            .Status.Should().Be(HealthStatus.Unhealthy);

        transcriber.FinishLoading();
        await readiness.WaitAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        (await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken))
            .Status.Should().Be(HealthStatus.Healthy);
    }

    private static WebApplication BuildWorker<TTranscriber>(Dictionary<string, string?> settings)
        where TTranscriber : class, ITranscriber
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        settings["ConnectionStrings:RabbitMq"] = "host=localhost";
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddTranscriptionWorker<TTranscriber, EngineOptions>();

        // No broker in unit tests: drop the consumer, keep everything else.
        var consumer = builder.Services.Single(d => d.ImplementationType == typeof(TranscriptionRequestConsumer));
        builder.Services.Remove(consumer);

        var app = builder.Build();
        app.MapWorkerEndpoints();
        return app;
    }

    private sealed class SlowTranscriber : ITranscriber
    {
        private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string EngineName => "slow";

        public void FinishLoading() => _loaded.TrySetResult();

        public Task InitializeAsync(CancellationToken cancellationToken) => _loaded.Task.WaitAsync(cancellationToken);

        public Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken) =>
            Task.FromResult(new TranscriptionOutput(string.Empty, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingTranscriber(IOptions<EngineOptions> options) : ITranscriber
    {
        public string EngineName => "failing";

        public Task InitializeAsync(CancellationToken cancellationToken) =>
            throw new FileNotFoundException("Model file not found.", options.Value.ModelPath);

        public Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ApplicationLifetimeStub : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
