using FluentValidation;
using TranscriptService.API.Models.Requests;
using TranscriptService.API.Provider;
using TranscriptService.API.Repository.Commands;
using TranscriptService.API.Repository.Queries;
using TranscriptService.API.Validators;

namespace TranscriptService.API.Configuration
{
    public static class IoCConfiguration
    {
        public static IServiceCollection AddServices(this WebApplicationBuilder builder)
        {
            var services = builder.Services;

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.WriteIndented = true;
                });

            // Health checks
            services.AddCustomHealthChecks(builder.Configuration);

            // Providers
            services.AddSingleton<ITranscriptProvider, TranscriptProvider>();

            // Repository (Commands/Queries)
            services.AddTransient<ITranscriptCommands, TranscriptCommands>();
            services.AddTransient<ITranscriptQueries, TranscriptQueries>();

            // Validators
            services.AddTransient<IValidator<TranscriptFileRequest>, TranscriptFileRequestValidator>();
            services.AddTransient<IValidator<TranscriptPathRequest>, TranscriptPathRequestValidator>();

            return services;
        }
    }
}
