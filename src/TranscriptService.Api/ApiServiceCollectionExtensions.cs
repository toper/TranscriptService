using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Messaging;
using TranscriptService.Api.OpenApi;
using TranscriptService.Api.Options;
using TranscriptService.Api.Retention;
using TranscriptService.Api.Security;
using TranscriptService.ServiceDefaults;

namespace TranscriptService.Api;

/// <summary>Composition of the API, one method per area, so Program.cs reads as a table of contents.</summary>
internal static class ApiServiceCollectionExtensions
{
    /// <summary>Room for multipart framing and form fields on top of the recording itself.</summary>
    private const long MultipartHeadroomBytes = 1024 * 1024;

    /// <summary>Validated <see cref="ApiOptions"/> and the transport limits derived from them.</summary>
    public static IServiceCollection AddApiOptions(this IServiceCollection services)
    {
        services.AddOptions<ApiOptions>()
            .BindConfiguration(ApiOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The endpoint enforces the exact recording limit with a 413 problem response; the
        // transport limits only stop requests that could not possibly be accepted.
        services.AddOptions<KestrelServerOptions>().Configure<IOptions<ApiOptions>>((kestrel, api) =>
            kestrel.Limits.MaxRequestBodySize = api.Value.MaxUploadBytes + MultipartHeadroomBytes);
        services.AddOptions<FormOptions>().Configure<IOptions<ApiOptions>>((form, api) =>
            form.MultipartBodyLengthLimit = api.Value.MaxUploadBytes + MultipartHeadroomBytes);
        return services;
    }

    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);
        return services.AddAuthorization();
    }

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services) =>
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer<ApiDocumentInfoTransformer>();
            options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>();
        });

    /// <summary>SQLite job store (migrated at startup), recording storage and submission.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TranscriptionDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Jobs")
                ?? throw new InvalidOperationException("Connection string 'Jobs' is not configured.")));
        services.AddHostedService<DatabaseMigrationService>();
        services.AddHealthChecks().AddDbContextCheck<TranscriptionDbContext>("database", tags: [ServiceDefaultsExtensions.ReadyTag]);
        services.AddSingleton<AudioStorage>();
        services.AddSingleton(TimeProvider.System);
        return services.AddScoped<TranscriptionSubmission>();
    }

    /// <summary>Requests to the engine workers and their result events.</summary>
    public static IServiceCollection AddTranscriptionMessaging(this IServiceCollection services)
    {
        services.AddSingleton<ITranscriptionRequestPublisher, EasyNetQRequestPublisher>();
        services.AddScoped<TranscriptionResultHandler>();
        return services.AddHostedService<TranscriptionResultSubscriber>();
    }

    /// <summary>Signed callbacks: retries and timeouts from the <c>CallbackDelivery</c> section, one circuit per receiver.</summary>
    public static IServiceCollection AddCallbackDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<PendingCallbacks>();
        services.AddHostedService<CallbackDispatcher>();
        services.AddHttpClient(CallbackDispatcher.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider =>
                CallbackAddressGuard.CreateHandler(provider.GetRequiredService<IOptions<ApiOptions>>().Value.AllowPrivateCallbackAddresses))
            .AddStandardResilienceHandler(configuration.GetSection("CallbackDelivery"))
            .SelectPipelineByAuthority();
        return services;
    }

    public static IServiceCollection AddRetention(this IServiceCollection services) =>
        services.AddScoped<RetentionCleaner>().AddHostedService<RetentionCleanupService>();
}
