using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Infrastructure.Minimal;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Validation;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Npgsql;

namespace CineBoutique.Inventory.Api.Features.Inventory;

internal static class LocationsEndpoints
{
    public static IEndpointRouteBuilder MapLocationsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapLocationsEndpoint(app);

        return app;
    }

    private static void MapLocationsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/locations",
            async (string? shopId, int? countType, bool? includeDisabled, IDbConnection connection, CancellationToken cancellationToken) =>
        {
            if (!TryNormalizeShopId(shopId, out var parsedShopId, out var errorResult))
            {
                return errorResult;
            }

            if (countType.HasValue && countType.Value is not (1 or 2))
            {
                return Results.BadRequest(new
                {
                    message = "Le paramètre countType doit être 1 (premier passage) ou 2 (second passage)."
                });
            }

            var includeDisabledFlag = includeDisabled ?? false;

            var locations = await QueryLocationsAsync(connection, parsedShopId, countType, null, includeDisabledFlag, cancellationToken).ConfigureAwait(false);
            return Results.Ok(locations);
        })
        .WithName("GetLocations")
        .WithTags("Locations")
        .Produces<IEnumerable<LocationListItemDto>>(StatusCodes.Status200OK)
        .WithOpenApi(op =>
        {
            op.Summary = "Liste les emplacements (locations)";
            op.Description = "Retourne les métadonnées et l'état d'occupation des locations, filtré par type de comptage optionnel.";
            op.Parameters ??= new List<OpenApiParameter>();
            if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "shopId", StringComparison.OrdinalIgnoreCase)))
            {
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "shopId",
                    In = ParameterLocation.Query,
                    Required = true,
                    Description = "Identifiant unique de la boutique dont on souhaite récupérer les zones.",
                    Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
                });
            }

            if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "countType", StringComparison.OrdinalIgnoreCase)))
            {
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "countType",
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = "Type de comptage ciblé (1 pour premier passage, 2 pour second, 3 pour contrôle).",
                    Schema = new OpenApiSchema { Type = "integer", Minimum = 1 }
                });
            }

            if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "includeDisabled", StringComparison.OrdinalIgnoreCase)))
            {
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "includeDisabled",
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = "Inclut les zones désactivées lorsqu'il est vrai.",
                    Schema = new OpenApiSchema { Type = "boolean" }
                });
            }
            return op;
        });

        app.MapPost(
            "/api/locations",
            async (
                string? shopId,
                CreateLocationRequest? request,
                IDbConnection connection,
                IAuditLogger auditLogger,
                IClock clock,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (!TryNormalizeShopId(shopId, out var parsedShopId, out var errorResult))
            {
                return errorResult;
            }

            if (request is null)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le corps de la requête est requis pour créer une zone.",
                    StatusCodes.Status400BadRequest);
            }

            var normalizedCode = request.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 32)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le code zone est requis et doit contenir entre 1 et 32 caractères.",
                    StatusCodes.Status400BadRequest);
            }

            var normalizedLabel = request.Label?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedLabel) || normalizedLabel.Length > 128)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le libellé de la zone est requis et doit contenir entre 1 et 128 caractères.",
                    StatusCodes.Status400BadRequest);
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var shop = await LoadShopAsync(connection, parsedShopId, cancellationToken).ConfigureAwait(false);
            if (shop is null)
            {
                return EndpointUtilities.Problem(
                    "Boutique introuvable",
                    "Impossible de trouver la boutique ciblée.",
                    StatusCodes.Status404NotFound);
            }

            var locationId = Guid.NewGuid();

            try
            {
                const string insertSql = "INSERT INTO \"Location\" (\"Id\", \"ShopId\", \"Code\", \"Label\") VALUES (@Id, @ShopId, @Code, @Label);";
                await connection.ExecuteAsync(
                        new CommandDefinition(
                            insertSql,
                            new { Id = locationId, ShopId = parsedShopId, Code = normalizedCode, Label = normalizedLabel },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return EndpointUtilities.Problem(
                    "Code déjà utilisé",
                    $"Impossible de créer cette zone : le code « {normalizedCode} » est déjà attribué dans cette boutique.",
                    StatusCodes.Status409Conflict);
            }

            var created = (await QueryLocationsAsync(connection, parsedShopId, null, new[] { locationId }, includeDisabled: true, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault();

            var actor = EndpointUtilities.FormatActorLabel(httpContext);
            var timestamp = EndpointUtilities.FormatTimestamp(clock.UtcNow);
            var shopLabel = string.IsNullOrWhiteSpace(shop.Name) ? parsedShopId.ToString() : shop.Name;
            var zoneDescription = FormatZoneDescription(created?.Code ?? normalizedCode, created?.Label ?? normalizedLabel);
            var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);

            await auditLogger
                .LogAsync(
                    $"{actor} a créé la zone {zoneDescription} pour la boutique {shopLabel} le {timestamp} UTC.",
                    userName,
                    "locations.create.success",
                    cancellationToken)
                .ConfigureAwait(false);

            var responsePayload = created ?? new LocationListItemDto
            {
                Id = locationId,
                Code = normalizedCode,
                Label = normalizedLabel,
                IsBusy = false,
                BusyBy = null,
                ActiveRunId = null,
                ActiveCountType = null,
                ActiveStartedAtUtc = null,
                CountStatuses = Array.Empty<LocationCountStatusDto>()
            };

            return Results.Created($"/api/locations/{responsePayload.Id}", responsePayload);
        })
        .WithName("CreateLocation")
        .WithTags("Locations")
        .Produces<LocationListItemDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .AddEndpointFilter<RequireOperatorHeadersFilter>();

        app.MapPut(
            "/api/locations/{locationId:guid}",
            async (
                Guid locationId,
                string? shopId,
                UpdateLocationRequest? request,
                IDbConnection connection,
                IAuditLogger auditLogger,
                IClock clock,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (!TryNormalizeShopId(shopId, out var parsedShopId, out var errorResult))
            {
                return errorResult;
            }

            if (request is null)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le corps de la requête est requis pour modifier une zone.",
                    StatusCodes.Status400BadRequest);
            }

            string? normalizedCode = null;
            if (request.Code is not null)
            {
                normalizedCode = request.Code.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 32)
                {
                    return EndpointUtilities.Problem(
                        "Requête invalide",
                        "Le code zone doit contenir entre 1 et 32 caractères lorsqu'il est fourni.",
                        StatusCodes.Status400BadRequest);
                }
            }

            string? normalizedLabel = null;
            if (request.Label is not null)
            {
                normalizedLabel = request.Label.Trim();
                if (string.IsNullOrWhiteSpace(normalizedLabel) || normalizedLabel.Length > 128)
                {
                    return EndpointUtilities.Problem(
                        "Requête invalide",
                        "Le libellé doit contenir entre 1 et 128 caractères lorsqu'il est fourni.",
                        StatusCodes.Status400BadRequest);
                }
            }

            bool? requestedDisabled = request.Disabled;

            if (normalizedCode is null && normalizedLabel is null && requestedDisabled is null)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Aucun champ à mettre à jour n'a été fourni.",
                    StatusCodes.Status400BadRequest);
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var shop = await LoadShopAsync(connection, parsedShopId, cancellationToken).ConfigureAwait(false);
            if (shop is null)
            {
                return EndpointUtilities.Problem(
                    "Boutique introuvable",
                    "Impossible de trouver la boutique ciblée.",
                    StatusCodes.Status404NotFound);
            }

            var existing = await InventoryLocationQueries.LoadLocationMetadataAsync(connection, parsedShopId, locationId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return EndpointUtilities.Problem(
                    "Zone introuvable",
                    "Impossible de trouver cette zone pour la boutique demandée.",
                    StatusCodes.Status404NotFound);
            }

            try
            {
                const string updateSql = "UPDATE \"Location\" SET \"Code\" = COALESCE(@Code, \"Code\"), \"Label\" = COALESCE(@Label, \"Label\"), \"Disabled\" = COALESCE(@Disabled, \"Disabled\") WHERE \"Id\" = @Id AND \"ShopId\" = @ShopId;";
                var affected = await connection.ExecuteAsync(
                        new CommandDefinition(
                            updateSql,
                            new { Id = locationId, ShopId = parsedShopId, Code = normalizedCode, Label = normalizedLabel, Disabled = requestedDisabled },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (affected == 0)
                {
                    return EndpointUtilities.Problem(
                        "Zone introuvable",
                        "Impossible de trouver cette zone pour la boutique demandée.",
                        StatusCodes.Status404NotFound);
                }
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return EndpointUtilities.Problem(
                    "Code déjà utilisé",
                    "Impossible de mettre à jour cette zone : ce code est déjà attribué à une autre zone de la boutique.",
                    StatusCodes.Status409Conflict);
            }

            var updated = (await QueryLocationsAsync(connection, parsedShopId, null, new[] { locationId }, includeDisabled: true, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault();

            if (updated is null)
            {
                return EndpointUtilities.Problem(
                    "Zone introuvable",
                    "La zone a été mise à jour mais n'a pas pu être relue.",
                    StatusCodes.Status404NotFound);
            }

            var actor = EndpointUtilities.FormatActorLabel(httpContext);
            var timestamp = EndpointUtilities.FormatTimestamp(clock.UtcNow);
            var shopLabel = string.IsNullOrWhiteSpace(shop.Name) ? parsedShopId.ToString() : shop.Name;
            var beforeDescription = FormatZoneDescription(existing.Code, existing.Label);
            var afterDescription = FormatZoneDescription(updated.Code, updated.Label);
            var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);

            await auditLogger
                .LogAsync(
                    $"{actor} a mis à jour la zone {beforeDescription} en {afterDescription} pour la boutique {shopLabel} le {timestamp} UTC.",
                    userName,
                    "locations.update.success",
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(updated);
        })
        .WithName("UpdateLocation")
        .WithTags("Locations")
        .Produces<LocationListItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .AddEndpointFilter<RequireOperatorHeadersFilter>();

        app.MapDelete(
            "/api/locations/{locationId:guid}",
            async (
                Guid locationId,
                string? shopId,
                IDbConnection connection,
                IAuditLogger auditLogger,
                IClock clock,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (!TryNormalizeShopId(shopId, out var parsedShopId, out var errorResult))
            {
                return errorResult;
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var shop = await LoadShopAsync(connection, parsedShopId, cancellationToken).ConfigureAwait(false);
            if (shop is null)
            {
                return EndpointUtilities.Problem(
                    "Boutique introuvable",
                    "Impossible de trouver la boutique ciblée.",
                    StatusCodes.Status404NotFound);
            }

            var existing = await InventoryLocationQueries.LoadLocationMetadataAsync(connection, parsedShopId, locationId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return EndpointUtilities.Problem(
                    "Zone introuvable",
                    "Impossible de trouver cette zone pour la boutique demandée.",
                    StatusCodes.Status404NotFound);
            }

            const string disableSql = """
UPDATE "Location"
SET "Disabled" = TRUE
WHERE "Id" = @Id AND "ShopId" = @ShopId;
""";

            var affected = await connection.ExecuteAsync(
                    new CommandDefinition(disableSql, new { Id = locationId, ShopId = parsedShopId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (affected == 0)
            {
                return EndpointUtilities.Problem(
                    "Zone introuvable",
                    "Impossible de trouver cette zone pour la boutique demandée.",
                    StatusCodes.Status404NotFound);
            }

            var disabled = (await QueryLocationsAsync(connection, parsedShopId, null, new[] { locationId }, includeDisabled: true, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault();

            if (disabled is null)
            {
                return EndpointUtilities.Problem(
                    "Zone introuvable",
                    "La zone a été désactivée mais n'a pas pu être relue.",
                    StatusCodes.Status404NotFound);
            }

            var actor = EndpointUtilities.FormatActorLabel(httpContext);
            var timestamp = EndpointUtilities.FormatTimestamp(clock.UtcNow);
            var shopLabel = string.IsNullOrWhiteSpace(shop.Name) ? parsedShopId.ToString() : shop.Name;
            var zoneDescription = FormatZoneDescription(disabled.Code, disabled.Label);
            var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);

            await auditLogger
                .LogAsync(
                    $"{actor} a désactivé la zone {zoneDescription} pour la boutique {shopLabel} le {timestamp} UTC.",
                    userName,
                    "locations.disable.success",
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(disabled);
        })
        .WithName("DisableLocation")
        .WithTags("Locations")
        .Produces<LocationListItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter<RequireOperatorHeadersFilter>();
    }

    private static bool TryNormalizeShopId(string? shopId, out Guid parsedShopId, out IResult? errorResult)
    {
        parsedShopId = Guid.Empty;
        errorResult = null;

        if (string.IsNullOrWhiteSpace(shopId))
        {
            errorResult = EndpointUtilities.Problem(
                "Requête invalide",
                "L'identifiant de boutique est requis.",
                StatusCodes.Status400BadRequest);
            return false;
        }

        if (!Guid.TryParse(shopId, out parsedShopId))
        {
            errorResult = EndpointUtilities.Problem(
                "Requête invalide",
                "L'identifiant de boutique est invalide.",
                StatusCodes.Status400BadRequest);
            return false;
        }

        return true;
    }

    private static async Task<IReadOnlyList<LocationListItemDto>> QueryLocationsAsync(
        IDbConnection connection,
        Guid shopId,
        int? countType,
        Guid[]? filterLocationIds,
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var columnsState = await InventoryOperatorSqlHelper.DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
        var locationOperatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        var activeRunsDistinctColumns = columnsState.HasOwnerUserId
            ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OwnerUserId\""
            : columnsState.HasOperatorDisplayName
                ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OperatorDisplayName\""
                : "cr.\"LocationId\", cr.\"CountType\"";

        var activeRunsOrderByColumns = columnsState.HasOwnerUserId
            ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OwnerUserId\", cr.\"StartedAtUtc\" DESC"
            : columnsState.HasOperatorDisplayName
                ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OperatorDisplayName\", cr.\"StartedAtUtc\" DESC"
                : "cr.\"LocationId\", cr.\"CountType\", cr.\"StartedAtUtc\" DESC";

        var hasFilter = filterLocationIds is { Length: > 0 };
        var filterClause = hasFilter ? " AND l.\"Id\" = ANY(@FilterIds::uuid[])" : string.Empty;
        var activeRunsFilterClause = hasFilter ? " AND cr.\"LocationId\" = ANY(@FilterIds::uuid[])" : string.Empty;

        var sql = $@"WITH active_runs AS (
    SELECT DISTINCT ON ({activeRunsDistinctColumns})
        cr.""LocationId"",
        cr.""Id""            AS ""ActiveRunId"",
        cr.""CountType""     AS ""ActiveCountType"",
        cr.""StartedAtUtc""  AS ""ActiveStartedAtUtc"",
        {locationOperatorSql.Projection} AS ""BusyBy""
    FROM ""CountingRun"" cr
{InventoryOperatorSqlHelper.AppendJoinClause(locationOperatorSql.JoinClause)}
    WHERE cr.""CompletedAtUtc"" IS NULL
      AND (@CountType IS NULL OR cr.""CountType"" = @CountType){activeRunsFilterClause}
      AND EXISTS (SELECT 1 FROM ""CountLine"" cl WHERE cl.""CountingRunId"" = cr.""Id"")
    ORDER BY {activeRunsOrderByColumns}
)
SELECT
    l.""Id"",
    l.""Code"",
    l.""Label"",
    l.""Disabled"",
    (ar.""ActiveRunId"" IS NOT NULL) AS ""IsBusy"",
    ar.""BusyBy"",
    CASE
        WHEN ar.""ActiveRunId"" IS NULL THEN NULL
        WHEN ar.""ActiveRunId""::text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN ar.""ActiveRunId""
        ELSE NULL
    END AS ""ActiveRunId"",
    ar.""ActiveCountType"",
    ar.""ActiveStartedAtUtc""
FROM ""Location"" l
LEFT JOIN active_runs ar ON l.""Id"" = ar.""LocationId""
WHERE l.""ShopId"" = @ShopId
  AND (@IncludeDisabled OR l.""Disabled"" = FALSE){filterClause}
ORDER BY l.""Code"" ASC;";

        var sqlParameters = new
        {
            CountType = countType,
            ShopId = shopId,
            FilterIds = hasFilter ? filterLocationIds : null,
            IncludeDisabled = includeDisabled
        };

        var locations = (await connection
                .QueryAsync<LocationListItemDto>(new CommandDefinition(sql, sqlParameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false))
            .ToList();

        if (locations.Count == 0)
        {
            return Array.Empty<LocationListItemDto>();
        }

        var locationIds = locations.Select(location => location.Id).ToArray();

        var openRunsOperatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        var openRunsSql = $@"SELECT
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""          AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {openRunsOperatorSql.OwnerDisplayProjection} AS ""OwnerDisplayName"",
    {openRunsOperatorSql.OperatorDisplayProjection} AS ""OperatorDisplayName"",
    {openRunsOperatorSql.OwnerUserIdProjection} AS ""OwnerUserId""
FROM ""CountingRun"" cr
{InventoryOperatorSqlHelper.AppendJoinClause(openRunsOperatorSql.JoinClause)}
WHERE cr.""CompletedAtUtc"" IS NULL
  AND cr.""LocationId"" = ANY(@LocationIds::uuid[])
  AND EXISTS (SELECT 1 FROM ""CountLine"" cl WHERE cl.""CountingRunId"" = cr.""Id"")
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""StartedAtUtc"" DESC;";

        var completedRunsOperatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        var completedRunsSql = $@"SELECT DISTINCT ON (cr.""LocationId"", cr.""CountType"")
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""           AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {completedRunsOperatorSql.OwnerDisplayProjection} AS ""OwnerDisplayName"",
    {completedRunsOperatorSql.OperatorDisplayProjection} AS ""OperatorDisplayName"",
    {completedRunsOperatorSql.OwnerUserIdProjection} AS ""OwnerUserId""
FROM ""CountingRun"" cr
{InventoryOperatorSqlHelper.AppendJoinClause(completedRunsOperatorSql.JoinClause)}
WHERE cr.""CompletedAtUtc"" IS NOT NULL
  AND cr.""LocationId"" = ANY(@LocationIds::uuid[])
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""CompletedAtUtc"" DESC;";

        var openRuns = await connection
            .QueryAsync<LocationCountStatusRow>(new CommandDefinition(openRunsSql, new { LocationIds = locationIds }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var completedRuns = await connection
            .QueryAsync<LocationCountStatusRow>(new CommandDefinition(completedRunsSql, new { LocationIds = locationIds }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var openLookup = openRuns.ToLookup(row => (row.LocationId, row.CountType));
        var completedLookup = completedRuns.ToLookup(row => (row.LocationId, row.CountType));

        static IEnumerable<short> DiscoverCountTypes(IEnumerable<LocationCountStatusRow> runs) => runs
            .Select(row => row.CountType)
            .Where(countTypeValue => countTypeValue > 0)
            .Distinct();

        var discoveredCountTypes = DiscoverCountTypes(openRuns).Concat(DiscoverCountTypes(completedRuns));

        var defaultCountTypes = new short[] { 1, 2 };

        if (countType is { } requested)
        {
            defaultCountTypes = defaultCountTypes.Concat(new[] { (short)requested }).ToArray();
        }

        var targetCountTypes = defaultCountTypes
            .Concat(discoveredCountTypes)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        static string? NormalizeDisplayName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        static Guid? NormalizeUserId(Guid? value) => value is { } guid && guid != Guid.Empty ? guid : null;

        static DateTimeOffset? ConvertToUtcTimestamp(DateTime? value) =>
            value.HasValue ? TimeUtil.ToUtcOffset(value.Value) : (DateTimeOffset?)null;

        foreach (var location in locations)
        {
            var statuses = new List<LocationCountStatusDto>(targetCountTypes.Length);

            foreach (var type in targetCountTypes)
            {
                var status = new LocationCountStatusDto
                {
                    CountType = type,
                    Status = LocationCountStatus.NotStarted,
                    RunId = null,
                    OwnerDisplayName = null,
                    OwnerUserId = null,
                    StartedAtUtc = null,
                    CompletedAtUtc = null
                };

                var open = openLookup[(location.Id, type)].FirstOrDefault();
                if (open is not null)
                {
                    status.Status = LocationCountStatus.InProgress;
                    status.RunId = EndpointUtilities.SanitizeRunId(open.RunId);
                    status.OwnerDisplayName = NormalizeDisplayName(open.OwnerDisplayName);
                    status.OwnerUserId = NormalizeUserId(open.OwnerUserId);
                    status.StartedAtUtc = ConvertToUtcTimestamp(open.StartedAtUtc);
                    status.CompletedAtUtc = ConvertToUtcTimestamp(open.CompletedAtUtc);
                }
                else
                {
                    var completed = completedLookup[(location.Id, type)].FirstOrDefault();
                    if (completed is not null)
                    {
                        status.Status = LocationCountStatus.Completed;
                        status.RunId = EndpointUtilities.SanitizeRunId(completed.RunId);
                        status.OwnerDisplayName = NormalizeDisplayName(completed.OwnerDisplayName);
                        status.OwnerUserId = NormalizeUserId(completed.OwnerUserId);
                        status.StartedAtUtc = ConvertToUtcTimestamp(completed.StartedAtUtc);
                        status.CompletedAtUtc = ConvertToUtcTimestamp(completed.CompletedAtUtc);
                    }
                }

                statuses.Add(status);
            }

            location.CountStatuses = statuses.Count > 0 ? statuses : Array.Empty<LocationCountStatusDto>();

            var openRunsForLocation = openRuns
                .Where(r => r.LocationId == location.Id)
                .ToList();

            if (countType is { } requestedType)
            {
                var runsForRequestedType = openRunsForLocation
                    .Where(r => r.CountType == requestedType)
                    .ToList();

                location.IsBusy = runsForRequestedType.Count != 0;

                var mostRecent = runsForRequestedType
                    .OrderByDescending(r => r.StartedAtUtc)
                    .FirstOrDefault();

                location.ActiveRunId = EndpointUtilities.SanitizeRunId(mostRecent?.RunId);
                location.ActiveCountType = mostRecent?.CountType;
                location.ActiveStartedAtUtc = TimeUtil.ToUtcOffset(mostRecent?.StartedAtUtc);
                var normalizedBusy = NormalizeDisplayName(mostRecent?.OwnerDisplayName)
                    ?? NormalizeDisplayName(mostRecent?.OperatorDisplayName);
                location.BusyBy = normalizedBusy;
            }
            else
            {
                location.IsBusy = openRunsForLocation.Count != 0;

                var mostRecent = openRunsForLocation
                    .OrderByDescending(r => r.StartedAtUtc)
                    .FirstOrDefault();

                location.ActiveRunId = EndpointUtilities.SanitizeRunId(mostRecent?.RunId);
                location.ActiveCountType = mostRecent?.CountType;
                location.ActiveStartedAtUtc = TimeUtil.ToUtcOffset(mostRecent?.StartedAtUtc);
                var normalizedBusy = NormalizeDisplayName(mostRecent?.OwnerDisplayName)
                    ?? NormalizeDisplayName(mostRecent?.OperatorDisplayName);
                location.BusyBy = normalizedBusy;
            }
        }

        return locations;
    }

    private static async Task<ShopSummaryRow?> LoadShopAsync(IDbConnection connection, Guid shopId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT \"Id\", \"Name\" FROM \"Shop\" WHERE \"Id\" = @ShopId LIMIT 1;";
        return await connection
            .QuerySingleOrDefaultAsync<ShopSummaryRow>(new CommandDefinition(sql, new { ShopId = shopId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }


    private static string FormatZoneDescription(string? code, string? label)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        var normalizedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

        return normalizedCode switch
        {
            null when normalizedLabel is null => "zone inconnue",
            null => normalizedLabel!,
            _ when normalizedLabel is null => normalizedCode,
            _ => $"{normalizedCode} – {normalizedLabel}"
        };
    }

    private sealed class ShopSummaryRow
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}

internal static class InventoryLocationQueries
{
    internal static Task<LocationMetadataRow?> LoadLocationMetadataAsync(
        IDbConnection connection,
        Guid shopId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT \"Id\", \"ShopId\", \"Code\", \"Label\", \"Disabled\" FROM \"Location\" WHERE \"Id\" = @LocationId AND \"ShopId\" = @ShopId LIMIT 1;";
        return connection.QuerySingleOrDefaultAsync<LocationMetadataRow>(
            new CommandDefinition(sql, new { LocationId = locationId, ShopId = shopId }, cancellationToken: cancellationToken));
    }
}
