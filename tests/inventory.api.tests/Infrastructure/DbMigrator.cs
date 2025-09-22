using System;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public static class DbMigrator
{
    public static void MigrateUp(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }
}
