using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TranscriptService.Contracts.Http;
using TranscriptService.Demo.Web.Client.Transcriptions;

namespace TranscriptService.Demo.Web.Transcriptions;

/// <summary>Typed client of the Transcript API, as any third-party consumer would write it.</summary>
public sealed class TranscriptApiClient(HttpClient http, IOptions<TranscriptApiOptions> options)
{
    /// <summary>Submits a recording; the result is delivered later to this demo's callback endpoint.</summary>
    /// <exception cref="TranscriptApiException">The API rejected the request.</exception>
    public async Task<TranscriptionResponse> SubmitAsync(Stream audio, string fileName, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var recording = new StreamContent(audio);
        recording.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var form = new MultipartFormDataContent
        {
            { recording, "audio", fileName },
            { new StringContent(settings.Language), "language" },
            { new StringContent(new Uri(settings.PublicBaseUrl!, DemoRoutes.Callbacks).ToString()), "callbackUrl" },
        };

        using var response = await http.PostAsync(new Uri("api/v1/transcriptions", UriKind.Relative), form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken).ConfigureAwait(false);
            throw new TranscriptApiException((int)response.StatusCode, problem?.Detail ?? response.ReasonPhrase ?? "The Transcript API rejected the request.");
        }

        return (await response.Content.ReadFromJsonAsync<TranscriptionResponse>(cancellationToken).ConfigureAwait(false))!;
    }
}

/// <summary>The Transcript API answered with an error; carries its status and explanation.</summary>
public sealed class TranscriptApiException(int statusCode, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
}
