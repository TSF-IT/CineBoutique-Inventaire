# Étape de build et de publication
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copie des fichiers de projet pour optimiser le cache Docker
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY CineBoutique.Inventory.sln ./
COPY src/inventory-domain/CineBoutique.Inventory.Domain.csproj src/inventory-domain/
COPY src/inventory-shared/CineBoutique.Inventory.Shared.csproj src/inventory-shared/
COPY src/inventory-infra/CineBoutique.Inventory.Infrastructure.csproj src/inventory-infra/
COPY src/inventory-api/CineBoutique.Inventory.Api.csproj src/inventory-api/
COPY tests/inventory.api.tests/CineBoutique.Inventory.Api.Tests.csproj tests/inventory.api.tests/
COPY tests/inventory.domain.tests/CineBoutique.Inventory.Domain.Tests.csproj tests/inventory.domain.tests/

RUN dotnet restore CineBoutique.Inventory.sln

# Copie du reste des sources et publication
COPY src/ src/

RUN dotnet publish src/inventory-api/CineBoutique.Inventory.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

# Étape finale d'exécution
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install --no-install-recommends -y wget \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CineBoutique.Inventory.Api.dll"]
