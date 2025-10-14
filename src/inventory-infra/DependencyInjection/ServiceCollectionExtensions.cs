using CineBoutique.Inventory.Domain.Auditing;
using CineBoutique.Inventory.Infrastructure.Auditing;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Seeding;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InfrastructureAssembly).Assembly)
                .For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole());

        services.AddScoped<InventoryDataSeeder>();
        services.AddTransient<InventoryE2ESeeder>();
        services.AddScoped<IAuditLogger, DapperAuditLogger>();

        return services;
    }
}
