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
RUN set -eux; \
    rm -f /etc/apt/sources.list; \
    rm -f /etc/apt/sources.list.d/debian.sources; \
    printf '%s\n' \
        'deb https://deb.debian.org/debian bookworm main' \
        'deb https://deb.debian.org/debian bookworm-updates main' \
        'deb https://security.debian.org/debian-security bookworm-security main' \
        'deb https://cloudfront.debian.net/debian bookworm main' \
        > /etc/apt/sources.list.d/00-custom.list; \
    apt-get -o Acquire::Retries=5 -o Acquire::http::Timeout=20 update; \
    apt-get install -y --no-install-recommends curl ca-certificates; \
    rm -rf /var/lib/apt/lists/*; \
    if ! getent group app >/dev/null; then groupadd --system app; fi; \
    if ! id app >/dev/null 2>&1; then useradd --system --gid app --create-home --home-dir /home/app app; fi

COPY --from=build /app/publish .
RUN chown -R app:app /app && mkdir -p /home/app && chown -R app:app /home/app

USER app

# Healthcheck intégré à l'image (évite de le redéfinir dans compose)
HEALTHCHECK --interval=5s --timeout=2s --start-period=10s --retries=15 \
  CMD curl -fsS http://localhost:8080/health >/dev/null || exit 1
ENTRYPOINT ["dotnet", "CineBoutique.Inventory.Api.dll"]
