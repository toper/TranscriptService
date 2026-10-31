using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TranscriptService.Audio;
using TranscriptService.ServiceDefaults;
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

        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The load can fail while Kestrel is still binding; stopping then cancels the start itself.
        }

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
    public async Task Health_WhileModelLoading_LiveOnly_ThenReady()
    {
        await using var app = BuildWorker<SlowTranscriber>(new() { ["Engine:ModelPath"] = "/models/model.bin" });
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        ((SlowTranscriber)app.Services.GetRequiredService<ITranscriber>()).FinishLoading();
        await app.Services.GetRequiredService<ModelReadiness>().WaitAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        var readyAfterLoad = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        readyAfterLoad.StatusCode.Should().Be(HttpStatusCode.OK);
        await app.StopAsync(TestContext.Current.CancellationToken);
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
        app.MapDefaultEndpoints();
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

}
