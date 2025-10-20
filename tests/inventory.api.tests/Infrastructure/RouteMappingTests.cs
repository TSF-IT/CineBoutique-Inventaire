using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public class RouteMappingTests : IClassFixture<TestApiFactory>
{
  private readonly TestApiFactory _f;
  public RouteMappingTests(TestApiFactory f) { _f = f; }

  [Fact]
  public void ImportEndpoint_IsSingleAndRequiresAuthorization()
  {
    // Récupère le EndpointDataSource depuis le host de tests
    var sources = _f.Services.GetRequiredService<IEnumerable<EndpointDataSource>>().ToArray();
    Assert.NotEmpty(sources);

    var endpoints = sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
    Assert.NotEmpty(endpoints);

    // Filtre : POST /api/products/import
    var matches = endpoints.Where(ep =>
      string.Equals(ep.RoutePattern.RawText, "/api/products/import", System.StringComparison.OrdinalIgnoreCase) &&
      (ep.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods?.Contains("POST") ?? false)
    ).ToArray();

    Assert.True(matches.Length == 1, $"Expected exactly 1 POST /api/products/import, found {matches.Length}.");

    // Vérifie qu'il y a une métadonnée d'autorisation (RequireAuthorization)
    var meta = matches[0].Metadata.GetOrderedMetadata<IAuthorizeData>();
    Assert.True(meta is { Count: > 0 }, "Expected /api/products/import to require authorization.");
  }
}
