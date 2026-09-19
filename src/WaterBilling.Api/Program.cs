using System.Text;
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

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<IngestionService>();
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
    ?? throw new InvalidOperationException("The Jwt configuration section is missing.");

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

await app.ApplyDatabaseMigrationsAsync();

app.Run();

/// <summary>Exposed so the integration test host can reference the entry point assembly.</summary>
public partial class Program;
