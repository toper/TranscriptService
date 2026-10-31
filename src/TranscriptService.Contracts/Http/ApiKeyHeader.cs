namespace TranscriptService.Contracts.Http;

/// <summary>How clients authenticate to the Transcript API.</summary>
public static class ApiKeyHeader
{
    /// <summary>Header carrying the API key on every <c>/api/v1</c> request.</summary>
    public const string Name = "X-API-KEY";
}
