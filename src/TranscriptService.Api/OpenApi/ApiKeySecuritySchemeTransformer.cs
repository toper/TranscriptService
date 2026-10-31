using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using TranscriptService.Api.Security;

namespace TranscriptService.Api.OpenApi;

/// <summary>Documents the API key scheme so Scalar can send it with "Try it".</summary>
internal sealed class ApiKeySecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeyAuthenticationHandler.HeaderName,
            In = ParameterLocation.Header,
            Description = "API key issued to the client.",
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationHandler.SchemeName, document)] = [] },
        ];
        return Task.CompletedTask;
    }
}
