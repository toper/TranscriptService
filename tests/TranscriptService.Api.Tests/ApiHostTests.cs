using System.Net;
using System.Text.Json;

namespace TranscriptService.Api.Tests;

public sealed class ApiHostTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    [Fact]
    public async Task ApiGroup_WithoutKey_Returns401ProblemDetails()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/v1/transcriptions/{Guid.NewGuid()}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ApiGroup_WithWrongKey_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", "wrong-key-wrong-key");

        var response = await client.GetAsync(new Uri($"/api/v1/transcriptions/{Guid.NewGuid()}", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthLive_WithoutKey_Returns200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesApiKeySecurity()
    {
        using var client = _factory.CreateClient();

        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        scheme.GetProperty("type").GetString().Should().Be("apiKey");
        scheme.GetProperty("name").GetString().Should().Be("X-API-KEY");
        scheme.GetProperty("in").GetString().Should().Be("header");
    }

    [Fact]
    public void Start_MissingApiKey_FailsToStart()
    {
        using var factory = new ApiTestFactory();
        factory.Settings["Api:ApiKey"] = string.Empty;

        FluentActions.Invoking(() => factory.CreateClient())
            .Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().WithMessage("*ApiKey*");
    }

    public void Dispose() => _factory.Dispose();
}
