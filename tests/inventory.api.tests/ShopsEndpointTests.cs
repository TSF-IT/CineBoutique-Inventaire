#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859
#pragma warning disable CA1812

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BCrypt.Net;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class ShopsEndpointTests : IAsyncLifetime
{
    private const string AdminLogin = "admin.test";
    private const string AdminDisplayName = "Administrateur Test";
    private const string AdminSecret = "Secret123!";

    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public ShopsEndpointTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        await _factory.EnsureMigratedAsync();
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetShops_ReturnsExistingShops()
    {
        await ResetDatabaseAsync();
        var adminShopId = await SeedShopAsync("Boutique Admin");
        await SeedShopUserAsync(adminShopId, AdminLogin, AdminDisplayName, isAdmin: true, secret: AdminSecret);
        await AuthenticateAsync(adminShopId, AdminLogin, AdminSecret);
        await ClearAuditLogsAsync();

        var lyonShopId = await SeedShopAsync("CinéBoutique Lyon");
        var nantesShopId = await SeedShopAsync("CinéBoutique Nantes");

        var response = await _client.GetAsync("/api/shops");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<ShopDto>>();
        Assert.NotNull(payload);

        Assert.Equal(3, payload!.Count);
        Assert.Contains(payload, shop => shop.Id == adminShopId && shop.Name == "Boutique Admin");
        Assert.Contains(payload, shop => shop.Id == lyonShopId && shop.Name == "CinéBoutique Lyon");
        Assert.Contains(payload, shop => shop.Id == nantesShopId && shop.Name == "CinéBoutique Nantes");
    }

    [Fact]
    public async Task CreateShop_PersistsShopAndWritesAudit()
    {
        await ResetDatabaseAsync();
        var adminShopId = await SeedShopAsync("Boutique Admin");
        await SeedShopUserAsync(adminShopId, AdminLogin, AdminDisplayName, isAdmin: true, secret: AdminSecret);
        await AuthenticateAsync(adminShopId, AdminLogin, AdminSecret);
        await ClearAuditLogsAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/shops",
            new CreateShopRequest { Name = "CinéBoutique Lille" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ShopDto>();
        Assert.NotNull(payload);
        Assert.Equal("CinéBoutique Lille", payload!.Name);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("shops.create.success", entry.Category);
        Assert.Contains("CinéBoutique Lille", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AdminDisplayName, entry.Actor);
    }

    [Fact]
    public async Task CreateShop_WithExistingName_ReturnsConflict()
    {
        await ResetDatabaseAsync();
        var adminShopId = await SeedShopAsync("Boutique Admin");
        await SeedShopUserAsync(adminShopId, AdminLogin, AdminDisplayName, isAdmin: true, secret: AdminSecret);
        await AuthenticateAsync(adminShopId, AdminLogin, AdminSecret);
        await ClearAuditLogsAsync();

        await SeedShopAsync("CinéBoutique Nice");

        var response = await _client.PostAsJsonAsync(
            "/api/shops",
            new CreateShopRequest { Name = "cinéboutique nice" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem!.Status);
        Assert.Contains("existe déjà", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateShop_RenamesShopAndWritesAudit()
    {
        await ResetDatabaseAsync();
        var adminShopId = await SeedShopAsync("Boutique Admin");
        await SeedShopUserAsync(adminShopId, AdminLogin, AdminDisplayName, isAdmin: true, secret: AdminSecret);
        await AuthenticateAsync(adminShopId, AdminLogin, AdminSecret);
        await ClearAuditLogsAsync();

        var targetShopId = await SeedShopAsync("CinéBoutique Nice");

        var response = await _client.PutAsJsonAsync(
            "/api/shops",
            new UpdateShopRequest { Id = targetShopId, Name = "CinéBoutique Nice Centre" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ShopDto>();
        Assert.NotNull(payload);
        Assert.Equal(targetShopId, payload!.Id);
        Assert.Equal("CinéBoutique Nice Centre", payload.Name);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("shops.update.success", entry.Category);
        Assert.Contains("Nice Centre", entry.Message, StringComparison.OrdinalIgnoreCase);

        var storedName = await GetShopNameAsync(targetShopId);
        Assert.Equal("CinéBoutique Nice Centre", storedName);
    }

    [Fact]
    public async Task DeleteShop_WhenEmpty_RemovesShopAndWritesAudit()
    {
        await ResetDatabaseAsync();
        var adminShopId = await SeedShopAsync("Boutique Admin");
        await SeedShopUserAsync(adminShopId, AdminLogin, AdminDisplayName, isAdmin: true, secret: AdminSecret);
        await AuthenticateAsync(adminShopId, AdminLogin, AdminSecret);
        await ClearAuditLogsAsync();

        var removableShopId = await SeedShopAsync("CinéBoutique Caen");

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/shops")
        {
            Content = JsonContent.Create(new DeleteShopRequest { Id = removableShopId })
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("shops.delete.success", entry.Category);
        Assert.Contains("CinéBoutique Caen", entry.Message, StringComparison.OrdinalIgnoreCase);

        var exists = await ShopExistsAsync(removableShopId);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteShop_WithLinkedLocation_ReturnsConflict()
    {
        await ResetDatabaseAsync();
        var adminShopId = await SeedShopAsync("Boutique Admin");
        await SeedShopUserAsync(adminShopId, AdminLogin, AdminDisplayName, isAdmin: true, secret: AdminSecret);
        await AuthenticateAsync(adminShopId, AdminLogin, AdminSecret);

        var targetShopId = await SeedShopAsync("CinéBoutique Reims");
        await SeedLocationAsync(targetShopId, "R1", "Zone R1");

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/shops")
        {
            Content = JsonContent.Create(new DeleteShopRequest { Id = targetShopId })
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem!.Status);
        Assert.Contains("ne peut pas être supprimée", problem.Detail, StringComparison.OrdinalIgnoreCase);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Empty(auditLogs);
    }

    private async Task AuthenticateAsync(Guid shopId, string login, string secret)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                ShopId = shopId,
                Login = login,
                Secret = secret
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(payload);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
    }

    private async Task<Guid> SeedShopAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var id = Guid.NewGuid();
        const string sql = "INSERT INTO \"Shop\" (\"Id\", \"Name\") VALUES (@Id, @Name);";
        await connection.ExecuteAsync(sql, new { Id = id, Name = name });
        return id;
    }

    private async Task SeedShopUserAsync(Guid shopId, string login, string displayName, bool isAdmin, string secret)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = """
        INSERT INTO "ShopUser" ("Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
        VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, @SecretHash, FALSE);
        """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                Id = Guid.NewGuid(),
                ShopId = shopId,
                Login = login,
                DisplayName = displayName,
                IsAdmin = isAdmin,
                SecretHash = BCrypt.Net.BCrypt.HashPassword(secret)
            });
    }

    private async Task SeedLocationAsync(Guid shopId, string code, string label)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = """
        INSERT INTO "Location" ("Id", "Code", "Label", "ShopId")
        VALUES (@Id, @Code, @Label, @ShopId);
        """;

        await connection.ExecuteAsync(sql, new { Id = Guid.NewGuid(), Code = code, Label = label, ShopId = shopId });
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
TRUNCATE TABLE "ShopUser" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Conflict" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<IReadOnlyList<AuditLogRow>> GetAuditLogsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = "SELECT \"Id\", \"Message\", \"Actor\", \"Category\" FROM audit_logs ORDER BY \"Id\";";
        var rows = await connection.QueryAsync<AuditLogRow>(sql);
        return rows.ToList();
    }

    private async Task ClearAuditLogsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = "TRUNCATE TABLE \"audit_logs\" RESTART IDENTITY;";
        await connection.ExecuteAsync(sql);
    }

    private async Task<string?> GetShopNameAsync(Guid shopId)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = "SELECT \"Name\" FROM \"Shop\" WHERE \"Id\" = @ShopId;";
        return await connection.ExecuteScalarAsync<string?>(sql, new { ShopId = shopId });
    }

    private async Task<bool> ShopExistsAsync(Guid shopId)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = "SELECT EXISTS (SELECT 1 FROM \"Shop\" WHERE \"Id\" = @ShopId);";
        return await connection.ExecuteScalarAsync<bool>(sql, new { ShopId = shopId });
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection)
    {
        if (connection.State == ConnectionState.Open)
        {
            return;
        }

        switch (connection)
        {
            case Npgsql.NpgsqlConnection npgsql:
                await npgsql.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                break;
            default:
                connection.Open();
                break;
        }
    }

    private sealed record AuditLogRow(long Id, string Message, string? Actor, string? Category);
}
