namespace TranscriptService.Worker.Core.Tests;

public sealed class InFlightTrackerTests
{
    [Fact]
    public async Task WaitIdle_WaitsForRunningWork()
    {
        var tracker = new InFlightTracker();
        var work = tracker.Enter();

        var idle = tracker.WaitIdleAsync(TimeSpan.FromSeconds(5));
        idle.IsCompleted.Should().BeFalse();

        work.Dispose();
        (await idle).Should().BeTrue();
    }

    [Fact]
    public async Task WaitIdle_GivesUpAfterTimeout()
    {
        var tracker = new InFlightTracker();
        using var work = tracker.Enter();

        (await tracker.WaitIdleAsync(TimeSpan.FromMilliseconds(50))).Should().BeFalse();
    }

    [Fact]
    public async Task WaitIdle_NothingRunning_CompletesAtOnce() =>
        (await new InFlightTracker().WaitIdleAsync(TimeSpan.Zero)).Should().BeTrue();
}
