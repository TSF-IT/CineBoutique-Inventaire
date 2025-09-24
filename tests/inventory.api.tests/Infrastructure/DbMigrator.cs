using System;
using System.Data;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public static class DbMigrator
{
    public static void MigrateUp(IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        var connectionFactory = scope.ServiceProvider.GetService<IDbConnectionFactory>();
        if (connectionFactory is null)
        {
            return;
        }

        using var connection = connectionFactory.CreateConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        const string sql = @"SELECT to_regclass('""AdminUser""') IS NOT NULL;";
        var tableExists = connection.ExecuteScalar<bool?>(sql) ?? false;

        Assert.True(tableExists, "Migrations did not run");
    }
}
