using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITranscriber, TTranscriber>();
        services.AddSingleton<ModelReadiness>();
        services.AddSingleton<TranscriptionJobHandler>();
        services.AddSingleton<ITranscriptionResultPublisher, EasyNetQResultPublisher>();

        // The consumer waits for ModelReadiness, so jobs are only taken once the model is loaded.
        services.AddHostedService<ModelInitializationService>();
        services.AddHostedService<TranscriptionRequestConsumer>();

        services.AddHealthChecks().AddCheck<ModelReadyHealthCheck>("model", tags: [ServiceDefaultsExtensions.ReadyTag]);
        return builder;
    }
}
