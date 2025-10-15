# ===== build =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Fichiers solution/props
COPY CineBoutique.Inventory.sln ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./

# Projets
COPY src/inventory-shared/CineBoutique.Inventory.Shared.csproj src/inventory-shared/
COPY src/inventory-domain/CineBoutique.Inventory.Domain.csproj src/inventory-domain/
COPY src/inventory-infra/CineBoutique.Inventory.Infrastructure.csproj src/inventory-infra/
COPY src/inventory-api/CineBoutique.Inventory.Api.csproj src/inventory-api/

# Restore (ciblé pour le projet API afin d'amorcer le graph)
RUN dotnet restore "src/inventory-api/CineBoutique.Inventory.Api.csproj"

# Copy du reste du code
COPY . .

# Publish
WORKDIR /src/src/inventory-api
RUN dotnet publish "CineBoutique.Inventory.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ===== runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Outils min pour healthcheck (image basée Debian)
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# Healthcheck intégré à l'image (évite de le redéfinir dans compose)
HEALTHCHECK --interval=3s --timeout=2s --retries=20 --start-period=10s CMD curl -fsS http://localhost:8080/health || exit 1

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CineBoutique.Inventory.Api.dll"]
