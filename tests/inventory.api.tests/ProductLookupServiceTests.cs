using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Services.Products;
using CineBoutique.Inventory.Infrastructure.Database.Products;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

public sealed class ProductLookupServiceTests
{
    [Fact]
    public async Task ResolveAsync_BySku_ReturnsProduct()
    {
        var service = CreateService(out var repository);
        var expected = new ProductLookupItem(Guid.NewGuid(), "SKU-42", "Collector", "9876543210123", null, "9876543210123");
        repository.SkuItem = expected;

        var result = await service.ResolveAsync("SKU-42", CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(ProductLookupStatus.Success);
        result.Product.Should().NotBeNull();
        result.Product!.Id.Should().Be(expected.Id);
        result.Product.Sku.Should().Be("SKU-42");
        result.Digits.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ByRawCode_ReturnsProduct()
    {
        var service = CreateService(out var repository);
        var expected = new ProductLookupItem(Guid.NewGuid(), "SKU-99", "Edition Rare", "1112223334445", "ABC-111222", "1112223334445");
        repository.RawCodeItems = new[] { expected };

        var result = await service.ResolveAsync("1112223334445", CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(ProductLookupStatus.Success);
        result.Product.Should().NotBeNull();
        result.Product!.Sku.Should().Be("SKU-99");
        result.Product.Ean.Should().Be("1112223334445");
    }

    [Fact]
    public async Task ResolveAsync_ByDigits_ReturnsProduct()
    {
        var service = CreateService(out var repository);
        var expected = new ProductLookupItem(Guid.NewGuid(), "SKU-100", "Steelbook", null, null, "445566");
        repository.CodeDigitsItems = new[] { expected };

        var result = await service.ResolveAsync("Code#4455-66", CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(ProductLookupStatus.Success);
        result.Product.Should().NotBeNull();
        result.Product!.Sku.Should().Be("SKU-100");
        result.Digits.Should().Be("445566");
    }

    [Fact]
    public async Task ResolveAsync_ByDigitsWithMultipleMatches_ReturnsConflict()
    {
        var service = CreateService(out var repository);
        repository.CodeDigitsItems = new[]
        {
            new ProductLookupItem(Guid.NewGuid(), "SKU-101", "Combo", "400100200", null, "400100200"),
            new ProductLookupItem(Guid.NewGuid(), "SKU-102", "Collector", null, "ALT-400100200", "400100200"),
        };

        var result = await service.ResolveAsync("400-100-200", CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(ProductLookupStatus.Conflict);
        result.Digits.Should().Be("400100200");
        result.Matches.Should().HaveCount(2);
        result.Matches.Should().Contain(match => match.Sku == "SKU-101" && match.Code == "400100200");
        result.Matches.Should().Contain(match => match.Sku == "SKU-102" && match.Code == "ALT-400100200");
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsNotFound()
    {
        var service = CreateService(out _);

        var result = await service.ResolveAsync("   ???   ", CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(ProductLookupStatus.NotFound);
        result.Product.Should().BeNull();
    }

    private static ProductLookupService CreateService(out FakeProductLookupRepository repository)
    {
        repository = new FakeProductLookupRepository();
        return new ProductLookupService(repository);
    }

    private sealed class FakeProductLookupRepository : IProductLookupRepository
    {
        public ProductLookupItem? SkuItem { get; set; }
        public IReadOnlyList<ProductLookupItem> RawCodeItems { get; set; } = Array.Empty<ProductLookupItem>();
        public IReadOnlyList<ProductLookupItem> CodeDigitsItems { get; set; } = Array.Empty<ProductLookupItem>();

        public Task<ProductLookupItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken)
            => Task.FromResult(SkuItem);

        public Task<IReadOnlyList<ProductLookupItem>> FindByRawCodeAsync(string rawCode, CancellationToken cancellationToken)
            => Task.FromResult(RawCodeItems);

        public Task<IReadOnlyList<ProductLookupItem>> FindByCodeDigitsAsync(string digits, CancellationToken cancellationToken)
            => Task.FromResult(CodeDigitsItems);
    }
}
