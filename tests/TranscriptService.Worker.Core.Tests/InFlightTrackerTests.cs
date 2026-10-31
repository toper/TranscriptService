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
    public async Task DisposeWhenIdle_FreesNativeResourcesOnlyAfterRunningWorkEnds()
    {
        var tracker = new InFlightTracker();
        var work = tracker.Enter();
        var freed = false;

        var disposing = tracker.DisposeWhenIdleAsync(() => freed = true).AsTask();
        freed.Should().BeFalse();

        work.Dispose();
        await disposing;
        freed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeWhenIdle_WorkNeverEnds_LeavesResourcesAlone()
    {
        var tracker = new InFlightTracker();
        using var stuck = tracker.Enter();
        var freed = false;

        await tracker.DisposeWhenIdleAsync(() => freed = true, TimeSpan.FromMilliseconds(50));

        freed.Should().BeFalse("freeing weights under a running native call would crash the process");
    }

    [Fact]
    public async Task WaitIdle_NothingRunning_CompletesAtOnce() =>
        (await new InFlightTracker().WaitIdleAsync(TimeSpan.Zero)).Should().BeTrue();
}
