using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Services.Products;
using CineBoutique.Inventory.Infrastructure.Database.Products;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

public sealed class ProductSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_AggregatesStrategiesWithoutDuplicates()
    {
        var service = CreateService(out var repository);
        var skuId = Guid.NewGuid();
        var rawId = Guid.NewGuid();
        var digitsId = Guid.NewGuid();

        repository.SkuItem = new ProductLookupItem(skuId, "SKU-0001", "Edition Limitée", "1234567890123", null, "1234567890123");
        repository.RawCodeItems = new[]
        {
            new ProductLookupItem(skuId, "SKU-0001", "Edition Limitée", "1234567890123", "1234567890123", "1234567890123"),
            new ProductLookupItem(rawId, "SKU-RAW", "Pack Collector", "ABC-001", "RAW-001", "001")
        };
        repository.CodeDigitsItems = new[]
        {
            new ProductLookupItem(rawId, "SKU-RAW", "Pack Collector", "ABC-001", "RAW-001", "001"),
            new ProductLookupItem(digitsId, "SKU-DIG", "Affiche Vintage", null, null, "987654")
        };

        var results = await service.SearchAsync("SKU-0001 987654", 10, CancellationToken.None).ConfigureAwait(false);

        results.Should().HaveCount(3);
        results[0].Sku.Should().Be("SKU-0001");
        results[1].Sku.Should().Be("SKU-RAW");
        results[2].Sku.Should().Be("SKU-DIG");
    }

    [Fact]
    public async Task SearchAsync_AppliesLimitAcrossStrategies()
    {
        var service = CreateService(out var repository);
        repository.RawCodeItems = new[]
        {
            new ProductLookupItem(Guid.NewGuid(), "SKU-100", "Edition 1", "100", null, "100"),
            new ProductLookupItem(Guid.NewGuid(), "SKU-200", "Edition 2", "200", null, "200"),
            new ProductLookupItem(Guid.NewGuid(), "SKU-300", "Edition 3", "300", null, "300")
        };
        repository.CodeDigitsItems = new[]
        {
            new ProductLookupItem(Guid.NewGuid(), "SKU-400", "Edition 4", null, null, "400")
        };

        var results = await service.SearchAsync("EAN-100", 2, CancellationToken.None).ConfigureAwait(false);

        results.Should().HaveCount(2);
        results.Select(item => item.Sku).Should().Equal("SKU-100", "SKU-200");
    }

    [Fact]
    public async Task SearchAsync_BlankCode_ReturnsEmpty()
    {
        var service = CreateService(out _);

        var results = await service.SearchAsync("   ", 5, CancellationToken.None).ConfigureAwait(false);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_LimitLessOrEqualToZero_Throws()
    {
        var service = CreateService(out _);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SearchAsync("code", 0, CancellationToken.None));
    }

    private static ProductSearchService CreateService(out FakeProductLookupRepository repository)
    {
        repository = new FakeProductLookupRepository();
        return new ProductSearchService(repository);
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
