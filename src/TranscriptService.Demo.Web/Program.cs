using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using TranscriptService.Contracts.Http;
using TranscriptService.Demo.Web.Components;
using TranscriptService.Demo.Web.Transcriptions;
using TranscriptService.ServiceDefaults;

if (await HealthProbe.TryRunAsync(args).ConfigureAwait(false) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<TranscriptApiOptions>()
    .BindConfiguration(TranscriptApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient<TranscriptApiClient>((provider, http) =>
    {
        var api = provider.GetRequiredService<IOptions<TranscriptApiOptions>>().Value;
        http.BaseAddress = api.BaseUrl;
        http.DefaultRequestHeaders.Add(ApiKeyHeader.Name, api.ApiKey);
    })
    // Retries only for safe methods: a re-sent upload would create a second transcription job.
    .AddStandardResilienceHandler(resilience => resilience.Retry.DisableForUnsafeHttpMethods());

builder.Services.AddSignalR();
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapDemoEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(TranscriptService.Demo.Web.Client._Imports).Assembly);

await app.RunAsync().ConfigureAwait(false);
return 0;
