#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859
#pragma warning disable CA1812

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class AuditLoggingTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public AuditLoggingTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Authentication:Users:0:Name"] = "Amélie",
            ["Authentication:Users:0:Pin"] = "1111",
            ["Authentication:Issuer"] = "CineBoutique.Inventory",
            ["Authentication:Audience"] = "CineBoutique.Inventory",
            ["Authentication:Secret"] = "ChangeMe-Secret-Key-For-Inventory-Api-123",
            ["Authentication:TokenLifetimeMinutes"] = "30"
        };

        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString, configuration);

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
    public async Task RestartInventory_WritesAuditLog()
    {
        await ResetDatabaseAsync();

        var locationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        using (var scope = _factory.Services.CreateScope())
        {
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = connectionFactory.CreateConnection();
            await EnsureConnectionOpenAsync(connection);

            const string insertLocationSql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, @Code, @Label);";
            await connection.ExecuteAsync(insertLocationSql, new { Id = locationId, Code = "S1", Label = "Zone S1" });

            const string insertSessionSql = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);";
            await connection.ExecuteAsync(insertSessionSql, new { Id = sessionId, Name = "Session principale", StartedAtUtc = startedAt });

            await CountingRunSqlHelper.InsertAsync(
                connection,
                new CountingRunInsert(
                    runId,
                    sessionId,
                    locationId,
                    countType: 1,
                    startedAt,
                    CompletedAtUtc: null,
                    OperatorDisplayName: "Amélie"));
        }

        var response = await _client.PostAsync($"/api/inventories/{locationId}/restart?countType=1", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Single(auditLogs);

        var entry = auditLogs.Single();
        Assert.Equal("inventories.restart", entry.Category);
        Assert.Null(entry.Actor);
        Assert.Contains("Zone S1", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("premier passage", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanProduct_Success_WritesAuditLog()
    {
        await ResetDatabaseAsync();

        var productId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddHours(-1);

        using (var scope = _factory.Services.CreateScope())
        {
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = connectionFactory.CreateConnection();
            await EnsureConnectionOpenAsync(connection);

            const string insertProductSql = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";
            await connection.ExecuteAsync(insertProductSql, new { Id = productId, Sku = "SKU-100", Name = "Produit test", Ean = "1234567890123", CreatedAtUtc = createdAt });
        }

        var response = await _client.GetAsync("/api/products/1234567890123");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Single(auditLogs);

        var entry = auditLogs.Single();
        Assert.Equal("products.scan.success", entry.Category);
        Assert.Contains("1234567890123", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Produit test", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateProduct_Success_WritesAuditLog()
    {
        await ResetDatabaseAsync();

        var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Sku = "SKU-NEW",
            Name = "Nouveau produit",
            Ean = "9876543210987"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(created);
        Assert.Equal("SKU-NEW", created!.Sku);
        Assert.Equal("Nouveau produit", created!.Name);
        Assert.Equal("9876543210987", created!.Ean);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("products.create.success", entry.Category);
        Assert.Contains("SKU-NEW", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nouveau produit", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0001", "0001")]
    [InlineData("0001", "0000000001")]
    [InlineData("0001", "0000000000001")]
    [InlineData("0000000000001", "0000000000001")]
    [InlineData("0000000000001", "0001")]
    public async Task ScanProduct_NumericEanVariants_ReturnsStoredProduct(string storedEan, string scannedCode)
    {
        await ResetDatabaseAsync();

        var productId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddHours(-1);

        using (var scope = _factory.Services.CreateScope())
        {
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = connectionFactory.CreateConnection();
            await EnsureConnectionOpenAsync(connection);

            const string insertProductSql = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";
            await connection.ExecuteAsync(
                insertProductSql,
                new { Id = productId, Sku = "SKU-0001", Name = "Produit court", Ean = storedEan, CreatedAtUtc = createdAt });
        }

        var response = await _client.GetAsync($"/api/products/{scannedCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);
        Assert.Equal(storedEan, product!.Ean);
        Assert.Equal("Produit court", product!.Name);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("products.scan.success", entry.Category);
        Assert.Contains(scannedCode, entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanProduct_NotFound_WritesAuditLog()
    {
        await ResetDatabaseAsync();

        var response = await _client.GetAsync("/api/products/00000000");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Single(auditLogs);

        var entry = auditLogs.Single();
        Assert.Equal("products.scan.not_found", entry.Category);
        Assert.Contains("00000000", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanProduct_InvalidInput_WritesAuditLog()
    {
        await ResetDatabaseAsync();

        var response = await _client.GetAsync("/api/products/%20");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Single(auditLogs);

        var entry = auditLogs.Single();
        Assert.Equal("products.scan.invalid", entry.Category);
        Assert.Contains("code produit vide", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinLogin_Success_WritesAuditLog()
    {
        await ResetDatabaseAsync();

        var response = await _client.PostAsJsonAsync("/auth/pin", new PinAuthenticationRequest { Pin = "1111" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Single(auditLogs);

        var entry = auditLogs.Single();
        Assert.Equal("auth.pin.success", entry.Category);
        Assert.Equal("Amélie", entry.Actor);
        Assert.Contains("s'est connecté", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinLogin_InvalidPin_WritesAuditLog()
    {
        await ResetDatabaseAsync();

        var response = await _client.PostAsJsonAsync("/auth/pin", new PinAuthenticationRequest { Pin = "9999" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Single(auditLogs);

        var entry = auditLogs.Single();
        Assert.Equal("auth.pin.failure", entry.Category);
        Assert.Null(entry.Actor);
        Assert.Contains("refusée", entry.Message, StringComparison.OrdinalIgnoreCase);
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
TRUNCATE TABLE "Location" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Conflict" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    private async Task<IReadOnlyList<AuditLogEntry>> GetAuditLogsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string query = "SELECT \"id\" AS \"Id\", \"at\" AS \"At\", \"actor\" AS \"Actor\", \"category\" AS \"Category\", \"message\" AS \"Message\" FROM \"audit_logs\" ORDER BY \"at\" ASC;";
        var rows = await connection.QueryAsync<AuditLogEntry>(query);
        return rows.ToList();
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection)
    {
        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync();
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }

    private sealed record AuditLogEntry(long Id, DateTimeOffset At, string? Actor, string? Category, string Message);
}
