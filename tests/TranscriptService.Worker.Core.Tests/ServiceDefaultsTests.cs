using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TranscriptService.ServiceDefaults;

namespace TranscriptService.Worker.Core.Tests;

public sealed class ServiceDefaultsTests
{
    [Fact]
    public async Task Logging_RespectsLevelsFromConfiguration()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Warning" });
        builder.AddServiceDefaults();
        await using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILogger<ServiceDefaultsTests>>();

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public async Task HealthProbe_ReadyHost_ReturnsZero()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var exitCode = await HealthProbe.ProbeAsync(new Uri(new Uri(app.Urls.First()), "/health/ready"), TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task HealthProbe_NothingListening_ReturnsOne() =>
        (await HealthProbe.ProbeAsync(new Uri("http://127.0.0.1:1/health/ready"), TestContext.Current.CancellationToken)).Should().Be(1);

    [Fact]
    public async Task HealthProbe_WithoutArgument_DoesNotRun() =>
        (await HealthProbe.TryRunAsync(["--urls", "http://+:8080"])).Should().BeNull();
}
