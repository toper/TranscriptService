using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TranscriptService.Api.Messaging;

namespace TranscriptService.Api.Tests;

/// <summary>
/// The API in-process with a temporary SQLite database and audio folder. RabbitMQ is replaced:
/// requests go to <see cref="Publisher"/> and the result subscriber is not started.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-0123456789";
    public const string SigningSecret = "test-signing-secret-0123456789abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"api-tests-{Guid.NewGuid():N}");

    public RecordingRequestPublisher Publisher { get; } = new();

    public string StoragePath => Path.Combine(_root, "audio");

    public Dictionary<string, string?> Settings { get; } = [];

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", ApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(StoragePath);
        var defaults = new Dictionary<string, string?>
        {
            ["Api:ApiKey"] = ApiKey,
            ["Api:CallbackSigningSecret"] = SigningSecret,
            // Test callback receivers listen on loopback.
            ["Api:AllowPrivateCallbackAddresses"] = "true",
            ["Api:StoragePath"] = StoragePath,
            ["ConnectionStrings:Jobs"] = $"Data Source={Path.Combine(_root, "jobs.db")}",
            ["ConnectionStrings:RabbitMq"] = "host=localhost",
        };
        foreach (var (key, value) in defaults.Concat(Settings))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITranscriptionRequestPublisher>();
            services.AddSingleton<ITranscriptionRequestPublisher>(Publisher);
            services.Remove(services.Single(d => d.ImplementationType == typeof(TranscriptionResultSubscriber)));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
