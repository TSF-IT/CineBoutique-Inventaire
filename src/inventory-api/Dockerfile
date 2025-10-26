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

# Outils min pour healthcheck + création d'un utilisateur non-root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --system app \
 && useradd --system --gid app --create-home --home-dir /home/app app

COPY --from=build /app/publish .
RUN chown -R app:app /app

USER app

# Healthcheck intégré à l'image (évite de le redéfinir dans compose)
HEALTHCHECK --interval=5s --timeout=2s --start-period=10s --retries=15 \
  CMD curl -fsS http://localhost:8080/health >/dev/null || exit 1
ENTRYPOINT ["dotnet", "CineBoutique.Inventory.Api.dll"]
