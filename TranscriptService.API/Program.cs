using NLog;
using NLog.Web;
using TranscriptService.API.Configuration;
using Asp.Versioning;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
logger.Debug("Init main");

try
{
    var webApplicationOptions = new WebApplicationOptions()
    {
        ContentRootPath = AppContext.BaseDirectory,
        Args = args
    };

    var builder = WebApplication.CreateBuilder(webApplicationOptions);

    // Configuration
    builder.ConfigureHosting(logger);
    builder.AddNlog();

    // API Versioning
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    }).AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

    // Services
    builder.AddServices();

    // Swagger
    builder.Services.AddSwaggerConfiguration();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    var app = builder.Build();

    // Exception handling
    if (app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/error-development");
    }
    else
    {
        app.UseExceptionHandler("/error");
        app.UseHsts();
    }

    // Middleware pipeline
    app.UseCors();
    app.UseHttpsRedirection();
    app.UseRouting();

    // Swagger UI (no authentication required)
    app.UseSwaggerConfiguration();

    app.UseApiKeyMiddleware();
    app.UseCustomHealthChecks();
    app.MapControllers();

    app.Run();
}
catch (Exception exception)
{
    logger.Error(exception, "Stopped program because of exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}
