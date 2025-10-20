using System;
using System.Collections.Generic;
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
    public async Task SearchAsync_MapsRepositoryResultsInOrder()
    {
        var service = CreateService(out var repository);
        repository.SearchResults = new[]
        {
            new ProductLookupItem(Guid.NewGuid(), "SKU-001", "Produit 001", "EAN-001", "RAW-001", null),
            new ProductLookupItem(Guid.NewGuid(), "SKU-002", "Produit 002", null, null, "445566"),
        };

        var results = await service
            .SearchAsync(" SKU-001 ", 10, hasPaging: false, pageSize: 50, offset: 0, CancellationToken.None)
            .ConfigureAwait(false);

        results.Should().HaveCount(2);
        results[0].Sku.Should().Be("SKU-001");
        results[0].Code.Should().Be("RAW-001", "le code brut prime sur l'EAN");
        results[1].Sku.Should().Be("SKU-002");
        results[1].Code.Should().Be("445566", "les chiffres sont utilisés en dernier recours");
        repository.LastQuery.Should().Be("SKU-001", "le code doit être normalisé avant la recherche");
        repository.LastLimit.Should().Be(10);
    }

    [Fact]
    public async Task SearchAsync_LimitAboveMaximum_IsClamped()
    {
        var service = CreateService(out var repository);
        repository.SearchResults = Array.Empty<ProductLookupItem>();

        var results = await service
            .SearchAsync("code", 500, hasPaging: false, pageSize: 50, offset: 0, CancellationToken.None)
            .ConfigureAwait(false);

        results.Should().BeEmpty();
        repository.LastLimit.Should().Be(50, "la limite SQL doit être bornée");
    }

    [Fact]
    public async Task SearchAsync_BlankCode_ReturnsEmpty()
    {
        var service = CreateService(out _);

        var results = await service
            .SearchAsync("   ", 5, hasPaging: false, pageSize: 50, offset: 0, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_LimitLessOrEqualToZero_Throws()
    {
        var service = CreateService(out _);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SearchAsync("code", 0, hasPaging: false, pageSize: 50, offset: 0, CancellationToken.None));

    }

    [Fact]
    public async Task SearchAsync_WithPaging_ForwardsPagingParameters()
    {
        var service = CreateService(out var repository);
        repository.SearchResults = Array.Empty<ProductLookupItem>();

        var results = await service
            .SearchAsync("code", 25, hasPaging: true, pageSize: 80, offset: 160, CancellationToken.None);

        results.Should().BeEmpty();
        repository.LastHasPaging.Should().BeTrue();
        repository.LastPageSize.Should().Be(80);
        repository.LastOffset.Should().Be(160);
    }

    private static ProductSearchService CreateService(out FakeProductLookupRepository repository)
    {
        repository = new FakeProductLookupRepository();
        return new ProductSearchService(repository);
    }

    private sealed class FakeProductLookupRepository : IProductLookupRepository
    {
        public IReadOnlyList<ProductLookupItem> SearchResults { get; set; } = Array.Empty<ProductLookupItem>();
        public string? LastQuery { get; private set; }
        public int? LastLimit { get; private set; }
        public bool? LastHasPaging { get; private set; }
        public int? LastPageSize { get; private set; }
        public int? LastOffset { get; private set; }

        public Task<ProductLookupItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken)
            => Task.FromResult<ProductLookupItem?>(null);

        public Task<IReadOnlyList<ProductLookupItem>> FindByRawCodeAsync(string rawCode, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProductLookupItem>>(Array.Empty<ProductLookupItem>());

        public Task<IReadOnlyList<ProductLookupItem>> FindByCodeDigitsAsync(string digits, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProductLookupItem>>(Array.Empty<ProductLookupItem>());

        public Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(
            string code,
            int limit,
            bool hasPaging,
            int pageSize,
            int offset,
            CancellationToken cancellationToken)
        {
            LastQuery = code;
            LastLimit = limit;
            LastHasPaging = hasPaging;
            LastPageSize = pageSize;
            LastOffset = offset;
            return Task.FromResult(SearchResults);
        }
    }
}
