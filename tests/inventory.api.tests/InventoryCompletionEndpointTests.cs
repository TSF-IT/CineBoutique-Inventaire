#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class InventoryCompletionEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public InventoryCompletionEndpointTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        await _factory.EnsureMigratedAsync();
        _client = _factory.CreateClient();
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteInventoryRun_ReturnsBadRequest_WhenNoItems()
    {
        await ResetDatabaseAsync();
        var locationId = await SeedLocationAsync("S1", "Zone S1");

        var payload = new CompleteInventoryRunRequest
        {
            CountType = 1,
            Operator = "Amélie",
            Items = new List<CompleteInventoryRunItemRequest>()
        };

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteInventoryRun_CreatesRunAndLines_ForExistingProduct()
    {
        await ResetDatabaseAsync();
        var locationId = await SeedLocationAsync("S1", "Zone S1");
        var productId = await SeedProductAsync("PROD-001", "Produit référencé", "12345678");

        var payload = new CompleteInventoryRunRequest
        {
            CountType = 1,
            Operator = "Amélie",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "12345678",
                    Quantity = 2,
                    IsManual = false
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>();
        Assert.NotNull(result);
        Assert.Equal(locationId, result!.LocationId);
        Assert.Equal(1, result.ItemsCount);
        Assert.Equal(2m, result.TotalQuantity);

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var hasOperatorColumn = await CountingRunSqlHelper.HasOperatorDisplayNameAsync(connection);

        var runsQuery = hasOperatorColumn
            ? "SELECT \"Id\" AS RunId, \"InventorySessionId\" AS SessionId, \"CompletedAtUtc\" AS CompletedAt, \"OperatorDisplayName\" AS Operator FROM \"CountingRun\""
            : "SELECT \"Id\" AS RunId, \"InventorySessionId\" AS SessionId, \"CompletedAtUtc\" AS CompletedAt, NULL::text AS Operator FROM \"CountingRun\"";

        var runs = await connection.QueryAsync<(Guid RunId, Guid SessionId, DateTimeOffset? CompletedAt, string? Operator)>(runsQuery);

        Assert.Single(runs);
        var singleRun = runs.Single();
        Assert.Equal(result.RunId, singleRun.RunId);
        Assert.NotNull(singleRun.CompletedAt);
        if (hasOperatorColumn)
        {
            Assert.Equal("Amélie", singleRun.Operator);
        }
        else
        {
            Assert.Null(singleRun.Operator);
        }

        var lines = await connection.QueryAsync<(Guid ProductId, decimal Quantity)>(
                "SELECT \"ProductId\", \"Quantity\" FROM \"CountLine\"")
            ;

        Assert.Single(lines);
        var line = lines.Single();
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public async Task CompleteInventoryRun_CreatesUnknownProduct_WhenEanNotFound()
    {
        await ResetDatabaseAsync();
        var locationId = await SeedLocationAsync("S1", "Zone S1");

        var payload = new CompleteInventoryRunRequest
        {
            CountType = 2,
            Operator = "Bruno",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "99999999",
                    Quantity = 5,
                    IsManual = true
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", payload);
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var product = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Sku, string Name)>(
                "SELECT \"Id\", \"Sku\", \"Name\" FROM \"Product\" WHERE \"Ean\" = @Ean LIMIT 1",
                new { Ean = "99999999" })
            ;

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.StartsWith("UNK-", product.Sku, StringComparison.Ordinal);
        Assert.Equal("Produit inconnu EAN 99999999", product.Name);

        var lines = await connection.QueryAsync<(Guid ProductId, decimal Quantity)>(
                "SELECT \"ProductId\", \"Quantity\" FROM \"CountLine\"")
            ;

        Assert.Single(lines);
        var line = lines.Single();
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(5m, line.Quantity);
    }

    [Fact]
    public async Task CompleteInventoryRun_CreatesConflict_WhenSecondRunDiffers()
    {
        await ResetDatabaseAsync();
        var locationId = await SeedLocationAsync("Z1", "Zone Z1");

        var firstPayload = new CompleteInventoryRunRequest
        {
            CountType = 1,
            Operator = "Yann",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "32165498",
                    Quantity = 10,
                    IsManual = false
                }
            }
        };

        var firstResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", firstPayload);
        firstResponse.EnsureSuccessStatusCode();

        var secondPayload = new CompleteInventoryRunRequest
        {
            CountType = 2,
            Operator = "Zoé",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "32165498",
                    Quantity = 4,
                    IsManual = false
                }
            }
        };

        var secondResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", secondPayload);
        secondResponse.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var conflicts = (await connection.QueryAsync<(Guid ConflictId, Guid LocationId, short CountType)>(
                "SELECT c.\"Id\" AS \"ConflictId\", cr.\"LocationId\" AS \"LocationId\", cr.\"CountType\" AS \"CountType\"\n" +
                "FROM \"Conflict\" c\n" +
                "JOIN \"CountLine\" cl ON cl.\"Id\" = c.\"CountLineId\"\n" +
                "JOIN \"CountingRun\" cr ON cr.\"Id\" = cl.\"CountingRunId\";"))
            .ToList();

        Assert.Single(conflicts);
        var conflict = conflicts[0];
        Assert.Equal(locationId, conflict.LocationId);
        Assert.True(conflict.CountType is 1 or 2, "Le conflit doit provenir d'un des deux premiers comptages.");

        var summaryResponse = await _client.GetAsync("/api/inventories/summary");
        summaryResponse.EnsureSuccessStatusCode();
        var summary = await summaryResponse.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.Conflicts);
        Assert.Single(summary.ConflictZones);
        Assert.Equal(locationId, summary.ConflictZones[0].LocationId);
    }

    [Fact]
    public async Task CompleteInventoryRun_AllowsThirdRunForInitialOperator_WhenConflictExists()
    {
        await ResetDatabaseAsync();
        var locationId = await SeedLocationAsync("Z3", "Zone Z3");

        var firstPayload = new CompleteInventoryRunRequest
        {
            CountType = 1,
            Operator = "Chloé",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "12345670",
                    Quantity = 5,
                    IsManual = true
                }
            }
        };

        var firstResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", firstPayload);
        firstResponse.EnsureSuccessStatusCode();

        var secondPayload = new CompleteInventoryRunRequest
        {
            CountType = 2,
            Operator = "Bruno",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "12345670",
                    Quantity = 7,
                    IsManual = true
                }
            }
        };

        var secondResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", secondPayload);
        secondResponse.EnsureSuccessStatusCode();

        var thirdPayload = new CompleteInventoryRunRequest
        {
            CountType = 3,
            Operator = "Chloé",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "12345670",
                    Quantity = 6,
                    IsManual = true
                }
            }
        };

        var thirdResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", thirdPayload);
        thirdResponse.EnsureSuccessStatusCode();

        var thirdResult = await thirdResponse.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>();
        Assert.NotNull(thirdResult);
        Assert.Equal(3, thirdResult!.CountType);

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var hasOperatorColumn = await CountingRunSqlHelper.HasOperatorDisplayNameAsync(connection);
        var selectSql = hasOperatorColumn
            ? "SELECT \"Id\" AS RunId, \"CountType\", \"OperatorDisplayName\" AS Operator FROM \"CountingRun\" WHERE \"Id\" = @Id LIMIT 1;"
            : "SELECT \"Id\" AS RunId, \"CountType\", NULL::text AS Operator FROM \"CountingRun\" WHERE \"Id\" = @Id LIMIT 1;";

        var run = await connection.QuerySingleAsync<(Guid RunId, short CountType, string? Operator)>(selectSql, new { Id = thirdResult.RunId });
        Assert.Equal((short)3, run.CountType);
        if (hasOperatorColumn)
        {
            Assert.Equal("Chloé", run.Operator);
        }
        else
        {
            Assert.Null(run.Operator);
        }
    }

    [Fact]
    public async Task CompleteInventoryRun_RejectsSecondRun_WhenOperatorMatchesFirst()
    {
        await ResetDatabaseAsync();
        var locationId = await SeedLocationAsync("Z2", "Zone Z2");

        var firstPayload = new CompleteInventoryRunRequest
        {
            CountType = 1,
            Operator = "Chloé",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "78945612",
                    Quantity = 3,
                    IsManual = false
                }
            }
        };

        var firstResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", firstPayload);
        firstResponse.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = connectionFactory.CreateConnection();
            await EnsureConnectionOpenAsync(connection);
            var hasOperatorColumn = await CountingRunSqlHelper.HasOperatorDisplayNameAsync(connection);
            Assert.True(hasOperatorColumn, "La colonne OperatorDisplayName est requise pour ce test.");
        }

        var secondPayload = new CompleteInventoryRunRequest
        {
            CountType = 2,
            Operator = "Chloé",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "78945612",
                    Quantity = 3,
                    IsManual = false
                }
            }
        };

        var secondResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", secondPayload);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var error = await secondResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(error);
        Assert.Equal(
            "Le deuxième comptage doit être réalisé par un opérateur différent du premier.",
            error!.GetValueOrDefault("message"));
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
        EXECUTE 'TRUNCATE TABLE "Audit" RESTART IDENTITY CASCADE;';
    END IF;
END $do$;

TRUNCATE TABLE "CountLine" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "CountingRun" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "InventorySession" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Location" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "ShopUser" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    private async Task<Guid> SeedLocationAsync(string code, string label)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string ensureShopSql =
            "INSERT INTO \"Shop\" (\"Name\") VALUES (@Name) ON CONFLICT DO NOTHING;";
        const string selectShopSql =
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) LIMIT 1;";

        await connection.ExecuteAsync(ensureShopSql, new { Name = "CinéBoutique Paris" });
        var shopId = await connection.ExecuteScalarAsync<Guid>(selectShopSql, new { Name = "CinéBoutique Paris" });

        var locationId = Guid.NewGuid();
        const string sql =
            "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\", \"ShopId\") VALUES (@Id, @Code, @Label, @ShopId);";
        await connection.ExecuteAsync(sql, new { Id = locationId, Code = code, Label = label, ShopId = shopId });
        return locationId;
    }

    private async Task<Guid> SeedProductAsync(string sku, string name, string ean)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var productId = Guid.NewGuid();
        const string sql =
            "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";
        await connection.ExecuteAsync(sql, new { Id = productId, Sku = sku, Name = name, Ean = ean, CreatedAtUtc = DateTimeOffset.UtcNow });
        return productId;
    }

    private static async Task EnsureConnectionOpenAsync(NpgsqlConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }
}
