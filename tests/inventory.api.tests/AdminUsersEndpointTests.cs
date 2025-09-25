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
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models.Admin;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class AdminUsersEndpointTests : IAsyncLifetime, IDisposable
{
    private readonly PostgresTestContainerFixture _pg;
    private readonly ITestOutputHelper _output;
    private InventoryApiApplicationFactory? _rootFactory;
    private WebApplicationFactory<Program>? _app;
    private HttpClient? _client;
    private bool _disposed;

    private InventoryApiApplicationFactory Root =>
    _rootFactory ?? throw new ObjectDisposedException(nameof(InventoryApiApplicationFactory));

    private WebApplicationFactory<Program> App =>
        _app ?? throw new ObjectDisposedException(nameof(WebApplicationFactory<Program>));

    private HttpClient Client =>
        _client ?? throw new ObjectDisposedException(nameof(HttpClient));

    public AdminUsersEndpointTests(PostgresTestContainerFixture pg, ITestOutputHelper output)
    {
        _pg = pg;
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _rootFactory = new InventoryApiApplicationFactory(_pg.ConnectionString);

        _app = _rootFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddProvider(new TestOutputLoggerProvider(_output));
                loggingBuilder.SetMinimumLevel(LogLevel.Debug);
            });
        });

        _client = App.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await Root.EnsureMigratedAsync().ConfigureAwait(false);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", EncodeCredentials("admin", "admin"));

        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetUsers_WithoutAuth_ReturnsUnauthorized()
    {
        await ResetDatabaseAsync();

        using var client = App.CreateClient();
        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
    }

    [Fact]
    public async Task CrudFlow_WorksAndLogsAudit()
    {
        await ResetDatabaseAsync();

        var ct = CancellationToken.None;

        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin{unique}@example.com";
        var displayName = $"Admin {unique}";

        var createResponse = await Client.PostAsJsonAsync("/api/admin/users", new AdminUserCreateRequest
        {
            Email = email,
            DisplayName = displayName
        }, ct);

        if (createResponse.StatusCode != HttpStatusCode.Created)
        {
            var body = await createResponse.Content.ReadAsStringAsync(ct);
            _output.WriteLine($"CreateAdminUser failed: {(int)createResponse.StatusCode} {createResponse.StatusCode} - {body}");
        }

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.NotNull(created);

        var listResponse = await Client.GetAsync("/api/admin/users");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<AdminUserListResponse>();
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal(created!.Id, list.Items[0].Id);

        var getResponse = await Client.GetAsync($"/api/admin/users/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        var fetched = await getResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.NotNull(fetched);
        Assert.Equal(email, fetched!.Email);

        var duplicateResponse = await Client.PostAsJsonAsync("/api/admin/users", new AdminUserCreateRequest
        {
            Email = email,
            DisplayName = $"Duplicate {displayName}"
        }, ct);

        if (duplicateResponse.StatusCode != HttpStatusCode.Conflict)
        {
            var duplicateBody = await duplicateResponse.Content.ReadAsStringAsync(ct);
            _output.WriteLine($"Duplicate CreateAdminUser failed: {(int)duplicateResponse.StatusCode} {duplicateResponse.StatusCode} - {duplicateBody}");
        }

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        var updateResponse = await Client.PutAsJsonAsync($"/api/admin/users/{created.Id}", new AdminUserUpdateRequest
        {
            Email = email,
            DisplayName = "Admin Updated"
        });

        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<AdminUserDto>();
        Assert.NotNull(updated);
        Assert.Equal("Admin Updated", updated!.DisplayName);

        var deleteResponse = await Client.DeleteAsync($"/api/admin/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = App.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(default);

        var adminCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM admin_users;");
        Assert.Equal(0, adminCount);

        var auditCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_logs WHERE category = 'AdminUser';");
        Assert.True(auditCount >= 5);
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = App.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(default);

        const string cleanupSql =
            "TRUNCATE TABLE admin_users RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Audit\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE audit_logs RESTART IDENTITY CASCADE;";

        await connection.ExecuteAsync(cleanupSql);
    }

    private static string EncodeCredentials(string user, string password)
    {
        var raw = Encoding.UTF8.GetBytes($"{user}:{password}");
        return Convert.ToBase64String(raw);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _client?.Dispose();
            _client = null;

            _app?.Dispose();
            _app = null;

            _rootFactory?.Dispose();
            _rootFactory = null;  
        }

        _disposed = true;
    }
}
