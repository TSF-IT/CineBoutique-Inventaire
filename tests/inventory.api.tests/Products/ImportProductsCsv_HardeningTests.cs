using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
public sealed class ImportProductsCsv_HardeningTests : IntegrationTestBase
{
    private static readonly string[] SampleCsvLines =
    {
        "barcode_rfid;item;descr",
        "1234567890123;SKU-ONE;Produit 1",
        "9999999999999;SKU-TWO;Produit 2"
    };

    public ImportProductsCsv_HardeningTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task ImportProductsCsv_WhenGlobalLockHeld_Returns423Locked()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        var shopId = await ResetAndCreateShopAsync("Boutique Import CSV Hardening - Lock").ConfigureAwait(false);

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(297351);", connection);
        await lockCommand.ExecuteScalarAsync().ConfigureAwait(false);

        try
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Admin", "true");

            using var content = CreateCsvContent(SampleCsvLines);
            var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);
            await response.ShouldBeAsync(HttpStatusCode.Locked, "le verrou global doit empêcher les imports concurrents").ConfigureAwait(false);

            using var body = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
            body.Should().NotBeNull();
            body!.RootElement.TryGetProperty("reason", out var reason).Should().BeTrue();
            reason.GetString().Should().Be("import_in_progress");
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand("SELECT pg_advisory_unlock(297351);", connection);
            await unlockCommand.ExecuteScalarAsync().ConfigureAwait(false);
        }
    }

    [SkippableFact]
    public async Task ImportProductsCsv_ReimportSameFile_IsSkippedWith204()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        var shopId = await ResetAndCreateShopAsync("Boutique Import CSV Hardening - Skip").ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var firstContent = CreateCsvContent(SampleCsvLines))
        {
            var firstResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", firstContent).ConfigureAwait(false);
            await firstResponse.ShouldBeAsync(HttpStatusCode.OK, "le premier import doit réussir").ConfigureAwait(false);
        }

        using var duplicateContent = CreateCsvContent(SampleCsvLines);
        var duplicateResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", duplicateContent).ConfigureAwait(false);
        await duplicateResponse.ShouldBeAsync(HttpStatusCode.NoContent, "un import identique doit être ignoré").ConfigureAwait(false);

        var payload = await duplicateResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Skipped.Should().BeTrue();
        payload.DryRun.Should().BeFalse();
        payload.Inserted.Should().Be(0);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(0);
        payload.UnknownColumns.Should().BeEmpty();
        payload.ProposedGroups.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task ImportProductsCsv_DryRun_WritesNoData()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        var shopId = await ResetAndCreateShopAsync("Boutique Import CSV Hardening - DryRun").ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using var dryRunContent = CreateCsvContent(SampleCsvLines);
        var dryRunResponse = await client.PostAsync($"/api/shops/{shopId}/products/import?dryRun=true", dryRunContent).ConfigureAwait(false);
        await dryRunResponse.ShouldBeAsync(HttpStatusCode.OK, "un dryRun doit s'exécuter sans erreur").ConfigureAwait(false);

        var payload = await dryRunResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.DryRun.Should().BeTrue();
        payload.Inserted.Should().Be(0);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(2);
        payload.Skipped.Should().BeFalse();
        payload.UnknownColumns.Should().BeEmpty();
        payload.ProposedGroups.Should().BeEmpty();

        var randomSku = $"SKU-{Guid.NewGuid():N}";
        var getResponse = await client.GetAsync($"/api/products/{randomSku}").ConfigureAwait(false);
        await getResponse.ShouldBeAsync(HttpStatusCode.NotFound, "un dryRun ne doit pas insérer de données").ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ImportProductsCsv_StreamExceedingLimit_Returns413()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        var shopId = await ResetAndCreateShopAsync("Boutique Import CSV Hardening - Payload").ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        const long maxCsvSizeBytes = 25L * 1024L * 1024L;
        using var largeStream = new LargeTestStream(maxCsvSizeBytes + 1);
        using var content = new StreamContent(largeStream, 81920);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);
        await response.ShouldBeAsync((HttpStatusCode)StatusCodes.Status413PayloadTooLarge, "un flux supérieur à la limite doit être rejeté").ConfigureAwait(false);
    }

    private async Task<Guid> ResetAndCreateShopAsync(string name)
    {
        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync(name).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return shopId;
    }

    private static StringContent CreateCsvContent(string[] lines)
    {
        var csv = string.Join('\n', lines);
        var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv")
        {
            CharSet = Encoding.UTF8.WebName
        };

        return content;
    }

    private sealed class LargeTestStream : Stream
    {
        private readonly long _length;
        private long _position;

        public LargeTestStream(long length)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (_position >= _length)
            {
                return 0;
            }

            var remaining = (int)Math.Min(count, _length - _position);
            Array.Fill(buffer, (byte)'A', offset, remaining);
            _position += remaining;
            return remaining;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _length)
            {
                return ValueTask.FromResult(0);
            }

            var remaining = (int)Math.Min(buffer.Length, _length - _position);
            buffer.Span[..remaining].Fill((byte)'A');
            _position += remaining;
            return ValueTask.FromResult(remaining);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
