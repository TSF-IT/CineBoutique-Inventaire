using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Shops;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services.Products;
using CineBoutique.Inventory.Api.Validation;
using Dapper;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Npgsql;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace CineBoutique.Inventory.Api.Features.Products;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapProductCatalogEndpoints(this IEndpointRouteBuilder app, bool catalogEndpointsPublic)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapProductDetailsEndpoint(app);
        MapProductLookupEndpoint(app, catalogEndpointsPublic);
        MapProductSearchEndpoint(app, catalogEndpointsPublic);
        MapProductCountEndpoint(app, catalogEndpointsPublic);
        MapShopCatalogEndpoints(app, catalogEndpointsPublic);
        MapProductSuggestEndpoint(app, catalogEndpointsPublic);

        return app;
    }

    private static void MapProductDetailsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/{sku}/details", async (
            string sku,
            IDbConnection connection,
            CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);
            const string sql = @"
      SELECT p.""Sku"", p.""Ean"", p.""Name"",
             pg.""Label"" AS ""Group"", pgp.""Label"" AS ""SubGroup"",
             p.""Attributes""
      FROM ""Product"" p
      LEFT JOIN ""ProductGroup"" pg  ON pg.""Id""  = p.""GroupId""
      LEFT JOIN ""ProductGroup"" pgp ON pgp.""Id"" = pg.""ParentId""
      WHERE p.""Sku"" = @sku
      LIMIT 1;";
            var row = await connection.QueryFirstOrDefaultAsync(
              new CommandDefinition(sql, new { sku }, cancellationToken: ct)).ConfigureAwait(false);
            return row is null ? Results.NotFound() : Results.Ok(row);
        })
        .WithMetadata(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute());
    }

    private static void MapProductLookupEndpoint(IEndpointRouteBuilder app, bool catalogEndpointsPublic)
    {
        ApplyCatalogVisibility(app.MapGet("/api/products/{code}", async (
                string code,
                IProductLookupService lookupService,
                IAuditLogger auditLogger,
                IClock clock,
                HttpContext httpContext,
                ILogger<ProductLookupLogger> logger,
                IProductLookupMetrics lookupMetrics,
                CancellationToken cancellationToken) =>
            {
                var result = await lookupService.ResolveAsync(code, cancellationToken).ConfigureAwait(false);

                var now = clock.UtcNow;
                var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
                var actor = EndpointUtilities.FormatActorLabel(httpContext);
                var timestamp = EndpointUtilities.FormatTimestamp(now);
                var displayCode = string.IsNullOrWhiteSpace(result.NormalizedCode) ? "(vide)" : result.NormalizedCode;

                switch (result.Status)
                {
                    case ProductLookupStatus.Success:
                    {
                        var product = result.Product!;
                        var productLabel = product.Name;
                        var skuLabel = string.IsNullOrWhiteSpace(product.Sku) ? "non renseigné" : product.Sku;
                        var eanLabel = string.IsNullOrWhiteSpace(product.Ean) ? "non renseigné" : product.Ean;
                        var successMessage = $"{actor} a scanné le code {displayCode} et a identifié le produit \"{productLabel}\" (SKU {skuLabel}, EAN {eanLabel}) le {timestamp} UTC.";
                        await auditLogger.LogAsync(successMessage, userName, "products.scan.success", cancellationToken).ConfigureAwait(false);
                        return Results.Ok(product);
                    }

                    case ProductLookupStatus.Conflict:
                    {
                        var matches = result.Matches
                            .Select(match => new ProductLookupConflictMatch(match.Sku, match.Code))
                            .ToArray();

                        lookupMetrics.IncrementAmbiguity();
                        logger.LogInformation(
                            "ProductLookupAmbiguity {@Lookup}",
                            new
                            {
                                Code = result.OriginalCode,
                                Digits = result.Digits ?? string.Empty,
                                MatchCount = matches.Length
                            });

                        var conflictPayload = new ProductLookupConflictResponse(
                            Ambiguous: true,
                            Code: result.OriginalCode,
                            Digits: result.Digits ?? string.Empty,
                            Matches: matches);

                        var digitsLabel = string.IsNullOrEmpty(result.Digits) ? "(n/a)" : result.Digits;
                        var conflictMessage = $"{actor} a scanné le code {displayCode} mais plusieurs produits partagent les chiffres {digitsLabel} le {timestamp} UTC.";
                        await auditLogger.LogAsync(conflictMessage, userName, "products.scan.conflict", cancellationToken).ConfigureAwait(false);
                        return Results.Json(conflictPayload, statusCode: StatusCodes.Status409Conflict);
                    }

                    default:
                    {
                        var notFoundMessage = $"{actor} a scanné le code {displayCode} sans correspondance produit le {timestamp} UTC.";
                        await auditLogger.LogAsync(notFoundMessage, userName, "products.scan.not_found", cancellationToken).ConfigureAwait(false);
                        return Results.NotFound();
                    }
                }
            }), catalogEndpointsPublic)
            .WithName("GetProductByCode")
            .WithTags("Produits")
            .Produces<ProductDto>(StatusCodes.Status200OK)
            .Produces<ProductLookupConflictResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi(op =>
            {
                op.Summary = "Recherche un produit par code scanné (SKU, code brut, chiffres).";
                op.Description = "Résout d'abord par SKU exact, puis par code brut (EAN/Code) et enfin par chiffres extraits. Retourne 409 en cas de collisions sur CodeDigits.";
                return op;
            });
    }

    private static void MapProductSearchEndpoint(IEndpointRouteBuilder app, bool catalogEndpointsPublic)
    {
        ApplyCatalogVisibility(app.MapGet("/api/products/search", async (
                string? code,
                int? limit,
                int? page,
                int? pageSize,
                string? sort,
                string? dir,
                IProductSearchService searchService,
                CancellationToken cancellationToken) =>
            {
                var sanitizedCode = code?.Trim();
                if (string.IsNullOrWhiteSpace(sanitizedCode))
                {
                    var validation = new ValidationResult(new[]
                    {
                        new ValidationFailure("code", "Le paramètre 'code' est obligatoire.")
                    });

                    return EndpointUtilities.ValidationProblem(validation);
                }

                var hasPaging = page.HasValue || pageSize.HasValue;
                var p = Math.Max(1, page ?? 1);
                var ps = Math.Max(1, Math.Min(100, pageSize ?? 50));
                var offset = (p - 1) * ps;

                var effectiveLimit = limit.GetValueOrDefault(20);
                if (effectiveLimit < 1 || effectiveLimit > 50)
                {
                    var validation = new ValidationResult(new[]
                    {
                        new ValidationFailure("limit", "Le paramètre 'limit' doit être compris entre 1 et 50.")
                    });

                    return EndpointUtilities.ValidationProblem(validation);
                }

                var items = await searchService
                    .SearchAsync(sanitizedCode, effectiveLimit, hasPaging, ps, offset, sort, dir, cancellationToken)
                    .ConfigureAwait(false);

                var response = items
                    .Select(item => new ProductSearchItemDto(item.Sku, item.Code, item.Name, item.Group, item.SubGroup))
                    .ToArray();
                return Results.Ok(response);
            }), catalogEndpointsPublic)
            .WithName("SearchProducts")
            .WithTags("Produits")
            .Produces<IReadOnlyList<ProductSearchItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Recherche un ensemble de produits à partir d'un code scanné.";
                operation.Description = "La recherche combine une requête SQL unique mêlant préfixes sur le SKU/EAN et similarité trigram sur le nom et les groupes. Les résultats sont dédupliqués par SKU et limités par le paramètre 'limit'.";

                if (operation.Parameters is { Count: > 0 })
                {
                    foreach (var parameter in operation.Parameters)
                    {
                        if (string.Equals(parameter.Name, "code", StringComparison.OrdinalIgnoreCase))
                        {
                            parameter.Description = "Code recherché (SKU exact, EAN ou code brut).";
                            parameter.Required = true;
                        }
                        else if (string.Equals(parameter.Name, "limit", StringComparison.OrdinalIgnoreCase))
                        {
                            parameter.Description = "Nombre maximum de résultats (défaut : 20, maximum : 50).";
                            if (parameter.Schema is not null)
                            {
                                parameter.Schema.Minimum = 1;
                                parameter.Schema.Maximum = 50;
                                parameter.Schema.Default = new OpenApiInteger(20);
                            }
                        }
                    }
                }

                operation.Responses ??= new OpenApiResponses();
                operation.Responses[StatusCodes.Status200OK.ToString(CultureInfo.InvariantCulture)] = new OpenApiResponse
                {
                    Description = "Liste des produits correspondant au code recherché.",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "array",
                                Items = new OpenApiSchema
                                {
                                    Reference = new OpenApiReference
                                    {
                                        Type = ReferenceType.Schema,
                                        Id = nameof(ProductSearchItemDto)
                                    }
                                }
                            },
                            Example = new OpenApiArray
                            {
                                new OpenApiObject
                                {
                                    ["sku"] = new OpenApiString("CB-0001"),
                                    ["code"] = new OpenApiString("CB-0001"),
                                    ["name"] = new OpenApiString("Café grains 1kg")
                                },
                                new OpenApiObject
                                {
                                    ["sku"] = new OpenApiString("CB-0101"),
                                    ["code"] = new OpenApiString("0001"),
                                    ["name"] = new OpenApiString("Bonbon réglisse")
                                }
                            }
                        }
                    }
                };

                return operation;
            });
    }

    private static void MapProductCountEndpoint(IEndpointRouteBuilder app, bool catalogEndpointsPublic)
    {
        ApplyCatalogVisibility(app.MapGet("/api/products/count", async (
            IDbConnection connection,
            IShopResolver shopResolver,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            // Essayez d’abord de résoudre le shop back-compat
            Guid? shopId = null;
            try
            {
                shopId = await shopResolver.GetDefaultForBackCompatAsync(connection, ct).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // La table des shops n’est pas encore prête : on tombera en fallback global plus bas
            }
            catch
            {
                // Toute autre erreur de résolution -> fallback global
            }

            // 1) Si un ShopId est disponible, tenter COUNT(scopé)
            if (shopId.HasValue)
            {
                try
                {
                    const string scopedSql = @"SELECT COUNT(*) FROM ""Product"" WHERE ""ShopId"" = @ShopId;";
                    var totalScoped = await connection.ExecuteScalarAsync<long>(
                        new CommandDefinition(scopedSql, new { ShopId = shopId.Value }, cancellationToken: ct)
                    ).ConfigureAwait(false);

                    return Results.Ok(new { total = totalScoped });
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
                {
                    // Table Product absente -> 0
                    return Results.Ok(new { total = 0L });
                }
            }

            // 2) Fallback global : COUNT(*) sans filtre (si Product existe)
            try
            {
                const string globalSql = @"SELECT COUNT(*) FROM ""Product"";";
                var totalGlobal = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition(globalSql, cancellationToken: ct)
                ).ConfigureAwait(false);

                return Results.Ok(new { total = totalGlobal });
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // Table Product absente -> 0
                return Results.Ok(new { total = 0L });
            }
        }), catalogEndpointsPublic);
    }

    private static void MapShopCatalogEndpoints(IEndpointRouteBuilder app, bool catalogEndpointsPublic)
    {
        ApplyCatalogVisibility(app.MapGet("/api/shops/{shopId:guid}/products", async (
            Guid shopId,
            int? page,
            int? pageSize,
            string? q,
            string? sortBy,
            string? sortDir,
            IDbConnection connection,
            CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            var p  = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 50, 1, 200);
            var off = (p - 1) * ps;
            var filter = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var sort = (sortBy ?? "sku").Trim().ToLowerInvariant();
            sort = sort switch
            {
                "ean"    => "ean",
                "name"   => "name",
                "descr"  => "name",
                "digits" => "digits",
                _         => "sku"
            };
            var dir  = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

            const string whereClause = """
WHERE p."ShopId" = @ShopId AND (
    @Filter IS NULL OR @Filter='' OR
    COALESCE(p."Sku",'') ILIKE '%'||@Filter||'%' OR
    COALESCE(p."Ean",'') ILIKE '%'||@Filter||'%' OR
    COALESCE(p."CodeDigits",'') ILIKE '%'||@Filter||'%' OR
    COALESCE(p."Name",'') ILIKE '%'||@Filter||'%'
)
""";

            const string globalWhereClause = """
WHERE
    @Filter IS NULL OR @Filter='' OR
    COALESCE(p."Sku",'') ILIKE '%'||@Filter||'%' OR
    COALESCE(p."Ean",'') ILIKE '%'||@Filter||'%' OR
    COALESCE(p."CodeDigits",'') ILIKE '%'||@Filter||'%' OR
    COALESCE(p."Name",'') ILIKE '%'||@Filter||'%'
""";

            static IResult BuildPagedResult(
                long totalCount,
                IEnumerable<dynamic> rows,
                int pageNumber,
                int pageSizeValue,
                string sortField,
                string sortDirectionValue,
                string? filterValue)
            {
                var totalPagesValue = totalCount == 0
                    ? 0
                    : (int)Math.Ceiling(totalCount / (double)pageSizeValue);

                return Results.Ok(new
                {
                    items = rows,
                    page = pageNumber,
                    pageSize = pageSizeValue,
                    total = totalCount,
                    totalPages = totalPagesValue,
                    sortBy = sortField,
                    sortDir = sortDirectionValue,
                    q = filterValue
                });
            }

            try
            {
                var total = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition($$"""
SELECT COUNT(*)
FROM "Product" p
{{whereClause}};
""", new { ShopId = shopId, Filter = filter }, cancellationToken: ct)).ConfigureAwait(false);

                var data = await connection.QueryAsync(
                    new CommandDefinition($$"""
SELECT
  p."Id",
  p."Sku",
  p."Name",
  p."Ean",
  p."CodeDigits",
  COALESCE(pgp."Label", pg."Label", p."Attributes"->>'originalGroupe') AS "group",
  COALESCE(
    CASE WHEN pgp."Id" IS NULL THEN NULL ELSE pg."Label" END,
    p."Attributes"->>'originalSousGroupe'
  ) AS "subGroup"
FROM "Product" p
LEFT JOIN "ProductGroup" pg  ON pg."Id"  = p."GroupId"
LEFT JOIN "ProductGroup" pgp ON pgp."Id" = pg."ParentId"
{{whereClause}}
ORDER BY
  CASE WHEN @Sort='ean'   THEN p."Ean"
       WHEN @Sort='name'  THEN p."Name"
       WHEN @Sort='digits'THEN p."CodeDigits"
       ELSE p."Sku" END {{dir}},
  p."Sku" ASC
LIMIT @Limit OFFSET @Offset;
""", new { ShopId = shopId, Filter = filter, Sort = sort, Limit = ps, Offset = off }, cancellationToken: ct)).ConfigureAwait(false);

                return BuildPagedResult(total, data, p, ps, sort, dir, filter);
            }
            catch (PostgresException ex) when (IsShopScopeMissing(ex))
            {
                var total = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition($$"""
SELECT COUNT(*)
FROM "Product" p
{{globalWhereClause}};
""", new { Filter = filter }, cancellationToken: ct)).ConfigureAwait(false);

                var data = await connection.QueryAsync(
                    new CommandDefinition($$"""
SELECT
  p."Id",
  p."Sku",
  p."Name",
  p."Ean",
  p."CodeDigits",
  COALESCE(pgp."Label", pg."Label", p."Attributes"->>'originalGroupe') AS "group",
  COALESCE(
    CASE WHEN pgp."Id" IS NULL THEN NULL ELSE pg."Label" END,
    p."Attributes"->>'originalSousGroupe'
  ) AS "subGroup"
FROM "Product" p
LEFT JOIN "ProductGroup" pg  ON pg."Id"  = p."GroupId"
LEFT JOIN "ProductGroup" pgp ON pgp."Id" = pg."ParentId"
{{globalWhereClause}}
ORDER BY
  CASE WHEN @Sort='ean'   THEN p."Ean"
       WHEN @Sort='name'  THEN p."Name"
       WHEN @Sort='digits'THEN p."CodeDigits"
       ELSE p."Sku" END {{dir}},
  p."Sku" ASC
LIMIT @Limit OFFSET @Offset;
""", new { Filter = filter, Sort = sort, Limit = ps, Offset = off }, cancellationToken: ct)).ConfigureAwait(false);

                return BuildPagedResult(total, data, p, ps, sort, dir, filter);
            }
        }), catalogEndpointsPublic);

        ApplyCatalogVisibility(app.MapGet("/api/shops/{shopId:guid}/products/export", async (
            Guid shopId,
            IDbConnection connection,
            CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            const string sql = """
    SELECT "Sku","Ean","Name","CodeDigits"
    FROM "Product"
    WHERE "ShopId"=@ShopId
    ORDER BY "Sku";
    """;

            var rows = await connection.QueryAsync(
                new CommandDefinition(sql, new { ShopId = shopId }, cancellationToken: ct)
            ).ConfigureAwait(false);

            // Construire CSV ; séparateur ; ; latin1
            var sb = new StringBuilder();
            sb.AppendLine("sku;ean;name;codeDigits");
            foreach (dynamic r in rows)
            {
                string esc(string? s) => (s ?? string.Empty).Replace("\"", "\"\"");
                sb.Append('"').Append(esc((string)r.Sku)).Append('"').Append(';')
                  .Append('"').Append(esc((string?)r.Ean)).Append('"').Append(';')
                  .Append('"').Append(esc((string)r.Name)).Append('"').Append(';')
                  .Append('"').Append(esc((string?)r.CodeDigits)).Append('"')
                  .AppendLine();
            }

            var bytes = Encoding.Latin1.GetBytes(sb.ToString());
            return Results.File(bytes, "text/csv; charset=ISO-8859-1", $"products_{shopId}.csv");
        }), catalogEndpointsPublic);

        ApplyCatalogVisibility(app.MapGet("/api/shops/{shopId:guid}/products/count", async (
            Guid shopId,
            IDbConnection connection,
            CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            const string countSql = "SELECT COUNT(*) FROM \"Product\" WHERE \"ShopId\"=@ShopId;";
            var count = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(countSql, new { ShopId = shopId }, cancellationToken: ct)).ConfigureAwait(false);

            bool hasCatalog;
            try
            {
                const string histSql = "SELECT EXISTS (SELECT 1 FROM \"ProductImportHistory\" WHERE \"ShopId\"=@ShopId);";
                hasCatalog = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(histSql, new { ShopId = shopId }, cancellationToken: ct)).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                const string fallbackSql = "SELECT EXISTS (SELECT 1 FROM \"ProductImport\" WHERE \"ShopId\"=@ShopId);";
                hasCatalog = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(fallbackSql, new { ShopId = shopId }, cancellationToken: ct)).ConfigureAwait(false);
            }

            return Results.Ok(new { count, hasCatalog });
        }), catalogEndpointsPublic);
    }

    private static void MapProductSuggestEndpoint(IEndpointRouteBuilder app, bool catalogEndpointsPublic)
    {
        ApplyCatalogVisibility(app.MapGet("/api/products/suggest", async (
            string q,
            int? limit,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            q = (q ?? string.Empty).Trim();
            if (q.Length == 0) return Results.BadRequest("q is required");

            if (limit.HasValue && (limit.Value < 1 || limit.Value > 50))
                return Results.BadRequest("limit must be between 1 and 50");

            var top = Math.Clamp(limit ?? 8, 1, 50);

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            var qRaw = q;
            var qTight = Regex.Replace(qRaw, @"[\s\-_]", string.Empty);

            if (qTight.Length >= 8 && qTight.All(char.IsDigit))
            {
                const string fastSql = @"
        SELECT p.""Sku"", p.""Ean"", p.""Name"",
               COALESCE(pgp.""Label"", pg.""Label"", p.""Attributes""->>'originalGroupe') AS ""Group"",
               COALESCE(
                 CASE WHEN pgp.""Id"" IS NULL THEN NULL ELSE pg.""Label"" END,
                 p.""Attributes""->>'originalSousGroupe'
               ) AS ""SubGroup""
        FROM ""Product"" p
        LEFT JOIN ""ProductGroup"" pg  ON pg.""Id""  = p.""GroupId""
        LEFT JOIN ""ProductGroup"" pgp ON pgp.""Id"" = pg.""ParentId""
        WHERE
             p.""Ean"" = @qExact
          OR p.""Ean"" = @qTight
          OR p.""Ean"" LIKE @qTightPrefix
          OR p.""CodeDigits"" = @qExact
          OR p.""CodeDigits"" = @qTight
          OR p.""CodeDigits"" LIKE @qTightPrefix
        ORDER BY
          CASE
            WHEN p.""Ean"" = @qExact OR p.""CodeDigits"" = @qExact THEN 3
            WHEN p.""Ean"" = @qTight OR p.""CodeDigits"" = @qTight THEN 2
            WHEN p.""Ean"" LIKE @qTightPrefix OR p.""CodeDigits"" LIKE @qTightPrefix THEN 1
            ELSE 0
          END DESC,
          p.""Sku""
        LIMIT @top;";

                var fastRows = await connection.QueryAsync<ProductSuggestionDto>(
                    new CommandDefinition(
                        fastSql,
                        new { qExact = qRaw, qTight, qTightPrefix = qTight + "%", top },
                        cancellationToken: cancellationToken
                    )
                ).ConfigureAwait(false);

                if (fastRows.Any())
                    return Results.Ok(fastRows);
            }

            var sql = @"
WITH cand AS (
  SELECT
    p.""Sku"",
    p.""Ean"",
    p.""Name"",
    COALESCE(pgp.""Label"", pg.""Label"", p.""Attributes""->>'originalGroupe') AS ""Group"",
    COALESCE(
      CASE WHEN pgp.""Id"" IS NULL THEN NULL ELSE pg.""Label"" END,
      p.""Attributes""->>'originalSousGroupe'
    ) AS ""SubGroup"",
    CASE WHEN LOWER(p.""Sku"") LIKE LOWER(@q) || '%' THEN 1 ELSE 0 END AS sku_pref,
    CASE WHEN LOWER(p.""Ean"") LIKE LOWER(@q) || '%' THEN 1 ELSE 0 END AS ean_pref,
    similarity(immutable_unaccent(LOWER(p.""Name"")), immutable_unaccent(LOWER(@q))) AS name_sim,
    CASE 
      WHEN pg.""Id"" IS NOT NULL 
        AND immutable_unaccent(LOWER(pg.""Label"")) LIKE immutable_unaccent(LOWER(@q)) || '%' 
      THEN 1 ELSE 0 END AS sub_pref,
    CASE 
      WHEN pgp.""Id"" IS NOT NULL 
        AND immutable_unaccent(LOWER(pgp.""Label"")) LIKE immutable_unaccent(LOWER(@q)) || '%' 
      THEN 1 ELSE 0 END AS grp_pref,
    COALESCE(similarity(immutable_unaccent(LOWER(pg.""Label"")),  immutable_unaccent(LOWER(@q))), 0) AS sub_sim,
    COALESCE(similarity(immutable_unaccent(LOWER(pgp.""Label"")), immutable_unaccent(LOWER(@q))), 0) AS grp_sim
  FROM ""Product"" p
  LEFT JOIN ""ProductGroup"" pg  ON pg.""Id""  = p.""GroupId""
  LEFT JOIN ""ProductGroup"" pgp ON pgp.""Id"" = pg.""ParentId""
  WHERE
    LOWER(p.""Sku"") LIKE LOWER(@q) || '%'
    OR LOWER(p.""Ean"") LIKE LOWER(@q) || '%'
    OR similarity(immutable_unaccent(LOWER(p.""Name"")), immutable_unaccent(LOWER(@q))) > 0.20
    OR (pg.""Id""  IS NOT NULL AND (
          similarity(immutable_unaccent(LOWER(pg.""Label"")),  immutable_unaccent(LOWER(@q))) > 0.20
       OR immutable_unaccent(LOWER(pg.""Label""))  LIKE immutable_unaccent(LOWER(@q)) || '%'
    ))
    OR (pgp.""Id"" IS NOT NULL AND (
          similarity(immutable_unaccent(LOWER(pgp.""Label"")), immutable_unaccent(LOWER(@q))) > 0.20
       OR immutable_unaccent(LOWER(pgp.""Label"")) LIKE immutable_unaccent(LOWER(@q)) || '%'
    ))
)
, best_per_sku AS (
  SELECT DISTINCT ON (c.""Sku"") c.*
  FROM cand c
  ORDER BY 
    c.""Sku"",
    c.sku_pref DESC,
    c.ean_pref DESC,
    c.name_sim DESC,
    GREATEST(c.sub_pref, c.grp_pref) DESC,
    GREATEST(c.sub_sim,  c.grp_sim)  DESC
)
SELECT ""Sku"",""Ean"",""Name"",""Group"",""SubGroup""
FROM best_per_sku
ORDER BY
  sku_pref DESC,
  ean_pref DESC,
  name_sim DESC,
  GREATEST(sub_pref, grp_pref) DESC,
  GREATEST(sub_sim,  grp_sim)  DESC,
  ""Sku""
LIMIT @top;";

            var suggestions = await connection.QueryAsync<ProductSuggestionDto>(
                new CommandDefinition(sql, new { q, top }, cancellationToken: cancellationToken)
            ).ConfigureAwait(false);

            return Results.Ok(suggestions);
        }), catalogEndpointsPublic)
        .WithName("SuggestProducts")
        .WithTags("Produits")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }

    private static RouteHandlerBuilder ApplyCatalogVisibility(RouteHandlerBuilder builder, bool catalogEndpointsPublic)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (catalogEndpointsPublic)
        {
            return builder.AllowAnonymous();
        }

        return builder.RequireAuthorization();
    }

    private sealed class ProductLookupLogger
    {
    }

    private static bool IsShopScopeMissing(PostgresException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex.SqlState is PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedTable;
    }
}
