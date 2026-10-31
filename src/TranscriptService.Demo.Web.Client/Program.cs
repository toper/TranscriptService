using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TranscriptService.Demo.Web.Client.Recording;
using TranscriptService.Demo.Web.Client.Transcriptions;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ITranscriptionSession, TranscriptionSession>();
builder.Services.AddScoped<IMicrophone, BrowserMicrophone>();

await builder.Build().RunAsync().ConfigureAwait(false);
