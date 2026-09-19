using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "WaterBilling";

    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">Application configuration, read for connection strings and JWT options.</param>
    /// <param name="validateConfigurationEagerly">
    /// When true (the default), a missing connection string or signing key fails at
    /// startup rather than at first use. Set false only for tooling that builds the
    /// host without intending to serve traffic — see <c>HostingContext</c>.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateConfigurationEagerly = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (validateConfigurationEagerly)
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. " +
                    "Set ConnectionStrings__WaterBilling, or copy .env.example to .env and run via docker compose.");
            }

            // Never connected to: the provider only needs to exist so that the model
            // can be built and the endpoint metadata read.
            connectionString = "Host=localhost;Database=openapi-document-generation";
        }

        services.AddDbContext<WaterBillingDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(WaterBillingDbContext).Assembly.FullName);
                // On-prem networks drop connections; retry the transient ones rather
                // than surfacing them to a meter that will just retry anyway.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            });

            // Invoices and readings carry required FKs to soft-deletable meters and
            // users. That is intentional: an invoice must remain readable after its
            // meter is retired, so the filter is not propagated across the join.
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        var jwt = services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations();

        if (validateConfigurationEagerly)
        {
            // Fail at startup, not at first login, if the signing key is missing.
            jwt.ValidateOnStart();
        }

        return services;
    }
}
