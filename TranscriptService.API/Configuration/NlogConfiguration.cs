using NLog.Web;

namespace TranscriptService.API.Configuration
{
    public static class NlogConfiguration
    {
        public static void AddNlog(this WebApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Host.UseNLog();
        }
    }
}
