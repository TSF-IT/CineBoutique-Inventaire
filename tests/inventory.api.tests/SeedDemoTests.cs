#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Seeding;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class SeedDemoTests : IAsyncLifetime
{
    private static readonly Guid DemoSessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid DemoRunB1Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public SeedDemoTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        await _factory.EnsureMigratedAsync();

        await ResetDatabaseAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
            await seeder.SeedAsync();
        }

        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LocationsEndpoint_ExposeSeededRun()
    {
        var response = await _client.GetAsync("/api/locations?countType=1");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<LocationListItemDto>>();
        Assert.NotNull(payload);

        var b1 = payload!.FirstOrDefault(item => string.Equals(item.Code, "B1", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(b1);

        Assert.True(b1!.IsBusy);
        Assert.Equal(DemoRunB1Id, b1.ActiveRunId);
        const short expectedCountType = 1;
        Assert.Equal(expectedCountType, b1.ActiveCountType);
        Assert.Equal("Amélie", b1.BusyBy);

        var status = Assert.Single(b1.CountStatuses.Where(s => s.CountType == 1));
        Assert.Equal(LocationCountStatus.InProgress, status.Status);
        Assert.Equal(DemoRunB1Id, status.RunId);
        Assert.Equal("Amélie", status.OperatorDisplayName);
        Assert.NotNull(status.StartedAtUtc);
        Assert.Null(status.CompletedAtUtc);
    }

    [Fact]
    public async Task SeededRunB1_HasPersistedCountLines()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string linesSql = @"
SELECT cl.""CountingRunId"", cl.""Quantity"", cl.""CountedAtUtc"", p.""Ean""
FROM ""CountLine"" cl
JOIN ""Product"" p ON p.""Id"" = cl.""ProductId""
WHERE cl.""CountingRunId"" = @RunId
ORDER BY cl.""CountedAtUtc"", cl.""Id"";";

        var lines = (await connection.QueryAsync<CountLineDetails>(linesSql, new { RunId = DemoRunB1Id }))
            .ToList();

        Assert.Equal(2, lines.Count);
        Assert.Equal("0000000000001", lines[0].Ean);
        Assert.Equal(3m, lines[0].Quantity);
        Assert.Equal("0000000000000", lines[1].Ean);
        Assert.Equal(5m, lines[1].Quantity);
        Assert.All(lines, line => Assert.Equal(DemoRunB1Id, line.CountingRunId));

        const string runSql = @"
SELECT ""InventorySessionId"", ""LocationId"", ""CountType"", ""OperatorDisplayName"", ""StartedAtUtc"", ""CompletedAtUtc""
FROM ""CountingRun""
WHERE ""Id"" = @RunId;";

        var run = await connection.QuerySingleAsync<RunSnapshot>(runSql, new { RunId = DemoRunB1Id });

        Assert.Equal(DemoSessionId, run.InventorySessionId);
        Assert.Equal((short)1, run.CountType);
        Assert.Equal("Amélie", run.OperatorDisplayName);
        Assert.Null(run.CompletedAtUtc);
        Assert.True(run.StartedAtUtc <= DateTimeOffset.UtcNow);
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string cleanupSql = """
DO $do$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Audit') THEN
        EXECUTE 'TRUNCATE TABLE ""Audit"" RESTART IDENTITY CASCADE;';
    END IF;
END $do$;

TRUNCATE TABLE "CountLine" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "CountingRun" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "InventorySession" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Location" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    private static async Task EnsureConnectionOpenAsync(NpgsqlConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Dapper during query materialization.")]
    private sealed record class CountLineDetails(Guid CountingRunId, decimal Quantity, DateTimeOffset CountedAtUtc, string Ean);

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Dapper during query materialization.")]
    private sealed record class RunSnapshot(
        Guid InventorySessionId,
        Guid LocationId,
        short CountType,
        string? OperatorDisplayName,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);
}
#pragma warning restore CA2234
#pragma warning restore CA2007
#pragma warning restore CA1707
#pragma warning restore CA1001
