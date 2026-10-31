using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TranscriptService.Contracts.Http;
using TranscriptService.Demo.Web.Client.Transcriptions;

namespace TranscriptService.Demo.Web.Transcriptions;

/// <summary>Typed client of the Transcript API, as any third-party consumer would write it.</summary>
public sealed class TranscriptApiClient(HttpClient http, IOptions<TranscriptApiOptions> options)
{
    /// <summary>
    /// Submits a recording. Its result is delivered later to this demo's callback endpoint, with the
    /// browser session in the callback URL so the result can be routed without any server-side state.
    /// </summary>
    /// <exception cref="TranscriptApiException">The API rejected the request or could not be reached.</exception>
    public async Task<TranscriptionResponse> SubmitAsync(Stream audio, string fileName, Guid session, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var callback = new Uri(settings.PublicBaseUrl!, $"{DemoRoutes.Callbacks}?{DemoRoutes.SessionField}={session}");
        using var recording = new StreamContent(audio);
        recording.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var form = new MultipartFormDataContent
        {
            { recording, "audio", fileName },
            { new StringContent(settings.Language), "language" },
            { new StringContent(callback.ToString()), "callbackUrl" },
        };

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(new Uri("api/v1/transcriptions", UriKind.Relative), form, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new TranscriptApiException(StatusCodes.Status503ServiceUnavailable, $"The Transcript API is unreachable: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TranscriptApiException((int)response.StatusCode, await DescribeAsync(response, cancellationToken).ConfigureAwait(false));
            }

            return (await response.Content.ReadFromJsonAsync<TranscriptionResponse>(cancellationToken).ConfigureAwait(false))!;
        }
    }

    /// <summary>The API's explanation: problem detail, else validation errors, else the status line.</summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken).ConfigureAwait(false);
            if (problem?.Detail is { } detail)
            {
                return detail;
            }

            if (problem?.Errors.Count > 0)
            {
                return string.Join(" ", problem.Errors.SelectMany(error => error.Value));
            }
        }
        catch (JsonException)
        {
            // Not a problem response (e.g. an HTML error page from a proxy).
        }

        return $"The Transcript API answered {(int)response.StatusCode} {response.ReasonPhrase}.";
    }
}

/// <summary>The Transcript API answered with an error or could not be reached.</summary>
public sealed class TranscriptApiException(int statusCode, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
}
