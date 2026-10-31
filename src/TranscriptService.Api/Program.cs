using Microsoft.AspNetCore.Authentication;
using Scalar.AspNetCore;
using TranscriptService.Api.Endpoints;
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

builder.Services.AddSingleton<ITranscriptionRequestPublisher, EasyNetQRequestPublisher>();

var app = builder.Build();

app.UseExceptionHandler();
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
