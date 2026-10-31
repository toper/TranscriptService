using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Endpoints;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Messaging;
using TranscriptService.Api.OpenApi;
using TranscriptService.Api.Options;
using TranscriptService.Api.Security;
using TranscriptService.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults().AddMessaging();

builder.Services.AddOptions<ApiOptions>()
    .BindConfiguration(ApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The endpoint enforces the exact recording limit with a 413 problem response; the transport
// limits only add headroom for multipart framing and form fields.
var maxRequestBytes = (builder.Configuration.GetValue<long?>("Api:MaxUploadBytes") ?? new ApiOptions().MaxUploadBytes) + (1024 * 1024);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxRequestBytes);
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maxRequestBytes);
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddAuthorization();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "TranscriptService API";
        document.Info.Description = "Asynchronous transcription of telephone recordings: WAV with PCM 16-bit or G.711 μ-law/A-law samples, any sample rate (8 kHz telephony included), mono or stereo.";
        return Task.CompletedTask;
    });
    options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>();
});

builder.Services.AddDbContext<TranscriptionDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Jobs")
        ?? throw new InvalidOperationException("Connection string 'Jobs' is not configured.")));
builder.Services.AddHostedService<DatabaseMigrationService>();
builder.Services.AddHealthChecks().AddDbContextCheck<TranscriptionDbContext>("database", tags: [ServiceDefaultsExtensions.ReadyTag]);
builder.Services.AddSingleton<AudioStorage>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<ITranscriptionRequestPublisher, EasyNetQRequestPublisher>();
builder.Services.AddScoped<TranscriptionResultHandler>();
builder.Services.AddHostedService<TranscriptionResultSubscriber>();
builder.Services.AddSingleton<PendingCallbacks>();
builder.Services.AddHostedService<CallbackDispatcher>();
builder.Services.AddHttpClient(CallbackDispatcher.HttpClientName)
    .AddStandardResilienceHandler(builder.Configuration.GetSection("CallbackDelivery"));

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Malformed requests (e.g. a broken multipart body) are the client's fault, not a 500.
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("TranscriptService API")
    .AddPreferredSecuritySchemes(ApiKeyAuthenticationHandler.SchemeName));
app.MapDefaultEndpoints();
app.MapGroup("/api/v1").RequireAuthorization().MapTranscriptionEndpoints();

await app.RunAsync().ConfigureAwait(false);
