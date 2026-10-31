using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Http;
using TranscriptService.Demo.Web.Client.Pages;
using TranscriptService.Demo.Web.Client.Recording;
using TranscriptService.Demo.Web.Client.Transcriptions;

namespace TranscriptService.Demo.Web.Tests;

public sealed class HomePageTests : IDisposable
{
    private readonly BunitContext _context = new();
    private readonly FakeMicrophone _microphone = new();
    private readonly FakeSession _session = new();

    public HomePageTests()
    {
        _context.Services.AddSingleton<IMicrophone>(_microphone);
        _context.Services.AddSingleton<ITranscriptionSession>(_session);
    }

    [Fact]
    public async Task Utterance_IsShownAsPending_ThenWithItsTranscript()
    {
        var page = _context.Render<Home>();
        page.Find("button#record").Click();

        await page.InvokeAsync(() => _microphone.SpeakAsync());
        page.WaitForAssertion(() => page.Markup.Should().Contain("Transcribing"));

        await page.InvokeAsync(() => _session.FinishAsync(Completed(_session.Submitted.Single(), "Ile kosztuje paleta do Litwy?")));
        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("Ile kosztuje paleta do Litwy?").And.Contain("whispernet");
            page.Markup.Should().NotContain("Transcribing");
        });
    }

    [Fact]
    public async Task ResultArrivingBeforeTheUploadReturns_IsStillShown()
    {
        _session.FinishBeforeReturning = id => Completed(id, "Szybsze niż odpowiedź HTTP");
        var page = _context.Render<Home>();
        page.Find("button#record").Click();

        await page.InvokeAsync(() => _microphone.SpeakAsync());

        page.WaitForAssertion(() => page.Markup.Should().Contain("Szybsze niż odpowiedź HTTP").And.NotContain("Transcribing"));
    }

    [Fact]
    public async Task FailedTranscription_ShowsTheReason()
    {
        var page = _context.Render<Home>();
        page.Find("button#record").Click();
        await page.InvokeAsync(() => _microphone.SpeakAsync());

        var failed = Completed(_session.Submitted.Single(), null) with { Status = JobStatus.Failed, FailureReason = "The recording is empty." };
        await page.InvokeAsync(() => _session.FinishAsync(failed));

        page.WaitForAssertion(() => page.Markup.Should().Contain("The recording is empty."));
    }

    [Fact]
    public void RecordButton_TogglesTheMicrophone()
    {
        var page = _context.Render<Home>();

        page.Find("button#record").Click();
        _microphone.Listening.Should().BeTrue();

        page.Find("button#record").Click();
        _microphone.Listening.Should().BeFalse();
    }

    public void Dispose() => _context.Dispose();

    private static TranscriptionResponse Completed(Guid id, string? text) => new(
        id, JobStatus.Completed, "pl", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "whispernet", text,
        text is null ? [] : [new TranscriptSegment(text, TimeSpan.Zero, TimeSpan.FromSeconds(1))],
        null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200));

    private sealed class FakeMicrophone : IMicrophone
    {
        public event Func<byte[], Task>? UtteranceRecorded;

        public bool Listening { get; private set; }

        public Task SpeakAsync() => UtteranceRecorded!.Invoke([1, 2, 3]);

        public Task StartAsync()
        {
            Listening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Listening = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSession : ITranscriptionSession
    {
        public event Func<TranscriptionResponse, Task>? Finished;

        public List<Guid> Submitted { get; } = [];

        public Func<Guid, TranscriptionResponse>? FinishBeforeReturning { get; set; }

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<Guid> SubmitAsync(Stream wav, string fileName, CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            Submitted.Add(id);
            if (FinishBeforeReturning is not null)
            {
                await FinishAsync(FinishBeforeReturning(id));
            }

            return id;
        }

        public Task FinishAsync(TranscriptionResponse transcription) => Finished!.Invoke(transcription);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
