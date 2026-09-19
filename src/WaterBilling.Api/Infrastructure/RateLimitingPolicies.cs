using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WaterBilling.Infrastructure.Auth;

namespace WaterBilling.Api.Infrastructure;

public static class RateLimitingPolicies
{
    public const string Ingestion = "ingestion";

    public const string Authentication = "authentication";

    /// <summary>
    /// The design cadence is one reading per meter per 15 minutes. 60/minute leaves
    /// roughly three orders of magnitude of headroom for retries and buffered
    /// uploads while still stopping a meter stuck in a reboot loop from flooding the
    /// ingestion path.
    /// </summary>
    private const int IngestionPermitsPerMinute = 60;

    public static void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(Ingestion, context => RateLimitPartition.GetFixedWindowLimiter(
            // Partition by meter key prefix, not by IP: many meters sit behind one
            // site gateway and would otherwise throttle each other.
            partitionKey: context.Request.Headers.TryGetValue(MeterApiKeyGenerator.HeaderName, out var key) && key.ToString().Length >= MeterApiKeyGenerator.PrefixLength
                ? key.ToString()[..MeterApiKeyGenerator.PrefixLength]
                : context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = IngestionPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

        options.AddPolicy(Authentication, context => RateLimitPartition.GetFixedWindowLimiter(
            // Slows credential stuffing without locking a legitimate user out.
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

        options.OnRejected = async (context, cancellationToken) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }

            context.HttpContext.Response.ContentType = "application/problem+json";

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                type = ApiProblems.BaseUri + "rate-limited",
                title = "Too many requests",
                status = StatusCodes.Status429TooManyRequests,
                detail = "The request rate for this client exceeded the configured limit. Retry after the interval in the Retry-After header.",
                traceId = context.HttpContext.TraceIdentifier
            }, cancellationToken);
        };
    }
}
