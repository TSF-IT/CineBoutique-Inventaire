using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class LocationsEndpointsTests : IntegrationTestBase
{
    public LocationsEndpointsTests(InventoryApiFixture fx)
    {
        UseFixture(fx);
    }

    [SkippableFact]
    public async Task CreateAndUpdateLocation_Workflow()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Zones").ConfigureAwait(false);
            await seeder.CreateLocationAsync(shopId, "EXIST", "Zone existante").ConfigureAwait(false);
        }).ConfigureAwait(false);

        Fixture.ClearAuditLogs();

        var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}"),
            new CreateLocationRequest
            {
                Code = "NOUV",
                Label = "Zone nouvelle"
            }).ConfigureAwait(false);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<LocationListItemDto>().ConfigureAwait(false);
        created.Should().NotBeNull();
        created!.Code.Should().Be("NOUV");
        created.Label.Should().Be("Zone nouvelle");
        created.Disabled.Should().BeFalse();
        created.CountStatuses.Should().NotBeNull();

        var listResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}"))
            .ConfigureAwait(false);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        list.Should().NotBeNull();
        list!.Any(item => item.Id == created.Id).Should().BeTrue();

        var updateResponse = await client.PutAsJsonAsync(
            client.CreateRelativeUri($"/api/locations/{created.Id}?shopId={shopId}"),
            new UpdateLocationRequest
            {
                Code = "UPDT",
                Label = "Zone mise à jour"
            }).ConfigureAwait(false);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<LocationListItemDto>().ConfigureAwait(false);
        updated.Should().NotBeNull();
        updated!.Code.Should().Be("UPDT");
        updated.Label.Should().Be("Zone mise à jour");
        updated.Disabled.Should().BeFalse();

        var auditEntries = Fixture.DrainAuditLogs();
        auditEntries.Should().NotBeNull();
        auditEntries.Should().Contain(entry => entry.Category == "locations.create.success");
        auditEntries.Should().Contain(entry => entry.Category == "locations.update.success");
    }

    [SkippableFact]
    public async Task UpdateLocation_NotFound_ReturnsProblem()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Introuvable").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        var response = await client.PutAsJsonAsync(
            client.CreateRelativeUri($"/api/locations/{Guid.NewGuid()}?shopId={shopId}"),
            new UpdateLocationRequest
            {
                Label = "Zone fantôme"
            }).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>().ConfigureAwait(false);
        problem.Should().NotBeNull();
        problem!.Detail.Should().Be("Impossible de trouver cette zone pour la boutique demandée.");
    }

    [SkippableFact]
    public async Task CreateLocation_DuplicateCode_ReturnsConflict()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Doublon Zone").ConfigureAwait(false);
            await seeder.CreateLocationAsync(shopId, "DUP", "Zone initiale").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}"),
            new CreateLocationRequest
            {
                Code = "dup",
                Label = "Zone dupliquée"
            }).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>().ConfigureAwait(false);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Code déjà utilisé");
        problem.Detail.Should().Be("Impossible de créer cette zone : le code « DUP » est déjà attribué dans cette boutique.");
    }

    [SkippableFact]
    public async Task DisableLocation_SoftDeletesAndHidesByDefault()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Désactivation").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "DIS1", "Zone à désactiver").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        var disableResponse = await client.DeleteAsync(
            client.CreateRelativeUri($"/api/locations/{locationId}?shopId={shopId}"))
            .ConfigureAwait(false);

        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<LocationListItemDto>().ConfigureAwait(false);
        disabled.Should().NotBeNull();
        disabled!.Disabled.Should().BeTrue();

        var activeListResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}"))
            .ConfigureAwait(false);
        activeListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeList = await activeListResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        activeList.Should().NotBeNull();
        activeList!.Any(item => item.Id == locationId).Should().BeFalse("la zone désactivée est masquée par défaut");

        var allListResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}&includeDisabled=true"))
            .ConfigureAwait(false);
        allListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var allList = await allListResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        allList.Should().NotBeNull();
        var disabledItem = allList!.SingleOrDefault(item => item.Id == locationId);
        disabledItem.Should().NotBeNull();
        disabledItem!.Disabled.Should().BeTrue();
    }

    [SkippableFact]
    public async Task DisableLocationStatus_WithExistingCounts_ReturnsConflict()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid productId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Statut Conflit").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "STATC1", "Zone statut conflit").ConfigureAwait(false);
            productId = await seeder.CreateProductAsync(shopId, "STATSKU1", "Produit statut conflit").ConfigureAwait(false);
        }).ConfigureAwait(false);

        await InsertCountingRunWithLineAsync(locationId, productId).ConfigureAwait(false);

        var client = CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            client.CreateRelativeUri($"/api/locations/{locationId}/status?shopId={shopId}"))
        {
            Content = JsonContent.Create(new UpdateLocationStatusRequest { IsActive = false })
        };

        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        var root = payload!.RootElement;
        root.GetProperty("locationId").GetGuid().Should().Be(locationId);
        root.GetProperty("counts").GetProperty("lines").GetInt32().Should().BeGreaterThan(0);

        var remaining = await GetCountLineCountAsync(locationId).ConfigureAwait(false);
        remaining.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task DisableLocationStatus_WithForcePurgesCounts()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid productId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Statut Force").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "STATF1", "Zone statut force").ConfigureAwait(false);
            productId = await seeder.CreateProductAsync(shopId, "STATSKU2", "Produit statut force").ConfigureAwait(false);
        }).ConfigureAwait(false);

        await InsertCountingRunWithLineAsync(locationId, productId).ConfigureAwait(false);

        var client = CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            client.CreateRelativeUri($"/api/locations/{locationId}/status?shopId={shopId}"))
        {
            Content = JsonContent.Create(new UpdateLocationStatusRequest { IsActive = false, Force = true })
        };

        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        var root = payload!.RootElement;
        root.GetProperty("locationId").GetGuid().Should().Be(locationId);
        root.GetProperty("deactivated").GetBoolean().Should().BeTrue();
        root.GetProperty("purged").GetBoolean().Should().BeTrue();
        root.GetProperty("purgedLines").GetInt32().Should().BeGreaterThan(0);

        var remaining = await GetCountLineCountAsync(locationId).ConfigureAwait(false);
        remaining.Should().Be(0);
    }

    [SkippableFact]
    public async Task DisableLocationStatus_WithForceButConflicts_ReturnsPurgeConflict()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid productId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Statut Purge").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "STATP1", "Zone statut purge").ConfigureAwait(false);
            productId = await seeder.CreateProductAsync(shopId, "STATSKU3", "Produit statut purge").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var (_, lineId) = await InsertCountingRunWithLineAsync(locationId, productId).ConfigureAwait(false);
        await CreateConflictForLineAsync(lineId).ConfigureAwait(false);

        var client = CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            client.CreateRelativeUri($"/api/locations/{locationId}/status?shopId={shopId}"))
        {
            Content = JsonContent.Create(new UpdateLocationStatusRequest { IsActive = false, Force = true })
        };

        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        var root = payload!.RootElement;
        root.GetProperty("title").GetString().Should().Be("Purge impossible");

        var remaining = await GetCountLineCountAsync(locationId).ConfigureAwait(false);
        remaining.Should().BeGreaterThan(0);
    }

    private async Task<(Guid RunId, Guid LineId)> InsertCountingRunWithLineAsync(Guid locationId, Guid productId)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        var sessionId = Guid.NewGuid();
        await using (var session = new NpgsqlCommand(
                   "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@id, @name, @startedAt);",
                   connection,
                   transaction))
        {
            session.Parameters.AddWithValue("id", sessionId);
            session.Parameters.AddWithValue("name", "Session Statut");
            session.Parameters.AddWithValue("startedAt", DateTimeOffset.UtcNow);
            await session.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var runId = Guid.NewGuid();
        await using (var run = new NpgsqlCommand(
                   "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\", \"StartedAtUtc\", \"CompletedAtUtc\", \"OperatorDisplayName\") VALUES (@id, @sessionId, @locationId, @countType, @startedAt, NULL, @operator);",
                   connection,
                   transaction))
        {
            run.Parameters.AddWithValue("id", runId);
            run.Parameters.AddWithValue("sessionId", sessionId);
            run.Parameters.AddWithValue("locationId", locationId);
            run.Parameters.AddWithValue("countType", (short)1);
            run.Parameters.AddWithValue("startedAt", DateTimeOffset.UtcNow);
            run.Parameters.AddWithValue("operator", "Tests");
            await run.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var lineId = Guid.NewGuid();
        await using (var line = new NpgsqlCommand(
                   "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@id, @runId, @productId, @quantity, @countedAt);",
                   connection,
                   transaction))
        {
            line.Parameters.AddWithValue("id", lineId);
            line.Parameters.AddWithValue("runId", runId);
            line.Parameters.AddWithValue("productId", productId);
            line.Parameters.AddWithValue("quantity", 1m);
            line.Parameters.AddWithValue("countedAt", DateTimeOffset.UtcNow);
            await line.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);

        return (runId, lineId);
    }

    private async Task<int> GetCountLineCountAsync(Guid locationId)
    {
        const string sql = """
SELECT COUNT(*)
FROM "CountLine" cl
JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
WHERE cr."LocationId" = @locationId;
""";

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters = { new NpgsqlParameter("locationId", locationId) }
        };

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private async Task CreateConflictForLineAsync(Guid countLineId)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"Notes\", \"CreatedAtUtc\", \"ResolvedAtUtc\") VALUES (@id, @lineId, @status, NULL, @createdAt, NULL);",
            connection)
        {
            Parameters =
            {
                new NpgsqlParameter("id", Guid.NewGuid()),
                new NpgsqlParameter("lineId", countLineId),
                new NpgsqlParameter("status", "open"),
                new NpgsqlParameter("createdAt", DateTimeOffset.UtcNow)
            }
        };

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
