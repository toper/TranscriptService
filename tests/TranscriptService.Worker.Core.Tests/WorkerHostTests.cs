using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TranscriptService.Audio;
using TranscriptService.Worker.Core.Health;
using TranscriptService.Worker.Core.Messaging;

namespace TranscriptService.Worker.Core.Tests;

public sealed class WorkerHostTests
{
    [Fact]
    public async Task Start_TranscriberInitializationFails_HostFailsToStart()
    {
        await using var app = BuildWorker<FailingTranscriber>(new() { ["Engine:ModelPath"] = "/models/missing.bin" });

        await FluentActions.Awaiting(() => app.StartAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Start_InvalidEngineOptions_HostFailsToStart()
    {
        await using var app = BuildWorker<ReadyTranscriber>(new() { ["Engine:ModelPath"] = string.Empty });

        await FluentActions.Awaiting(() => app.StartAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<OptionsValidationException>().WithMessage("*ModelPath*");
    }

    [Fact]
    public async Task Ready_BeforeModelLoaded_IsUnhealthy_AfterwardsHealthy()
    {
        var readiness = new ModelReadiness();
        var check = new ModelReadyHealthCheck(readiness);
        var initialization = new ModelInitializationService(new ReadyTranscriber(), readiness);

        (await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken))
            .Status.Should().Be(HealthStatus.Unhealthy);

        await initialization.StartingAsync(TestContext.Current.CancellationToken);

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

        // No broker in unit tests: drop the consumer, keep everything that runs before it.
        var consumer = builder.Services.Single(d => d.ImplementationType == typeof(TranscriptionRequestConsumer));
        builder.Services.Remove(consumer);

        var app = builder.Build();
        app.MapWorkerEndpoints();
        return app;
    }

    private sealed class ReadyTranscriber : ITranscriber
    {
        public string EngineName => "ready";

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
}
