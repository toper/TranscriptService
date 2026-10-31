using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TranscriptService.Audio.Tests;
using TranscriptService.Contracts.Http;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Tests.Endpoints;

public sealed class TranscriptionEndpointsTests : IDisposable
{
    private static readonly Uri Transcriptions = new("/api/v1/transcriptions", UriKind.Relative);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiTestFactory _factory = new();

    [Fact]
    public async Task Post_Valid8kHzWav_Returns202WithLocation_StoresFile_PublishesCommand()
    {
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(2)), callbackUrl: "https://client.example/hook"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<TranscriptionResponse>(Json, TestContext.Current.CancellationToken);
        body!.Status.Should().Be(JobStatus.Queued);
        body.Language.Should().Be("pl");
        body.AudioDuration.Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(5));
        response.Headers.Location.Should().Be(new Uri($"/api/v1/transcriptions/{body.Id}", UriKind.Relative));

        var (command, _) = _factory.Publisher.Published.Should().ContainSingle().Subject;
        command.JobId.Should().Be(body.Id);
        command.Language.Should().Be("pl");
        File.Exists(command.AudioPath).Should().BeTrue();
        Path.GetDirectoryName(command.AudioPath).Should().Be(_factory.StoragePath);
    }

    [Fact]
    public async Task Post_ShortAudio_PublishesHighPriority()
    {
        using var client = _factory.CreateAuthorizedClient();

        await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(5))), TestContext.Current.CancellationToken);

        _factory.Publisher.Published.Should().ContainSingle().Which.Priority.Should().Be(MessagePriority.High);
    }

    [Fact]
    public async Task Post_LongAudio_PublishesNormalPriority()
    {
        using var client = _factory.CreateAuthorizedClient();

        await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(31))), TestContext.Current.CancellationToken);

        _factory.Publisher.Published.Should().ContainSingle().Which.Priority.Should().Be(MessagePriority.Normal);
    }

    [Fact]
    public async Task Post_AcceptsOctetStreamWav()
    {
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(1)), contentType: "application/octet-stream"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Post_UnsupportedFormat_Returns400ProblemDetailsWithReason()
    {
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(WavFixtures.IeeeFloat(new float[8000], 8000)), TestContext.Current.CancellationToken);

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        problem.Detail.Should().Contain("IeeeFloat");
        _factory.Publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_MissingAudio_Returns400()
    {
        using var client = _factory.CreateAuthorizedClient();
        using var form = new MultipartFormDataContent { { new StringContent("pl"), "language" } };

        var response = await client.PostAsync(Transcriptions, form, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_TooLong_Returns400()
    {
        _factory.Settings["Api:MaxAudioDuration"] = "00:00:02";
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(3))), TestContext.Current.CancellationToken);

        (await ProblemAsync(response, HttpStatusCode.BadRequest)).Detail.Should().Contain("00:00:02");
    }

    [Fact]
    public async Task Post_TooLarge_Returns413()
    {
        _factory.Settings["Api:MaxUploadBytes"] = "1000";
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(1))), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Theory]
    [InlineData("ftp://client.example/hook")]
    [InlineData("/relative/hook")]
    [InlineData("not a url")]
    public async Task Post_InvalidCallbackUrl_Returns400(string callbackUrl)
    {
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(1)), callbackUrl: callbackUrl), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_InvalidLanguage_Returns400()
    {
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(1)), language: "polish"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_PublishFails_Returns503_AndCleansUp()
    {
        _factory.Publisher.Fail = new InvalidOperationException("Broker unreachable");
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(1))), TestContext.Current.CancellationToken);

        await ProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Directory.EnumerateFiles(_factory.StoragePath).Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Unknown_Returns404ProblemDetails()
    {
        using var client = _factory.CreateAuthorizedClient();

        var response = await client.GetAsync(new Uri($"{Transcriptions}/{Guid.NewGuid()}", UriKind.Relative), TestContext.Current.CancellationToken);

        await ProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Queued_ReturnsJob()
    {
        using var client = _factory.CreateAuthorizedClient();
        var created = await (await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(1))), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<TranscriptionResponse>(Json, TestContext.Current.CancellationToken);

        var job = await client.GetFromJsonAsync<TranscriptionResponse>(new Uri($"{Transcriptions}/{created!.Id}", UriKind.Relative), Json, TestContext.Current.CancellationToken);

        job!.Id.Should().Be(created.Id);
        job.Status.Should().Be(JobStatus.Queued);
    }

    [Fact]
    public async Task Delete_RemovesJobAndFile()
    {
        using var client = _factory.CreateAuthorizedClient();
        var created = await (await client.PostAsync(Transcriptions, Form(Wav(TimeSpan.FromSeconds(1))), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<TranscriptionResponse>(Json, TestContext.Current.CancellationToken);
        var jobUri = new Uri($"{Transcriptions}/{created!.Id}", UriKind.Relative);

        var deleted = await client.DeleteAsync(jobUri, TestContext.Current.CancellationToken);
        var afterwards = await client.GetAsync(jobUri, TestContext.Current.CancellationToken);

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterwards.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Directory.EnumerateFiles(_factory.StoragePath).Should().BeEmpty();
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTranscriptionEndpoints()
    {
        using var client = _factory.CreateClient();

        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");

        var post = paths.GetProperty("/api/v1/transcriptions").GetProperty("post");
        post.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        post.GetProperty("responses").EnumerateObject().Select(r => r.Name).Should().Contain(["202", "400", "401", "413", "503"]);
        paths.GetProperty("/api/v1/transcriptions/{id}").EnumerateObject().Select(o => o.Name).Should().Contain(["get", "delete"]);
    }

    public void Dispose() => _factory.Dispose();

    private static byte[] Wav(TimeSpan duration) => WavFixtures.Pcm16(WavFixtures.Sine(440, 8000, duration), 8000);

    private static MultipartFormDataContent Form(byte[] wav, string? language = null, string? callbackUrl = null, string contentType = "audio/wav")
    {
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { audio, "audio", "call.wav" } };
        if (language is not null)
        {
            form.Add(new StringContent(language), "language");
        }

        if (callbackUrl is not null)
        {
            form.Add(new StringContent(callbackUrl), "callbackUrl");
        }

        return form;
    }

    private static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))!;
    }
}
