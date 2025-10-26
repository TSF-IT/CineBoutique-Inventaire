using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CineBoutique.Inventory.Api.Features.Products;

internal static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapProductImportEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapShopImportEndpoints(app);

        return app;
    }

    private static void MapShopImportEndpoints(IEndpointRouteBuilder app)
    {
        // IMPORT CSV PAR BOUTIQUE (écrase ou merge selon service d'import)
        app.MapPost("/api/shops/{shopId:guid}/products/import", async (
            System.Guid shopId,
            Microsoft.AspNetCore.Http.HttpRequest request,
            System.Data.IDbConnection connection,
            CineBoutique.Inventory.Infrastructure.Locks.IImportLockService importLockService,
            string? dryRun,
            string? mode,
            System.Threading.CancellationToken ct) =>
        {
            var lockHandle = await importLockService.TryAcquireForShopAsync(shopId, ct).ConfigureAwait(false);
            if (lockHandle is null)
                return Results.Json(new { reason = "import_in_progress" }, statusCode: StatusCodes.Status423Locked);

            await using (lockHandle)
            {
                await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

                var importMode = ParseImportMode(mode);

                // dryRun (bool)
                bool isDryRun = false;
                if (!string.IsNullOrWhiteSpace(dryRun) && bool.TryParse(dryRun, out var b)) isDryRun = b;

                if (!isDryRun && importMode == CineBoutique.Inventory.Api.Models.ProductImportMode.ReplaceCatalogue)
                {
                    var hasCountLines = await HasCountLinesForShopAsync(shopId, connection, ct).ConfigureAwait(false);
                    if (hasCountLines)
                    {
                        return Results.Json(
                            new
                            {
                                reason = "catalog_locked",
                                message = "Impossible de remplacer le catalogue : des comptages contiennent déjà des produits issus de ce CSV. Importez un fichier complémentaire pour ajouter de nouvelles références."
                            },
                            statusCode: StatusCodes.Status423Locked);
                    }
                }

                // 25 MiB
                const long maxCsvSizeBytes = 25L * 1024L * 1024L;
                if (request.ContentLength is { } len && len > maxCsvSizeBytes)
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

                // Récupération du flux CSV
                System.IO.Stream csvStream;
                if (request.HasFormContentType)
                {
                    var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files.GetFile("file");
                    if (file is null || file.Length == 0) return Results.BadRequest(new { reason = "MISSING_FILE" });
                    if (file.Length > maxCsvSizeBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    csvStream = file.OpenReadStream();
                }
                else
                {
                    var contentType = (request.ContentType ?? string.Empty).Trim();
                    var looksCsv =
                        contentType.StartsWith("text/csv", System.StringComparison.OrdinalIgnoreCase) ||
                        contentType.StartsWith("application/octet-stream", System.StringComparison.OrdinalIgnoreCase) ||
                        contentType.StartsWith("application/vnd.ms-excel", System.StringComparison.OrdinalIgnoreCase) ||
                        contentType.StartsWith("text/plain", System.StringComparison.OrdinalIgnoreCase);

                    if (!looksCsv)
                        return Results.BadRequest(new { reason = "UNSUPPORTED_CONTENT_TYPE" });
                    csvStream = request.Body;
                }

                var user = EndpointUtilities.GetAuthenticatedUserName(request.HttpContext);
                var importService = request.HttpContext.RequestServices.GetRequiredService<CineBoutique.Inventory.Api.Services.Products.IProductImportService>();
                var cmd = new CineBoutique.Inventory.Api.Models.ProductImportCommand(csvStream, isDryRun, user, shopId, importMode);
                var result = await importService.ImportAsync(cmd, ct).ConfigureAwait(false);

                return result.ResultType switch
                {
                    CineBoutique.Inventory.Api.Models.ProductImportResultType.ValidationFailed => Results.BadRequest(result.Response),
                    CineBoutique.Inventory.Api.Models.ProductImportResultType.Skipped          => Results.StatusCode(StatusCodes.Status204NoContent),
                    _                                                                          => Results.Ok(result.Response)
                };
            }
        }).RequireAuthorization();

        app.MapGet("/api/shops/{shopId:guid}/products/import/status", async (
            System.Guid shopId,
            System.Data.IDbConnection connection,
            System.Threading.CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            var hasCountLines = await HasCountLinesForShopAsync(shopId, connection, ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                canReplace = !hasCountLines,
                lockReason = hasCountLines ? "counting_started" : null,
                hasCountLines
            });
        }).RequireAuthorization();
    }

    private static ProductImportMode ParseImportMode(string? mode)
    {
        if (string.Equals(mode, "merge", StringComparison.OrdinalIgnoreCase))
        {
            return ProductImportMode.Merge;
        }

        return ProductImportMode.ReplaceCatalogue;
    }

    private static async Task<bool> HasCountLinesForShopAsync(Guid shopId, IDbConnection connection, CancellationToken ct)
    {
        const string sql = @"SELECT EXISTS (
        SELECT 1
        FROM ""CountLine"" cl
        JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
        JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
        WHERE l.""ShopId"" = @ShopId
    );";

        try
        {
            return await connection.ExecuteScalarAsync<bool>(
                    new Dapper.CommandDefinition(sql, new { ShopId = shopId }, cancellationToken: ct))
                .ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return false;
        }
    }

}
