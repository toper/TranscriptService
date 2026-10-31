using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TranscriptService.ServiceDefaults;
using TranscriptService.Worker.Core.Health;
using TranscriptService.Worker.Core.Messaging;

namespace TranscriptService.Worker.Core;

/// <summary>Turns a minimal web host into an engine worker, so each engine's Program.cs is a few lines.</summary>
public static class WorkerHostExtensions
{
    /// <summary>
    /// Registers the engine, its validated options (section <c>Engine</c>), model warm-up,
    /// RabbitMQ consumption and health checks.
    /// </summary>
    public static WebApplicationBuilder AddTranscriptionWorker<TTranscriber, TEngineOptions>(this WebApplicationBuilder builder)
        where TTranscriber : class, ITranscriber
        where TEngineOptions : EngineOptions
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceDefaults().AddMessaging();

        var services = builder.Services;
        services.AddOptions<TEngineOptions>()
            .Bind(builder.Configuration.GetSection(EngineOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        if (typeof(TEngineOptions) != typeof(EngineOptions))
        {
            services.AddSingleton<IOptions<EngineOptions>>(sp => sp.GetRequiredService<IOptions<TEngineOptions>>());
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITranscriber, TTranscriber>();
        services.AddSingleton<ModelReadiness>();
        services.AddSingleton<TranscriptionJobHandler>();
        services.AddSingleton<ITranscriptionResultPublisher, EasyNetQResultPublisher>();

        // Order matters: the model loads (StartingAsync) before the consumer starts taking jobs.
        services.AddHostedService<ModelInitializationService>();
        services.AddHostedService<TranscriptionRequestConsumer>();

        services.AddHealthChecks().AddCheck<ModelReadyHealthCheck>("model", tags: [ServiceDefaultsExtensions.ReadyTag]);
        return builder;
    }

    /// <summary>Maps the worker's only HTTP surface: health endpoints for orchestration.</summary>
    public static WebApplication MapWorkerEndpoints(this WebApplication app) => app.MapDefaultEndpoints();
}
