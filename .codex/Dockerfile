# Image SDK .NET 8 officielle
FROM mcr.microsoft.com/dotnet/sdk:8.0

# Outils utiles
RUN apt-get update && apt-get install -y git curl jq ca-certificates && rm -rf /var/lib/apt/lists/*

# Variables d'env standard pour build/tests
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    ASPNETCORE_ENVIRONMENT=CI \
    DISABLE_SERILOG=true \
    DISABLE_MIGRATIONS=true

WORKDIR /workspace
