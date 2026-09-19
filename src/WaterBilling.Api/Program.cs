using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using WaterBilling.Api.Features;
using WaterBilling.Api.Features.Auth;
using WaterBilling.Api.Features.Ingestion;
using WaterBilling.Api.Features.Meters;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Users;
using WaterBilling.Infrastructure;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Serilog from configuration so that log level and sinks are an operational
// setting, not a redeploy. File sink included because an on-prem box may have no
// log shipper and a support call starts with "send me the log".
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// The build-time OpenAPI generator starts the host with no database and no secrets.
// Everything that shapes the document still runs; only the startup work that needs
// those resources is skipped.
var isDocumentGeneration = HostingContext.IsOpenApiDocumentGeneration;

builder.Services.AddInfrastructure(builder.Configuration, validateConfigurationEagerly: !isDocumentGeneration);

// ASP.NET Core's web JSON defaults set NumberHandling = AllowReadingFromString, which
// makes the OpenAPI schema exporter describe every numeric field as `number | string`.
// That union propagates into every generated client and forces callers to narrow a
// type the API never actually returns. Strict mode restores a clean contract, and
// makes a meter that posts "totalM3": "12.5" fail validation loudly instead of being
// quietly accepted in a form the documented schema does not promise.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddScoped<IngestionService>();

if (!isDocumentGeneration)
{
    builder.Services.AddHostedService<DatabaseInitializer>();
}
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>(includeInternalTypes: true);

builder.Services.AddOptions<SeedOptions>()
    .Bind(builder.Configuration.GetSection(SeedOptions.SectionName));

// RFC 9457 problem documents for every failure, including the ones the framework
// produces (404 on an unmatched route, 415 on a bad content type).
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// --- Authentication: two schemes that never overlap ---
// Humans carry a JWT; meters present a per-meter key. Keeping them separate is what
// lets the authorization policies state "a device credential cannot read invoices".
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    if (!isDocumentGeneration)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is not configured. Generate one with `openssl rand -base64 48` " +
            "and set it as Jwt__SigningKey, or copy .env.example to .env and run via docker compose.");
    }

    // Placeholder that never signs anything: the document generator only needs the
    // authentication schemes to be registered so they appear in the spec.
    jwtOptions.SigningKey = new string('0', 64);
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            // Default is 5 minutes, which quietly extends every token's life.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    })
    .AddScheme<AuthenticationSchemeOptions, MeterKeyAuthenticationHandler>(
        MeterKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireRole(nameof(UserRole.Admin)))
    .AddPolicy(AuthorizationPolicies.AuthenticatedUser, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser())
    .AddPolicy(AuthorizationPolicies.MeterDevice, policy => policy
        .AddAuthenticationSchemes(MeterKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireClaim(MeterClaimTypes.MeterId));

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<SecuritySchemeDocumentTransformer>());

// Rate limiting keeps one malfunctioning device from saturating ingestion. The
// per-meter partition means a chatty meter throttles only itself.
builder.Services.AddRateLimiter(RateLimitingPolicies.Configure);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Smart Water Billing API")
        .WithTheme(ScalarTheme.BluePlanet));
}

app.UseSerilogRequestLogging();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapIngestionEndpoints();
app.MapMeterReadingEndpoints();
app.MapHealthEndpoints();

app.Run();

/// <summary>Exposed so the integration test host can reference the entry point assembly.</summary>
public partial class Program;
