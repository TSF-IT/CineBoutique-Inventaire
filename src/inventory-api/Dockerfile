# build
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

# Restore
RUN dotnet restore "src/inventory-api/CineBoutique.Inventory.Api.csproj"

# Copy du reste du code
COPY . .

# Publish
WORKDIR /src/src/inventory-api
RUN dotnet publish "CineBoutique.Inventory.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CineBoutique.Inventory.Api.dll"]
