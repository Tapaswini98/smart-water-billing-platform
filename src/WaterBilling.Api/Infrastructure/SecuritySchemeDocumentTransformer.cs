using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using WaterBilling.Infrastructure.Auth;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// Declares both authentication schemes in the OpenAPI document so the generated
/// reference is actually usable: a reviewer can paste a JWT for the admin calls and
/// a meter key for ingestion without reading the source to find out how.
/// </summary>
public sealed class SecuritySchemeDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "Smart Water Billing API",
            Version = "v1",
            Description =
                "On-premise IoT water billing. Humans authenticate with a JWT from /api/v1/auth/login; " +
                "meters authenticate per request with the X-Meter-Key header."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the accessToken returned by POST /api/v1/auth/login."
        };

        document.Components.SecuritySchemes[MeterKeyAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = MeterApiKeyGenerator.HeaderName,
            In = ParameterLocation.Header,
            Description = "Per-meter ingestion key. Issued when the meter is created and shown only once."
        };

        return Task.CompletedTask;
    }
}
