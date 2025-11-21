using CineBoutique.Inventory.Api.Configuration;
using CineBoutique.Inventory.Api.Features.Products;
using Microsoft.Extensions.Options;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var appSettings = app.ServiceProvider.GetRequiredService<IOptions<AppSettingsOptions>>().Value;
        var catalogEndpointsPublic = appSettings.CatalogEndpointsPublic;

        app.MapProductAdminEndpoints();
        app.MapProductCatalogEndpoints(catalogEndpointsPublic);
        app.MapProductImportEndpoints();

        return app;
    }
}
