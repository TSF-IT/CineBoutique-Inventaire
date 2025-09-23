# syntax=docker/dockerfile:1.6

# Étape de construction
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CineBoutique.Inventory.sln ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY src/inventory-api/CineBoutique.Inventory.Api.csproj src/inventory-api/
COPY src/inventory-infra/CineBoutique.Inventory.Infrastructure.csproj src/inventory-infra/
COPY src/inventory-domain/CineBoutique.Inventory.Domain.csproj src/inventory-domain/
COPY src/inventory-shared/CineBoutique.Inventory.Shared.csproj src/inventory-shared/

RUN --mount=type=cache,id=nuget-cache,target=/root/.nuget/packages \
    dotnet restore "src/inventory-api/CineBoutique.Inventory.Api.csproj"

COPY . .
WORKDIR /src/src/inventory-api

RUN --mount=type=cache,id=nuget-cache,target=/root/.nuget/packages \
    dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Étape finale
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install --no-install-recommends -y wget \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CineBoutique.Inventory.Api.dll"]
