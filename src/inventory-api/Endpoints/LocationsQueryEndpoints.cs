using System.Data;
using System.Data.Common;
using System.Linq;
using CineBoutique.Inventory.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class LocationsQueryEndpoints
{
    public static IEndpointRouteBuilder MapLocationsQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGet(
                "/locations",
                async Task<IResult> (
                    string? shopId,
                    IDbConnection connection,
                    CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(shopId))
                    {
                        return Results.BadRequest(new { error = "shopId is required" });
                    }

                    if (!Guid.TryParse(shopId, out var parsedShopId) || parsedShopId == Guid.Empty)
                    {
                        return Results.BadRequest(new { error = "shopId must be a non-empty GUID" });
                    }

                    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

                    var locations = (await connection
                            .QueryAsync<LocationRow>(
                                new CommandDefinition(
                                    LocationSql,
                                    new { ShopId = parsedShopId },
                                    cancellationToken: cancellationToken)))
                        .ToList();

                    if (locations.Count == 0)
                    {
                        return Results.Ok(Array.Empty<LocationDto>());
                    }

                    var statusRows = await connection
                        .QueryAsync<CountStatusRow>(
                            new CommandDefinition(
                                StatusSql,
                                new { ShopId = parsedShopId },
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);

                    var statusLookup = statusRows.ToLookup(row => row.LocationId);

                    var results = new List<LocationDto>(locations.Count);

                    foreach (var location in locations)
                    {
                        var rows = statusLookup[location.Id]
                            .OrderBy(row => row.CountType)
                            .ThenByDescending(row => row.StartedAtUtc ?? DateTimeOffset.MinValue)
                            .ToList();

                        var activeRow = rows
                            .Where(row => string.Equals(row.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(row => row.StartedAtUtc ?? DateTimeOffset.MinValue)
                            .FirstOrDefault();

                        var statusDtos = rows
                            .Select(row => new CountStatusDto(
                                row.CountType,
                                row.Status,
                                NormalizeGuid(row.RunId),
                                NormalizeDisplayName(row.OwnerDisplayName),
                                NormalizeGuid(row.OwnerUserId),
                                row.StartedAtUtc,
                                row.CompletedAtUtc))
                            .ToList();

                        var countStatuses = statusDtos.Count > 0
                            ? statusDtos
                            : Array.Empty<CountStatusDto>();

                        results.Add(
                            new LocationDto(
                                location.Id,
                                location.Code,
                                location.Label,
                                activeRow is not null,
                                NormalizeDisplayName(activeRow?.OwnerDisplayName),
                                NormalizeGuid(activeRow?.RunId),
                                activeRow?.CountType,
                                activeRow?.StartedAtUtc,
                                countStatuses));
                    }

                    return Results.Ok(results);
                })
            .WithName("GetLocationsV2")
            .WithTags("Locations")
            .Produces<IEnumerable<LocationDto>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }

    private static string? NormalizeDisplayName(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Guid? NormalizeGuid(Guid? value)
        => value is { } guid && guid != Guid.Empty ? guid : null;

    private const string LocationSql = @"SELECT
    l.""Id"",
    l.""Code"",
    l.""Label""
FROM ""Location"" l
WHERE l.""ShopId"" = @ShopId
ORDER BY l.""Code"" ASC;";

    private const string StatusSql = @"SELECT
    cr.""LocationId"",
    cr.""CountType""::smallint AS ""CountType"",
    CASE WHEN cr.""CompletedAtUtc"" IS NULL THEN 'in_progress' ELSE 'completed' END AS ""Status"",
    cr.""Id"" AS ""RunId"",
    su.""DisplayName"" AS ""OwnerDisplayName"",
    cr.""OwnerUserId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
LEFT JOIN ""ShopUser"" su ON su.""Id"" = cr.""OwnerUserId""
WHERE l.""ShopId"" = @ShopId
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""StartedAtUtc"" DESC;";

    private sealed record LocationRow(Guid Id, string Code, string Label);

    private sealed record CountStatusRow(
        Guid LocationId,
        short CountType,
        string Status,
        Guid? RunId,
        string? OwnerDisplayName,
        Guid? OwnerUserId,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);
}
