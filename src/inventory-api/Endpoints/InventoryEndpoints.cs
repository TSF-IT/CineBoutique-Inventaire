using System;
using CineBoutique.Inventory.Api.Features.Inventory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CineBoutique.Inventory.Api.Endpoints;

// Type marqueur non statique, uniquement pour la catégorisation des logs
internal sealed class InventoryEndpointsMarker { }

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapRunsEndpoints();
        app.MapSessionsEndpoints();
        app.MapLocationsEndpoints();
        app.MapConflictsEndpoints();

        return app;
    }
}
