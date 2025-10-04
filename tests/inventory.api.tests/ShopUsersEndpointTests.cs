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
public sealed class ShopUsersEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public ShopUsersEndpointTests(PostgresTestContainerFixture pg)
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
    public async Task GetShopUsers_ReturnsUsersForShop()
    {
        await ResetDatabaseAsync();
        await ClearAuditLogsAsync();

        var targetShopId = await SeedShopAsync("CinéBoutique Tours");
        var firstUserId = await SeedShopUserAsync(targetShopId, "user1", "Utilisateur 1", isAdmin: false, secret: string.Empty);
        var secondUserId = await SeedShopUserAsync(targetShopId, "user2", "Utilisateur 2", isAdmin: true, secret: string.Empty);
        var disabledUserId = await SeedShopUserAsync(targetShopId, "user3", "Zeta", isAdmin: false, secret: string.Empty);
        await DisableShopUserAsync(disabledUserId);

        var response = await _client.GetAsync($"/api/shops/{targetShopId}/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<ShopUserDto>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);
        Assert.Equal(new[] { secondUserId, firstUserId }, payload.Select(user => user.Id).ToArray());
        Assert.DoesNotContain(payload, user => user.Id == disabledUserId);
        Assert.All(payload, user => Assert.False(user.Disabled));
    }

    [Fact]
    public async Task CreateShopUser_PersistsUserAndWritesAudit()
    {
        await ResetDatabaseAsync();
        await ClearAuditLogsAsync();

        var targetShopId = await SeedShopAsync("CinéBoutique Dijon");

        var response = await _client.PostAsJsonAsync(
            $"/api/shops/{targetShopId}/users",
            new CreateShopUserRequest
            {
                Login = "employe1",
                DisplayName = "Employé 1",
                IsAdmin = false
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ShopUserDto>();
        Assert.NotNull(payload);
        Assert.Equal(targetShopId, payload!.ShopId);
        Assert.Equal("employe1", payload.Login);
        Assert.False(payload.Disabled);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("shop_users.create.success", entry.Category);
        Assert.Contains("employe1", entry.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await GetShopUserAsync(payload.Id);
        Assert.NotNull(stored);
        Assert.Equal("Employé 1", stored!.DisplayName);
    }

    [Fact]
    public async Task CreateShopUser_WithDuplicateLogin_ReturnsConflict()
    {
        await ResetDatabaseAsync();
        await ClearAuditLogsAsync();

        var targetShopId = await SeedShopAsync("CinéBoutique Brest");
        await SeedShopUserAsync(targetShopId, "gestionnaire", "Gestionnaire", isAdmin: false, secret: string.Empty);

        var response = await _client.PostAsJsonAsync(
            $"/api/shops/{targetShopId}/users",
            new CreateShopUserRequest
            {
                Login = "GESTIONNAIRE",
                DisplayName = "Gestionnaire 2",
                IsAdmin = false
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem!.Status);
        Assert.Contains("déjà utilisé", problem.Detail, StringComparison.OrdinalIgnoreCase);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Empty(auditLogs);
    }

    [Fact]
    public async Task UpdateShopUser_UpdatesFieldsAndWritesAudit()
    {
        await ResetDatabaseAsync();
        await ClearAuditLogsAsync();

        var targetShopId = await SeedShopAsync("CinéBoutique Metz");
        var userId = await SeedShopUserAsync(targetShopId, "caissier", "Caissier", isAdmin: false, secret: string.Empty);

        var response = await _client.PutAsJsonAsync(
            $"/api/shops/{targetShopId}/users",
            new UpdateShopUserRequest
            {
                Id = userId,
                Login = "caissier",
                DisplayName = "Responsable caisse",
                IsAdmin = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ShopUserDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.IsAdmin);
        Assert.Equal("Responsable caisse", payload.DisplayName);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("shop_users.update.success", entry.Category);
        Assert.Contains("caissier", entry.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await GetShopUserAsync(userId);
        Assert.NotNull(stored);
        Assert.True(stored!.IsAdmin);
        Assert.Equal("Responsable caisse", stored.DisplayName);
    }

    [Fact]
    public async Task DeleteShopUser_SoftDisablesUserAndWritesAudit()
    {
        await ResetDatabaseAsync();
        await ClearAuditLogsAsync();

        var targetShopId = await SeedShopAsync("CinéBoutique Pau");
        var userId = await SeedShopUserAsync(targetShopId, "vendeur", "Vendeur", isAdmin: false, secret: string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/shops/{targetShopId}/users")
        {
            Content = JsonContent.Create(new DeleteShopUserRequest { Id = userId })
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ShopUserDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Disabled);

        var auditLogs = await GetAuditLogsAsync();
        var entry = Assert.Single(auditLogs);
        Assert.Equal("shop_users.delete.success", entry.Category);
        Assert.Contains("vendeur", entry.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await GetShopUserAsync(userId);
        Assert.NotNull(stored);
        Assert.True(stored!.Disabled);
    }

    [Fact]
    public async Task DeleteShopUser_WhenUserMissing_ReturnsNotFound()
    {
        await ResetDatabaseAsync();
        await ClearAuditLogsAsync();

        var targetShopId = await SeedShopAsync("CinéBoutique Nîmes");

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/shops/{targetShopId}/users")
        {
            Content = JsonContent.Create(new DeleteShopUserRequest { Id = Guid.NewGuid() })
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem!.Status);

        var auditLogs = await GetAuditLogsAsync();
        Assert.Empty(auditLogs);
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

    private async Task<Guid> SeedShopUserAsync(Guid shopId, string login, string displayName, bool isAdmin, string secret)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var id = Guid.NewGuid();
        const string sql = """
        INSERT INTO "ShopUser" ("Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
        VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, @SecretHash, FALSE);
        """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                Id = id,
                ShopId = shopId,
                Login = login,
                DisplayName = displayName,
                IsAdmin = isAdmin,
                SecretHash = string.IsNullOrEmpty(secret) ? string.Empty : BCrypt.Net.BCrypt.HashPassword(secret)
            });

        return id;
    }

    private async Task DisableShopUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = "UPDATE \"ShopUser\" SET \"Disabled\" = TRUE WHERE \"Id\" = @Id;";
        await connection.ExecuteAsync(sql, new { Id = userId });
    }

    private async Task<ShopUserRow?> GetShopUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql = """
        SELECT "Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Disabled"
        FROM "ShopUser"
        WHERE "Id" = @Id;
        """;

        return await connection.QuerySingleOrDefaultAsync<ShopUserRow>(sql, new { Id = userId });
    }

    private async Task<IReadOnlyList<AuditLogRow>> GetAuditLogsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string sql =
            "SELECT \"id\" AS \"Id\", \"message\" AS \"Message\", \"actor\" AS \"Actor\", \"category\" AS \"Category\" FROM audit_logs ORDER BY \"at\" ASC;";
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
        _client.ClearAuth();
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

    private sealed record ShopUserRow(Guid Id, Guid ShopId, string Login, string DisplayName, bool IsAdmin, bool Disabled);
}
