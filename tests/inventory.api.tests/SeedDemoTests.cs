#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Seeding;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class SeedDemoTests : IAsyncLifetime, IDisposable
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory? _factory;

    public SeedDemoTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        await _factory.EnsureMigratedAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }

    [Fact]
    [Trait("Category", "SeedDemo")]
    public async Task SeedDemo_CreatesExpectedRuns()
    {
        var factory = _factory ?? throw new InvalidOperationException("Factory not initialized.");

        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
            await seeder.SeedAsync().ConfigureAwait(false);
        }

        await using var connection = new NpgsqlConnection(_pg.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        const string sql = @"
SELECT L.""Code",
       R.""CountType"",
       CASE WHEN R.""CompletedAtUtc"" IS NULL THEN 'InProgress' ELSE 'Completed' END AS ""Status""
FROM ""Location"" L
JOIN ""CountingRun"" R ON R.""LocationId"" = L.""Id""
WHERE L.""Code"" IN ('B1','B2','B3','B4')";

        var rows = (await connection.QueryAsync<SeedRunRow>(sql).ConfigureAwait(false)).ToList();

        var b1Runs = rows.Where(r => r.Code == "B1").ToList();
        Assert.Contains(b1Runs, r => r.Status == "InProgress");

        var b2Runs = rows.Where(r => r.Code == "B2").ToList();
        Assert.Contains(b2Runs, r => r.Status == "Completed");
        Assert.Contains(b2Runs, r => r.Status == "InProgress");

        var b3Runs = rows.Where(r => r.Code == "B3").ToList();
        Assert.True(b3Runs.Count(r => r.Status == "Completed") >= 2, "B3 should have at least two completed runs.");

        var b4Runs = rows.Where(r => r.Code == "B4").ToList();
        Assert.True(b4Runs.Count(r => r.Status == "Completed") >= 2, "B4 should have at least two completed runs.");
    }

    private sealed record SeedRunRow(string Code, int CountType, string Status);
}
