using TranscriptService.API.Middleware;

namespace TranscriptService.API.Configuration
{
    public static class ApiKeyConfiguration
    {
        public static IApplicationBuilder UseApiKeyMiddleware(this IApplicationBuilder app)
        {
            return app.UseApiKeyAuthentication();
        }
    }
}
