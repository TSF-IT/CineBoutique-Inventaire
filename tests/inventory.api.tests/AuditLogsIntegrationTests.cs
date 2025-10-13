using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class AuditLogsIntegrationTests : IntegrationTestBase
{
    public AuditLogsIntegrationTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task ShopCreation_PersistsAuditLogEntry()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        using var factory = new InventoryApiFactory(Fixture.ConnectionString, useTestAuditLogger: false);
        var client = factory.CreateClient();
        client.SetBearerToken(JwtTestTokenFactory.CreateAdminToken());

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/shops"),
            new CreateShopRequest { Name = "Audit Boutique" }).ConfigureAwait(false);

        await response.ShouldBeAsync(HttpStatusCode.Created, "la création de boutique doit réussir").ConfigureAwait(false);

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string sql = """
            SELECT message, actor, category
            FROM audit_logs
            ORDER BY at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        var hasRow = await reader.ReadAsync().ConfigureAwait(false);
        hasRow.Should().BeTrue("la création doit produire une entrée d'audit persistée");

        var message = reader.GetString(0);
        var actor = reader.IsDBNull(1) ? null : reader.GetString(1);
        var category = reader.IsDBNull(2) ? null : reader.GetString(2);

        actor.Should().NotBeNullOrWhiteSpace("l'audit doit identifier l'acteur ayant réalisé la modification");
        category.Should().Be("shops.create.success", "la catégorie doit refléter la création de boutique");
        message.Should().Contain("Audit Boutique").And.Contain("a créé la boutique", "le message doit contextualiser l'action exécutée");
    }
}
