namespace TranscriptService.ServiceDefaults;

/// <summary>
/// Container health check without curl or a shell: the image runs its own entry point with
/// <c>--health-probe</c>, which queries <c>/health/ready</c> and exits with 0 (healthy) or 1.
/// Chiseled images ship neither tool, and adding one would undo their small attack surface.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--health-probe";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>Runs the probe when <paramref name="args"/> ask for it; otherwise returns <see langword="null"/>.</summary>
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (!args.Contains(Argument, StringComparer.Ordinal))
        {
            return null;
        }

        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';', ',')[0] ?? "8080";
        return await ProbeAsync(new Uri($"http://127.0.0.1:{port}/health/ready"), CancellationToken.None).ConfigureAwait(false);
    }

    public static async Task<int> ProbeAsync(Uri readinessUri, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = Timeout };
        try
        {
            using var response = await http.GetAsync(readinessUri, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
