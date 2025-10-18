using CineBoutique.Inventory.Domain.Auditing;
using CineBoutique.Inventory.Infrastructure.Auditing;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Database.Products;
using CineBoutique.Inventory.Infrastructure.Seeding;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("La chaîne de connexion 'ConnectionStrings:Default' est absente ou vide.");
        }

        services.AddSingleton(new DatabaseOptions(connectionString));
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddScoped<NpgsqlConnection>(_ => new NpgsqlConnection(connectionString));

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InfrastructureAssembly).Assembly)
                .For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole());

        services.AddScoped<InventoryDataSeeder>();
        services.AddScoped<IAuditLogger, DapperAuditLogger>();
        services.AddScoped<IProductLookupRepository, ProductLookupRepository>();
        services.AddScoped<IProductGroupRepository, ProductGroupRepository>();
        services.AddScoped<IProductSuggestionRepository, ProductSuggestionRepository>();

        return services;
    }
}
