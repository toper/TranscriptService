using EasyNetQ;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Layouts;
using NLog.Web;

namespace TranscriptService.ServiceDefaults;

/// <summary>Defaults every TranscriptService host starts with.</summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Health checks tagged with this value gate <c>/health/ready</c>.</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Structured JSON logs on stdout (NLog), the format container log collectors parse, plus the
    /// health check registry. Minimum levels still come from the <c>Logging</c> configuration section.
    /// </summary>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        LogManager.Setup().LoadConfiguration(config => config
            .ForLogger()
            .WriteToConsole(new JsonLayout
            {
                IncludeEventProperties = true,
                Attributes =
                {
                    new JsonAttribute("time", "${date:universalTime=true:format=o}"),
                    new JsonAttribute("level", "${level:upperCase=true}"),
                    new JsonAttribute("logger", "${logger}"),
                    new JsonAttribute("message", "${message}"),
                    new JsonAttribute("exception", "${exception:format=tostring}"),
                },
            }));
        builder.Logging.ClearProviders();
        builder.Host.UseNLog();

        builder.Services.AddHealthChecks();
        return builder;
    }

    /// <summary>
    /// Registers the EasyNetQ bus for <c>ConnectionStrings:RabbitMq</c> with System.Text.Json
    /// serialization, and makes broker connectivity part of readiness.
    /// </summary>
    public static WebApplicationBuilder AddMessaging(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("Connection string 'RabbitMq' is not configured.");
        builder.Services.AddEasyNetQ(connectionString).UseSystemTextJsonV2();
        builder.Services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [ReadyTag]);
        return builder;
    }

    /// <summary>
    /// <c>/health/live</c> answers while the process runs; <c>/health/ready</c> runs the checks tagged
    /// <see cref="ReadyTag"/>, so orchestration only routes work to a host that can do it.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
        return app;
    }
}
