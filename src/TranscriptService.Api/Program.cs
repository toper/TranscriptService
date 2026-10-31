using Scalar.AspNetCore;
using TranscriptService.Api;
using TranscriptService.Api.Endpoints;
using TranscriptService.Api.Security;
using TranscriptService.ServiceDefaults;

if (await HealthProbe.TryRunAsync(args).ConfigureAwait(false) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults().AddMessaging();

builder.Services
    .AddApiOptions()
    .AddProblemDetails()
    .AddApiKeyAuthentication()
    .AddApiDocumentation()
    .AddJobs(builder.Configuration)
    .AddTranscriptionMessaging()
    .AddCallbackDelivery(builder.Configuration)
    .AddRetention();

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
return 0;
