using NLog;

namespace TranscriptService.API.Configuration
{
    public static class HostingConfiguration
    {
        public static void ConfigureHosting(this WebApplicationBuilder builder, Logger logger)
        {
            builder.Environment.ContentRootPath = Directory.GetCurrentDirectory();

            // Configure Kestrel for large file uploads and long timeout
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 100_000_000; // 100MB
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
                options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
            });
        }
    }
}
