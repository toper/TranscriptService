using Microsoft.AspNetCore.Builder;
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
}
