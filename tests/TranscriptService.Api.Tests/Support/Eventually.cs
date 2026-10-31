namespace TranscriptService.Api.Tests.Support;

/// <summary>Waits for a condition that background services make true, failing after a timeout.</summary>
internal static class Eventually
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    public static Task TrueAsync(Func<bool> condition) => TrueAsync(() => Task.FromResult(condition()));

    public static async Task TrueAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(DefaultTimeout);
        while (!await condition())
        {
            await Task.Delay(PollInterval, timeout.Token);
        }
    }
}
