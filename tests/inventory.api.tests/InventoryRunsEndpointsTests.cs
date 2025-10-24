using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class InventoryRunsEndpointsTests : IntegrationTestBase
{
    public InventoryRunsEndpointsTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task StartRun_CreatesActiveRun_ThenSummaryListsIt()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);

        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run");
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();
        started!.RunId.Should().NotBeEmpty();
        started.LocationId.Should().Be(seeded.LocationId);
        started.OwnerUserId.Should().Be(seeded.PrimaryUserId);
        started.CountType.Should().Be(1);

        await InsertCountLineAsync(started!.RunId, seeded.ProductEan, 1m).ConfigureAwait(false);

        var summaryResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/summary?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await summaryResponse.ShouldBeAsync(HttpStatusCode.OK, "get summary");
        var summary = await summaryResponse.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        summary.Should().NotBeNull();
        summary!.OpenRuns.Should().BeGreaterOrEqualTo(1);
        summary.OpenRunDetails.Should().NotBeNull();
        summary.OpenRunDetails.Should().Contain(detail => detail.RunId == started.RunId && detail.LocationId == seeded.LocationId);
    }

    [SkippableFact]
    public async Task StartRun_WithoutLines_DoesNotAppearInSummary()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);

        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run without lines");
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var summaryResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/summary?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await summaryResponse.ShouldBeAsync(HttpStatusCode.OK, "get summary without lines");
        var summary = await summaryResponse.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        summary.Should().NotBeNull();
        summary!.OpenRuns.Should().Be(0);
        summary.OpenRunDetails.Should().NotContain(detail => detail.RunId == started!.RunId);
    }

    [SkippableFact]
    public async Task CompleteRun_MakesRunCompleted_ActiveRunLookupReturnsNotFound()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run");
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var completeResponse = await CompleteRunAsync(
            client,
            seeded.LocationId,
            started!.RunId,
            seeded.PrimaryUserId,
            1,
            (seeded.ProductEan, 5m)
        ).ConfigureAwait(false);

        await completeResponse.ShouldBeAsync(HttpStatusCode.OK, "complete run");
        var completed = await completeResponse.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>().ConfigureAwait(false);
        completed.Should().NotBeNull();
        completed!.RunId.Should().Be(started.RunId);
        completed.CompletedAtUtc.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));

        var lookupResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/active-run?countType=1&ownerUserId={seeded.PrimaryUserId}")
        ).ConfigureAwait(false);

        lookupResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task CompleteRun_WhenCatalogContainsDuplicatedEans_CompletesSuccessfully()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid operatorId = Guid.Empty;
        const string duplicatedEan = "5555555555555";

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Doublons").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "LOC-DUP", "Zone Doublons").ConfigureAwait(false);
            operatorId = await seeder.CreateShopUserAsync(shopId, "dup-operator", "Opérateur Doublons").ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, "SKU-DUP-1", "Produit Doublon A", duplicatedEan).ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, "SKU-DUP-2", "Produit Doublon B", duplicatedEan).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, operatorId, 1)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run with duplicated EANs");

        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var completeResponse = await CompleteRunAsync(
            client,
            locationId,
            started!.RunId,
            operatorId,
            1,
            (duplicatedEan, 3m)
        ).ConfigureAwait(false);

        await completeResponse.ShouldBeAsync(HttpStatusCode.OK, "complete run should ignore duplicated catalog rows");
    }

    [SkippableFact]
    public async Task ReleaseRun_ReleasesLock_ThenAnotherStartIsAllowed()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var firstStart = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);

        await firstStart.ShouldBeAsync(HttpStatusCode.OK, "start run");
        var started = await firstStart.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var releaseResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/release"),
            new ReleaseRunRequest(started!.RunId, seeded.PrimaryUserId)
        ).ConfigureAwait(false);

        await releaseResponse.ShouldBeAsync(HttpStatusCode.NoContent, "release run");

        var lookupResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/active-run?countType=1&ownerUserId={seeded.PrimaryUserId}")
        ).ConfigureAwait(false);
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var secondStart = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);

        await secondStart.ShouldBeAsync(HttpStatusCode.OK, "start after release");
        var restarted = await secondStart.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        restarted.Should().NotBeNull();
        restarted!.RunId.Should().NotBe(started.RunId);
    }

    [SkippableFact]
    public async Task RestartRun_FromActiveRun_CompletesAndAllowsNewStart()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var initialStart = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 2)
        ).ConfigureAwait(false);
        await initialStart.ShouldBeAsync(HttpStatusCode.OK, "start before restart");
        var started = await initialStart.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var restartResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/restart"),
            new RestartRunRequest(seeded.PrimaryUserId, 2)
        ).ConfigureAwait(false);
        await restartResponse.ShouldBeAsync(HttpStatusCode.NoContent, "restart");

        var lookupResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/active-run?countType=2&ownerUserId={seeded.PrimaryUserId}")
        ).ConfigureAwait(false);
        lookupResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var newStart = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 2)
        ).ConfigureAwait(false);
        await newStart.ShouldBeAsync(HttpStatusCode.OK, "start after restart");
        var restarted = await newStart.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        restarted.Should().NotBeNull();
        restarted!.RunId.Should().NotBe(started!.RunId);
    }

    [SkippableFact]
    public async Task DeleteRun_RemovesIt_ThenGetByIdReturns404()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run");
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var deleteUri = client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/runs/{started!.RunId}?ownerUserId={seeded.PrimaryUserId}");
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, deleteUri);
        var deleteResponse = await client.SendAsync(deleteRequest).ConfigureAwait(false);
        await deleteResponse.ShouldBeAsync(HttpStatusCode.NoContent, "delete run");

        var getResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/runs/{started.RunId}")
        ).ConfigureAwait(false);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task StartRun_WhenAlreadyActiveWithAnotherOperator_Returns409()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var firstStart = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);
        await firstStart.ShouldBeAsync(HttpStatusCode.OK, "initial start");

        var secondStart = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.SecondaryUserId, 1)
        ).ConfigureAwait(false);

        secondStart.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task CompleteRun_WhenRunMissing_Returns404()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var missingRunId = Guid.NewGuid();
        var completeResponse = await CompleteRunAsync(
            client,
            seeded.LocationId,
            missingRunId,
            seeded.PrimaryUserId,
            1,
            (seeded.ProductEan, 2m)
        ).ConfigureAwait(false);

        completeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task AnyRunEndpoint_WithUnknownLocation_Returns404()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid operatorId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Orpheline").ConfigureAwait(false);
            operatorId = await seeder.CreateShopUserAsync(shopId, "orphan", "Opérateur").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        var unknownLocation = Guid.NewGuid();

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{unknownLocation}/start"),
            new StartRunRequest(shopId, operatorId, 1)
        ).ConfigureAwait(false);
        startResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var releaseResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{unknownLocation}/release"),
            new ReleaseRunRequest(Guid.NewGuid(), operatorId)
        ).ConfigureAwait(false);
        releaseResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(Guid ShopId, Guid LocationId, Guid PrimaryUserId, Guid SecondaryUserId, string ProductSku, string ProductEan)> SeedInventoryContextAsync()
    {
        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid primaryUserId = Guid.Empty;
        Guid secondaryUserId = Guid.Empty;
        const string productSku = "SKU-RUN-001";
        const string productEan = "1234567890123";

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Inventaire").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "LOC-RUN", "Zone Inventaire").ConfigureAwait(false);
            primaryUserId = await seeder.CreateShopUserAsync(shopId, "alice", "Alice").ConfigureAwait(false);
            secondaryUserId = await seeder.CreateShopUserAsync(shopId, "bob", "Bob").ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, productSku, "Produit Inventaire", productEan).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (shopId, locationId, primaryUserId, secondaryUserId, productSku, productEan);
    }

    private async Task InsertCountLineAsync(Guid runId, string ean, decimal quantity)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        Guid productId;
        await using (var lookup = new NpgsqlCommand("SELECT \"Id\" FROM \"Product\" WHERE \"Ean\" = @ean LIMIT 1;", connection, transaction))
        {
            lookup.Parameters.AddWithValue("ean", ean);
            var result = await lookup.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is not Guid id)
            {
                throw new InvalidOperationException($"Produit introuvable pour l'EAN {ean}.");
            }

            productId = id;
        }

        await using (var insert = new NpgsqlCommand(
                   "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@id, @runId, @productId, @quantity, @at);",
                   connection,
                   transaction))
        {
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("runId", runId);
            insert.Parameters.AddWithValue("productId", productId);
            insert.Parameters.AddWithValue("quantity", quantity);
            insert.Parameters.AddWithValue("at", DateTimeOffset.UtcNow);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private static Task<HttpResponseMessage> CompleteRunAsync(
        HttpClient client,
        Guid locationId,
        Guid runId,
        Guid ownerUserId,
        short countType,
        params (string ean, decimal quantity)[] items)
    {
        var payload = new CompleteRunRequest(
            runId,
            ownerUserId,
            countType,
            items.Select(item => new CompleteRunItemRequest(item.ean, item.quantity, false)).ToArray()
        );

        return client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            payload
        );
    }
}
