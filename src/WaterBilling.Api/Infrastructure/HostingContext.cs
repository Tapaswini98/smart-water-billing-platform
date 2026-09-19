using System.Reflection;

namespace WaterBilling.Api.Infrastructure;

public static class HostingContext
{
    /// <summary>
    /// True when this process is the build-time OpenAPI document generator
    /// (<c>dotnet-getdocument</c>) rather than a real host.
    /// <para>
    /// The generator builds and starts the application in order to read its endpoint
    /// metadata, then discards it. It has no database and no secrets, so the two
    /// pieces of startup work that need them — applying migrations and eagerly
    /// validating the JWT signing key — are skipped when this is true. Everything
    /// that shapes the OpenAPI document still runs.
    /// </para>
    /// <para>
    /// Detected by entry assembly rather than by an environment variable because the
    /// tool sets no distinguishing variable, and an argument check would break the
    /// moment the tool's argument list changes.
    /// </para>
    /// </summary>
    public static bool IsOpenApiDocumentGeneration { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name is "GetDocument.Insider";
}
