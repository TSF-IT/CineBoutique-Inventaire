#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models.Admin;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public class AdminUsersEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public AdminUsersEndpointTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", EncodeCredentials("admin", "admin"));

        var host = _factory.Services.GetRequiredService<IHost>();
        DbMigrator.MigrateUp(host);

        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetUsers_WithoutAuth_ReturnsUnauthorized()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
    }

    [Fact]
    public async Task CrudFlow_WorksAndLogsAudit()
    {
        await ResetDatabaseAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/admin/users", new AdminUserCreateRequest
        {
            Email = "admin1@example.com",
            DisplayName = "Admin One"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.NotNull(created);

        var listResponse = await _client.GetAsync("/api/admin/users");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<AdminUserListResponse>();
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal(created!.Id, list.Items[0].Id);

        var getResponse = await _client.GetAsync($"/api/admin/users/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        var fetched = await getResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.NotNull(fetched);
        Assert.Equal("admin1@example.com", fetched!.Email);

        var updateResponse = await _client.PutAsJsonAsync($"/api/admin/users/{created.Id}", new AdminUserUpdateRequest
        {
            Email = "admin1@example.com",
            DisplayName = "Admin Updated"
        });

        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.NotNull(updated);
        Assert.Equal("Admin Updated", updated!.DisplayName);

        var deleteResponse = await _client.DeleteAsync($"/api/admin/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(default);

        var adminCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"AdminUser\";");
        Assert.Equal(0, adminCount);

        var auditCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"Audit\" WHERE \"EntityName\" = 'AdminUser';");
        Assert.True(auditCount >= 5);
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(default);

        const string cleanupSql =
            "TRUNCATE TABLE \"AdminUser\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Audit\" RESTART IDENTITY CASCADE;";

        await connection.ExecuteAsync(cleanupSql);
    }

    private static string EncodeCredentials(string user, string password)
    {
        var raw = Encoding.UTF8.GetBytes($"{user}:{password}");
        return Convert.ToBase64String(raw);
    }
}
