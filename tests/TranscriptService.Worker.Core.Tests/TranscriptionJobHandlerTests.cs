using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TranscriptService.Audio;
using TranscriptService.Audio.Tests;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Messages;
using TranscriptService.Worker.Core.Messaging;

namespace TranscriptService.Worker.Core.Tests;

public sealed class TranscriptionJobHandlerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeTranscriber _transcriber;
    private readonly RecordingPublisher _publisher = new();
    private readonly TranscriptionJobHandler _handler;
    private readonly string _oneSecondAt8kHz = WavFixtures.WriteTemp(WavFixtures.Pcm16(new float[8000], 8000));

    public TranscriptionJobHandlerTests()
    {
        _transcriber = new FakeTranscriber(_time);
        _handler = new TranscriptionJobHandler(_transcriber, _publisher, _time, NullLogger<TranscriptionJobHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidAudio_PublishesCompletedWithEngineAndDurations()
    {
        _transcriber.Output = new TranscriptionOutput("dzień dobry", [new TranscriptSegment("dzień dobry", TimeSpan.Zero, TimeSpan.FromSeconds(1))]);
        _transcriber.Takes = TimeSpan.FromSeconds(2);
        var command = Command(_oneSecondAt8kHz);

        await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var completed = _publisher.Completed.Should().ContainSingle().Subject;
        completed.JobId.Should().Be(command.JobId);
        completed.Engine.Should().Be(FakeTranscriber.Name);
        completed.FullText.Should().Be("dzień dobry");
        completed.Segments.Should().ContainSingle();
        completed.AudioDuration.Should().BeCloseTo(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
        completed.ProcessingTime.Should().Be(TimeSpan.FromSeconds(2));
        _transcriber.ReceivedSampleRate.Should().Be(AudioPreprocessor.TargetSampleRate);
        _transcriber.ReceivedLanguage.Should().Be("pl");
    }

    [Fact]
    public async Task Handle_SilentAudio_PublishesCompletedWithEmptyText()
    {
        _transcriber.Output = new TranscriptionOutput(string.Empty, []);

        await _handler.HandleAsync(Command(_oneSecondAt8kHz), TestContext.Current.CancellationToken);

        var completed = _publisher.Completed.Should().ContainSingle().Subject;
        completed.FullText.Should().BeEmpty();
        completed.Segments.Should().BeEmpty();
        _publisher.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnsupportedAudio_PublishesFailedWithReason()
    {
        var path = WavFixtures.WriteTemp("not a wav file"u8.ToArray());

        await _handler.HandleAsync(Command(path), TestContext.Current.CancellationToken);

        _publisher.Failed.Should().ContainSingle().Which.Reason.Should().Contain("not a valid WAV");
        _publisher.Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingFile_PublishesFailed()
    {
        await _handler.HandleAsync(Command(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav")), TestContext.Current.CancellationToken);

        _publisher.Failed.Should().ContainSingle().Which.Reason.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_LanguageTheEngineDoesNotKnow_PublishesFailedWithoutTranscribing()
    {
        await _handler.HandleAsync(Command(_oneSecondAt8kHz) with { Language = "xx" }, TestContext.Current.CancellationToken);

        _publisher.Failed.Should().ContainSingle().Which.Reason.Should().Contain("'xx'").And.Contain("en, pl");
        _transcriber.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EngineRejectsRequest_PublishesFailedWithReason()
    {
        _transcriber.Throw = new TranscriptionRejectedException("The deployed Vosk model recognizes 'pl' only; requested 'en'.");

        await _handler.HandleAsync(Command(_oneSecondAt8kHz), TestContext.Current.CancellationToken);

        _publisher.Failed.Should().ContainSingle().Which.Reason.Should().Be(_transcriber.Throw.Message);
    }

    [Fact]
    public async Task Handle_LibraryThrowsNotSupported_PublishesGenericReason()
    {
        _transcriber.Throw = new NotSupportedException("Specified method is not supported.");

        await _handler.HandleAsync(Command(_oneSecondAt8kHz), TestContext.Current.CancellationToken);

        _publisher.Failed.Should().ContainSingle().Which.Reason.Should().NotContain("Specified method");
    }

    [Fact]
    public async Task Handle_PublishingCompletedFails_PropagatesWithoutReportingEngineFailure()
    {
        _publisher.FailCompleted = new InvalidOperationException("Broker unreachable");

        await FluentActions.Awaiting(() => _handler.HandleAsync(Command(_oneSecondAt8kHz), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        _publisher.Failed.Should().BeEmpty("the transcription succeeded; the broker redelivers the job instead");
    }

    [Fact]
    public async Task Handle_HostCancellation_Propagates_AndPublishesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => _handler.HandleAsync(Command(_oneSecondAt8kHz), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        _publisher.Completed.Should().BeEmpty();
        _publisher.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TranscriberThrows_PublishesFailedWithoutLeakingStackTrace()
    {
        _transcriber.Throw = new InvalidOperationException("CUDA error 700 at ggml-cuda.cu:123");

        await _handler.HandleAsync(Command(_oneSecondAt8kHz), TestContext.Current.CancellationToken);

        var failed = _publisher.Failed.Should().ContainSingle().Subject;
        failed.Engine.Should().Be(FakeTranscriber.Name);
        failed.Reason.Should().NotContain("CUDA").And.NotContain("ggml");
    }

    public void Dispose() => File.Delete(_oneSecondAt8kHz);

    private static TranscribeAudio Command(string path) => new(Guid.NewGuid(), path, "pl", TimeSpan.FromSeconds(1));

    private sealed class FakeTranscriber(FakeTimeProvider time) : ITranscriber
    {
        public const string Name = "fake";

        public TranscriptionOutput Output { get; set; } = new(string.Empty, []);

        public TimeSpan Takes { get; set; }

        public Exception? Throw { get; set; }

        public int ReceivedSampleRate { get; private set; }

        public string? ReceivedLanguage { get; private set; }

        public string EngineName => Name;

        public IReadOnlySet<string> SupportedLanguages { get; } = new HashSet<string> { "pl", "en" };

        public int Calls { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedSampleRate = audio.SampleRate;
            ReceivedLanguage = language;
            time.Advance(Takes);
            return Throw is null ? Task.FromResult(Output) : Task.FromException<TranscriptionOutput>(Throw);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingPublisher : ITranscriptionResultPublisher
    {
        public List<TranscriptionCompleted> Completed { get; } = [];

        public List<TranscriptionFailed> Failed { get; } = [];

        public Exception? FailCompleted { get; set; }

        public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
            where TMessage : class
        {
            switch (message)
            {
                case TranscriptionCompleted when FailCompleted is not null:
                    return Task.FromException(FailCompleted);
                case TranscriptionCompleted completed:
                    Completed.Add(completed);
                    break;
                case TranscriptionFailed failed:
                    Failed.Add(failed);
                    break;
            }

            return Task.CompletedTask;
        }
    }
}
