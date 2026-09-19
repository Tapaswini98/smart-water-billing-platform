# syntax=docker/dockerfile:1.7

# ---------------------------------------------------------------------------
# Build stage
# Restore is a separate layer from build: project files change rarely, source
# changes constantly, so an unchanged dependency graph means a cached restore.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/WaterBilling.Domain/WaterBilling.Domain.csproj src/WaterBilling.Domain/
COPY src/WaterBilling.Infrastructure/WaterBilling.Infrastructure.csproj src/WaterBilling.Infrastructure/
COPY src/WaterBilling.Api/WaterBilling.Api.csproj src/WaterBilling.Api/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/WaterBilling.Api/WaterBilling.Api.csproj

COPY src/ src/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/WaterBilling.Api/WaterBilling.Api.csproj \
        --configuration ${BUILD_CONFIGURATION} \
        --no-restore \
        --output /app/publish

# ---------------------------------------------------------------------------
# Runtime stage
# aspnet, not sdk: ~110 MB instead of ~800 MB, and no compiler on a box that
# sits inside a customer's network.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# curl is here for the container healthcheck below and nothing else.
RUN apt-get update \
    && apt-get install --no-install-recommends --yes curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The base image ships a non-root `app` user. Running as root inside a container
# that terminates HTTP on a customer's LAN is an avoidable risk.
RUN mkdir -p /app/logs && chown -R app:app /app
USER app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

EXPOSE 8080

# Liveness only — a container should not be killed because PostgreSQL is briefly
# unreachable. Readiness (/health/ready) is the orchestrator's concern.
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "WaterBilling.Api.dll"]
