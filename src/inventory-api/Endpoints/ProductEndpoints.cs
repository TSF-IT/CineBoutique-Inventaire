// Modifications : déplacement des endpoints produits depuis Program.cs avec mutualisation des helpers locaux.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CineBoutique.Inventory.Api.Configuration;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services.Products;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper; // Requis pour CommandDefinition et les extensions Dapper.
using FluentValidation.Results;
using CsvHelper;
using CsvHelper.Configuration;
using Npgsql; // Requis pour PostgresException et PostgresErrorCodes dans les endpoints.
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class ProductEndpoints
{
    private const string LowerSkuConstraintName = "UX_Product_LowerSku";
    private const string EanNotNullConstraintName = "UX_Product_Ean_NotNull";
    private sealed class ProductLookupLogger
    {
    }

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var appSettings = app.ServiceProvider.GetRequiredService<IOptions<AppSettingsOptions>>().Value;
        var catalogEndpointsPublic = appSettings.CatalogEndpointsPublic;

        MapCreateProductEndpoint(app);
        MapSearchProductsEndpoint(app);
        MapGetProductEndpoint(app);
        MapUpdateProductEndpoints(app);
        MapImportProductsEndpoint(app);

        app.MapGet("/api/products/{sku}/details", async (
            string sku,
            System.Data.IDbConnection connection,
            System.Threading.CancellationToken ct) =>
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
              new Dapper.CommandDefinition(sql, new { sku }, cancellationToken: ct)).ConfigureAwait(false);
            return row is null ? Results.NotFound() : Results.Ok(row);
        })
        .WithMetadata(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute());

        ApplyCatalogVisibility(app.MapGet("/api/products/count", async (
            System.Data.IDbConnection connection,
            CineBoutique.Inventory.Api.Infrastructure.Shops.IShopResolver shopResolver,
            Microsoft.AspNetCore.Http.HttpContext httpContext,
            System.Threading.CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            // Essayez d’abord de résoudre le shop back-compat
            System.Guid? shopId = null;
            try
            {
                shopId = await shopResolver.GetDefaultForBackCompatAsync(connection, ct).ConfigureAwait(false);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
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
                        new Dapper.CommandDefinition(scopedSql, new { ShopId = shopId.Value }, cancellationToken: ct)
                    ).ConfigureAwait(false);

                    return Results.Ok(new { total = totalScoped });
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
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
                    new Dapper.CommandDefinition(globalSql, cancellationToken: ct)
                ).ConfigureAwait(false);

                return Results.Ok(new { total = totalGlobal });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
            {
                // Table Product absente -> 0
                return Results.Ok(new { total = 0L });
            }
        }), catalogEndpointsPublic);

        MapShopScopedProductEndpoints(app, catalogEndpointsPublic);

        ApplyCatalogVisibility(app.MapGet("/api/products/suggest", async (
            string q,
            int? limit,
            System.Data.IDbConnection connection,
            System.Threading.CancellationToken cancellationToken) =>
        {
            q = (q ?? string.Empty).Trim();
            if (q.Length == 0) return Results.BadRequest("q is required");

            // ✅ 400 si limit hors plage, pas de clamp silencieux
            if (limit.HasValue && (limit.Value < 1 || limit.Value > 50))
                return Results.BadRequest("limit must be between 1 and 50");

            var top = Math.Clamp(limit ?? 8, 1, 50);

            // Ouvrir la connexion AVANT toute requête
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken)
                .ConfigureAwait(false);

            // 🔎 Fast-path EAN/RFID : supprimer séparateurs, >=8 -> exact/prefix, trié en premier
            var qRaw = q;
            var qTight = Regex.Replace(qRaw, @"[\s\-_]", string.Empty);

            if (qTight.Length >= 8 && qTight.All(char.IsDigit))
            {
                const string fastSql = @"
        SELECT p.""Sku"", p.""Ean"", p.""Name"",
               COALESCE(pgp.""Label"", pg.""Label"") AS ""Group"",
               CASE WHEN pgp.""Id"" IS NULL THEN NULL ELSE pg.""Label"" END AS ""SubGroup""
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

                var fastRows = await connection.QueryAsync<CineBoutique.Inventory.Api.Models.ProductSuggestionDto>(
                    new Dapper.CommandDefinition(
                        fastSql,
                        new { qExact = qRaw, qTight, qTightPrefix = qTight + "%", top },
                        cancellationToken: cancellationToken
                    )
                ).ConfigureAwait(false);

                if (fastRows.Any())
                    return Results.Ok(fastRows);
            }

            // 🧠 Fallback texte : accent-insensible + ranking par similarité, dédoublage par SKU
            var sql = @"
WITH cand AS (
  SELECT
    p.""Sku"",
    p.""Ean"",
    p.""Name"",
    COALESCE(pgp.""Label"", pg.""Label"") AS ""Group"",
    CASE WHEN pgp.""Id"" IS NULL THEN NULL ELSE pg.""Label"" END AS ""SubGroup"",
    /* Flags pour tri déterministe */
    CASE WHEN LOWER(p.""Sku"") LIKE LOWER(@q) || '%' THEN 1 ELSE 0 END AS sku_pref,
    CASE WHEN LOWER(p.""Ean"") LIKE LOWER(@q) || '%' THEN 1 ELSE 0 END AS ean_pref,
    /* Similarité accent-insensible */
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
  /* Choisir la meilleure ligne par SKU selon la même priorité */
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

            var suggestions = await connection.QueryAsync<CineBoutique.Inventory.Api.Models.ProductSuggestionDto>(
                new Dapper.CommandDefinition(sql, new { q, top }, cancellationToken: cancellationToken)
            ).ConfigureAwait(false);

            return Results.Ok(suggestions);
        }), catalogEndpointsPublic)
        .WithName("SuggestProducts")
        .WithTags("Produits")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static void MapUpdateProductEndpoints(IEndpointRouteBuilder app)
    {
        // --- MAJ par SKU ---

        var updateBySku = async (
            string code,
            CreateProductRequest request,
            IDbConnection connection,
            IAuditLogger auditLogger,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var sku = code;
            if (string.IsNullOrWhiteSpace(sku))
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, "(SKU vide)", "sans sku", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le SKU (dans l'URL) est requis." });
            }

            if (request is null)
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sku, "corps null", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le corps de la requête est requis." });
            }

            var sanitizedSku = sku.Trim();
            var sanitizedName = request.Name?.Trim();
            var sanitizedEan = string.IsNullOrWhiteSpace(request.Ean) ? null : request.Ean.Trim();
            var sanitizedCodeDigits = CodeDigitsSanitizer.Build(sanitizedEan);

            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sanitizedSku, "sans nom", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit est requis." });
            }

            if (sanitizedName.Length > 256)
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sanitizedSku, "nom trop long", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit ne peut pas dépasser 256 caractères." });
            }

            if (sanitizedEan is { Length: > 0 } && (sanitizedEan.Length is < 8 or > 13 || !sanitizedEan.All(char.IsDigit)))
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sanitizedSku, "EAN invalide", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "L'EAN doit contenir entre 8 et 13 chiffres." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            // Récupère le produit par SKU (case-insensitive)
            const string fetchSql = @"SELECT ""Id"", ""Sku"", ""Name"", ""Ean""
                                  FROM ""Product""
                                  WHERE LOWER(""Sku"") = LOWER(@Sku)
                                  LIMIT 1;";
            var existing = await connection.QueryFirstOrDefaultAsync<ProductDto>(
                new CommandDefinition(fetchSql, new { Sku = sanitizedSku }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (existing is null)
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sanitizedSku, "inexistant", "products.update.notfound", cancellationToken).ConfigureAwait(false);
                return Results.NotFound(new { message = $"Aucun produit avec le SKU '{sanitizedSku}'." });
            }

            const string updateSql = @"UPDATE ""Product""
                                   SET ""Name"" = @Name, ""Ean"" = @Ean, ""CodeDigits"" = @CodeDigits
                                   WHERE ""Id"" = @Id
                                   RETURNING ""Id"", ""Sku"", ""Name"", ""Ean"";";
            try
            {
                var updated = await connection.QuerySingleAsync<ProductDto>(
                    new CommandDefinition(updateSql, new { Id = existing.Id, Name = sanitizedName, Ean = sanitizedEan, CodeDigits = sanitizedCodeDigits }, cancellationToken: cancellationToken)).ConfigureAwait(false);

                return Results.Ok(updated);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                if (string.Equals(ex.ConstraintName, EanNotNullConstraintName, StringComparison.Ordinal))
                {
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sanitizedSku, $"EAN déjà utilisé ({sanitizedEan})", "products.update.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Cet EAN est déjà utilisé." });
                }

                if (string.Equals(ex.ConstraintName, LowerSkuConstraintName, StringComparison.Ordinal))
                {
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sanitizedSku, "SKU déjà utilisé", "products.update.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Ce SKU est déjà utilisé." });
                }

                throw;
            }
        };

        app.MapPost("/api/products/{code}", updateBySku)
           .WithName("UpdateProductBySkuPost")
           .WithTags("Produits")
           .Produces<ProductDto>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict)
           .WithOpenApi(op =>
           {
               op.Summary = "Met à jour un produit par SKU (POST compat).";
               op.Description = "Modifie le nom et/ou l'EAN du produit identifié par son SKU.";
               return op;
           });

        app.MapPut("/api/products/{code}", updateBySku)
           .WithName("UpdateProductBySku")
           .WithTags("Produits")
           .Produces<ProductDto>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);

        // --- MAJ par Id (GUID) ---

        var updateById = async (
            Guid id,
            CreateProductRequest request,
            IDbConnection connection,
            IAuditLogger auditLogger,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), "corps null", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le corps de la requête est requis." });
            }

            var sanitizedName = request.Name?.Trim();
            var sanitizedEan = string.IsNullOrWhiteSpace(request.Ean) ? null : request.Ean.Trim();
            var sanitizedCodeDigits = CodeDigitsSanitizer.Build(sanitizedEan);

            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), "sans nom", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit est requis." });
            }

            if (sanitizedName.Length > 256)
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), "nom trop long", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit ne peut pas dépasser 256 caractères." });
            }

            if (sanitizedEan is { Length: > 0 } && (sanitizedEan.Length is < 8 or > 13 || !sanitizedEan.All(char.IsDigit)))
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), "EAN invalide", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "L'EAN doit contenir entre 8 et 13 chiffres." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            // Vérifie l'existence
            const string existsSql = @"SELECT ""Id"", ""Sku"", ""Name"", ""Ean""
                                   FROM ""Product"" WHERE ""Id"" = @Id LIMIT 1;";
            var existing = await connection.QueryFirstOrDefaultAsync<ProductDto>(
                new CommandDefinition(existsSql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (existing is null)
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), "inexistant", "products.update.notfound", cancellationToken).ConfigureAwait(false);
                return Results.NotFound(new { message = $"Aucun produit avec l'Id '{id.ToString("D")}'." });
            }

            const string updateSql = @"UPDATE ""Product""
                                   SET ""Name"" = @Name, ""Ean"" = @Ean, ""CodeDigits"" = @CodeDigits
                                   WHERE ""Id"" = @Id
                                   RETURNING ""Id"", ""Sku"", ""Name"", ""Ean"";";
            try
            {
                var updated = await connection.QuerySingleAsync<ProductDto>(
                    new CommandDefinition(updateSql, new { Id = id, Name = sanitizedName, Ean = sanitizedEan, CodeDigits = sanitizedCodeDigits }, cancellationToken: cancellationToken)).ConfigureAwait(false);

                return Results.Ok(updated);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                if (string.Equals(ex.ConstraintName, EanNotNullConstraintName, StringComparison.Ordinal))
                {
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), $"EAN déjà utilisé ({sanitizedEan})", "products.update.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Cet EAN est déjà utilisé." });
                }

                if (string.Equals(ex.ConstraintName, LowerSkuConstraintName, StringComparison.Ordinal))
                {
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), "SKU déjà utilisé", "products.update.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Ce SKU est déjà utilisé." });
                }

                throw;
            }
        };

        app.MapPost("/api/products/by-id/{id:guid}", updateById)
           .WithName("UpdateProductByIdPost")
           .WithTags("Produits")
           .Produces<ProductDto>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);

        app.MapPut("/api/products/by-id/{id:guid}", updateById)
           .WithName("UpdateProductById")
           .WithTags("Produits")
           .Produces<ProductDto>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }


    private static void MapCreateProductEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/products", async (
            CreateProductRequest request,
            IDbConnection connection,
            IAuditLogger auditLogger,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { message = "Le corps de la requête est requis." });
            }

            var sanitizedSku = request.Sku?.Trim();
            var sanitizedName = request.Name?.Trim();
            var sanitizedEan = string.IsNullOrWhiteSpace(request.Ean) ? null : request.Ean.Trim();
            var sanitizedCodeDigits = CodeDigitsSanitizer.Build(sanitizedEan);

            if (string.IsNullOrWhiteSpace(sanitizedSku))
            {
                await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, "sans SKU", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le SKU est requis." });
            }

            if (sanitizedSku.Length > 32)
            {
                await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, "avec un SKU trop long", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le SKU ne peut pas dépasser 32 caractères." });
            }

            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, "sans nom", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit est requis." });
            }

            if (sanitizedName.Length > 256)
            {
                await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, "avec un nom trop long", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit ne peut pas dépasser 256 caractères." });
            }

            if (sanitizedEan is { Length: > 0 } && (sanitizedEan.Length is < 8 or > 13 || !sanitizedEan.All(char.IsDigit)))
            {
                await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, "avec un EAN invalide", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "L'EAN doit contenir entre 8 et 13 chiffres." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            const string insertSql = @"INSERT INTO ""Product"" (""Id"", ""Sku"", ""Name"", ""Ean"", ""CodeDigits"", ""CreatedAtUtc"")
VALUES (@Id, @Sku, @Name, @Ean, @CodeDigits, @CreatedAtUtc)
ON CONFLICT (LOWER(""Sku"")) DO NOTHING
RETURNING ""Id"", ""Sku"", ""Name"", ""Ean"";";

            var now = clock.UtcNow;
            try
            {
                var createdProduct = await connection.QuerySingleOrDefaultAsync<ProductDto>(
                    new CommandDefinition(
                        insertSql,
                        new
                        {
                            Id = Guid.NewGuid(),
                            Sku = sanitizedSku,
                            Name = sanitizedName,
                            Ean = sanitizedEan,
                            CodeDigits = sanitizedCodeDigits,
                            CreatedAtUtc = now
                        },
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (createdProduct is null)
                {
                    await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, $"avec un SKU déjà utilisé ({sanitizedSku})", "products.create.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Ce SKU est déjà utilisé." });
                }

                var location = $"/api/products/{Uri.EscapeDataString(createdProduct.Sku)}";
                return Results.Created(location, createdProduct);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                if (string.Equals(ex.ConstraintName, EanNotNullConstraintName, StringComparison.Ordinal))
                {
                    var eanLabel = string.IsNullOrWhiteSpace(sanitizedEan) ? "(EAN non renseigné)" : sanitizedEan;
                    await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, $"avec un EAN déjà utilisé ({eanLabel})", "products.create.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Cet EAN est déjà utilisé." });
                }

                if (string.Equals(ex.ConstraintName, LowerSkuConstraintName, StringComparison.Ordinal))
                {
                    await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, $"avec un SKU déjà utilisé ({sanitizedSku})", "products.create.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Ce SKU est déjà utilisé." });
                }

                throw;
            }
        })
        .WithName("CreateProduct")
        .WithTags("Produits")
        .Produces<ProductDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .WithOpenApi(op =>
        {
            op.Summary = "Crée un nouveau produit.";
            op.Description = "Permet l'ajout manuel d'un produit en spécifiant son SKU, son nom et éventuellement un code EAN.";
            return op;
        });
    }

    private static void MapImportProductsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/products/import", async (
                Microsoft.AspNetCore.Http.HttpRequest request,
                System.Data.IDbConnection connection,
                string? dryRun,
                System.Threading.CancellationToken cancellationToken) =>
            {
                // Logger local via DI (pas d’injection en signature)
                var services = request.HttpContext.RequestServices;
                var loggerFactory = (Microsoft.Extensions.Logging.ILoggerFactory)
                    services.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory))!;
                var log = loggerFactory?.CreateLogger("InventoryApi.ProductImport")
                          ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

                // Parsing robuste du paramètre dryRun
                bool isDryRun;
                if (string.IsNullOrWhiteSpace(dryRun))
                {
                    isDryRun = false;
                }
                else if (bool.TryParse(dryRun, out var b))
                {
                    isDryRun = b;
                }
                else
                {
                    // ✨ Contrat attendu par les tests : enveloppe Errors[] + UnknownColumns[]
                    return Results.Json(new
                    {
                        Errors = new[]
                        {
                            new {
                                Reason  = "INVALID_DRY_RUN",
                                Message = $"'{dryRun}' is not a valid boolean (expected 'true' or 'false').",
                                Field   = "dryRun"
                            }
                        },
                        UnknownColumns = Array.Empty<string>()
                    }, statusCode: 400);
                }

                await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken)
                    .ConfigureAwait(false);

                var httpContext = request.HttpContext;

                if (httpContext is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
                }

                var importService = httpContext.RequestServices.GetRequiredService<IProductImportService>();
                const long maxCsvSizeBytes = 25L * 1024L * 1024L;

                if (request.ContentLength is { } contentLength && contentLength > maxCsvSizeBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                static IResult BuildFailure(string reason) =>
                    Results.BadRequest(ProductImportResponse.Failure(
                        0,
                        new[]
                        {
                            new ProductImportError(0, reason)
                        },
                        ImmutableArray<string>.Empty,
                        ImmutableArray<ProductImportGroupProposal>.Empty));

                Stream? ownedStream = null;
                Stream? nonDryBufferedStream = null;
                Stream streamToImport = request.Body;

                try
                {
                    if (request.HasFormContentType)
                    {
                        var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
                        var file = form.Files.GetFile("file");

                        if (file is null || file.Length == 0)
                        {
                            return BuildFailure("MISSING_FILE");
                        }

                        if (file.Length > maxCsvSizeBytes)
                        {
                            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                        }

                        ownedStream = file.OpenReadStream();
                        streamToImport = ownedStream;
                    }
                    else if (!IsCsvContentType(request.ContentType))
                    {
                        return BuildFailure("UNSUPPORTED_CONTENT_TYPE");
                    }
                }
                catch (IOException)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
                catch (InvalidDataException)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                var username = EndpointUtilities.GetAuthenticatedUserName(httpContext);

                try
                {
                    // Map d'équivalences d'entêtes → canonique (insensible à la casse)
                    var synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["barcode_rfid"] = "ean",
                        ["ean13"] = "ean",
                        ["item"] = "sku",
                        ["code"] = "sku",
                        ["descr"] = "name",
                        ["description"] = "name",
                        ["sous_groupe"] = "sousGroupe",
                        ["subgroup"] = "sousGroupe",
                        ["groupe"] = "groupe"
                    };

                    // Colonnes "métier" reconnues (jeu canonique après normalisation)
                    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                      { "sku", "ean", "name", "groupe", "sousGroupe" };

                    // En non-dry, on s'assure que l'entête est bien lue
                    if (!isDryRun)
                    {
                        Stream headerStream = streamToImport;
                        MemoryStream? bufferedNonDryStream = null;
                        if (!headerStream.CanSeek)
                        {
                            bufferedNonDryStream = new MemoryStream();
                            await headerStream.CopyToAsync(bufferedNonDryStream, cancellationToken).ConfigureAwait(false);
                            bufferedNonDryStream.Position = 0;
                            streamToImport = bufferedNonDryStream;
                            headerStream = bufferedNonDryStream;

                            if (ownedStream is null)
                            {
                                ownedStream = bufferedNonDryStream;
                            }
                            else
                            {
                                nonDryBufferedStream = bufferedNonDryStream;
                            }
                        }
                        else
                        {
                            headerStream.Seek(0, SeekOrigin.Begin);
                        }

                        using (var headerReader = new StreamReader(headerStream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
                        {
                            var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
                            {
                                Delimiter = ";",
                                BadDataFound = null,
                                MissingFieldFound = null,
                                PrepareHeaderForMatch = args =>
                                {
                                    var k = (args.Header ?? string.Empty).Trim();
                                    return synonyms.TryGetValue(k, out var mapped) ? mapped : k;
                                }
                            };

                            using var csv = new CsvReader(headerReader, csvConfiguration);

                            try
                            {
                                if (csv.Context.Reader.HeaderRecord == null || csv.Context.Reader.HeaderRecord.Length == 0)
                                {
                                    if (await csv.ReadAsync().ConfigureAwait(false))
                                    {
                                        csv.ReadHeader();
                                    }
                                }
                            }
                            catch
                            {
                                // Tolérance : si l'entête est déjà chargée, on ignore.
                            }
                        }

                        if (streamToImport.CanSeek)
                        {
                            streamToImport.Seek(0, SeekOrigin.Begin);
                        }
                    }

                    if (isDryRun)
                    {
                        MemoryStream? bufferedStream = null;
                        Stream inspectionStream;
                        if (streamToImport.CanSeek)
                        {
                            streamToImport.Seek(0, SeekOrigin.Begin);
                            inspectionStream = streamToImport;
                        }
                        else
                        {
                            bufferedStream = new MemoryStream();
                            await streamToImport.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
                            bufferedStream.Position = 0;
                            inspectionStream = bufferedStream;
                        }

                        try
                        {
                            using (var inspectionReader = new StreamReader(inspectionStream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
                            {
                                var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
                                {
                                    Delimiter = ";",
                                    BadDataFound = null,
                                    MissingFieldFound = null,
                                    PrepareHeaderForMatch = args =>
                                    {
                                        var k = (args.Header ?? string.Empty).Trim();
                                        return synonyms.TryGetValue(k, out var mapped) ? mapped : k;
                                    }
                                };

                                using var csv = new CsvReader(inspectionReader, csvConfiguration);

                                var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                // Assure le chargement de l'entête si besoin
                                // (selon la config CsvHelper, ReadHeader peut être nécessaire)
                                try
                                {
                                    // Si la lib/ton code lit déjà l'entête en amont, ce ReadHeader sera no-op
                                    if (await csv.ReadAsync().ConfigureAwait(false))
                                    {
                                        csv.ReadHeader();
                                    }
                                }
                                catch
                                {
                                    /* on tolère l'absence d'entête */
                                }

                                var header = csv.Context.Reader.HeaderRecord;
                                if (header is { Length: > 0 })
                                {
                                    foreach (var raw in header)
                                    {
                                        if (string.IsNullOrWhiteSpace(raw)) continue;
                                        var k = synonyms.TryGetValue(raw, out var mapped) ? mapped : raw;
                                        if (!known.Contains(k)) unknown.Add(k);
                                    }
                                }

                                inspectionReader.DiscardBufferedData();
                                inspectionStream.Seek(0, SeekOrigin.Begin);

                                var dryRunCommand = new ProductImportCommand(inspectionStream, isDryRun, username);
                                var dryRunResult = await importService.ImportAsync(dryRunCommand, cancellationToken).ConfigureAwait(false);
                                unknown.UnionWith(dryRunResult.Response.UnknownColumns);

                                var response = new
                                {
                                    dryRunResult.Response.Total,
                                    dryRunResult.Response.Inserted,
                                    dryRunResult.Response.Updated,
                                    dryRunResult.Response.WouldInsert,
                                    dryRunResult.Response.ErrorCount,
                                    dryRunResult.Response.DryRun,
                                    dryRunResult.Response.Skipped,
                                    dryRunResult.Response.Errors,
                                    dryRunResult.Response.ProposedGroups,
                                    // conserve tes champs de preview existants si tu en as (compteurs, etc.)
                                    unknownColumns = unknown.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray()
                                };

                                return Results.Ok(response);
                            }
                        }
                        finally
                        {
                            if (bufferedStream is not null)
                            {
                                await bufferedStream.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                    }

                    var unknownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var command = new ProductImportCommand(streamToImport, isDryRun, username);
                    var result = await importService.ImportAsync(command, cancellationToken).ConfigureAwait(false);
                    unknownColumns.UnionWith(result.Response.UnknownColumns);

                    return result.ResultType switch
                    {
                        ProductImportResultType.ValidationFailed => Results.BadRequest(result.Response),
                        ProductImportResultType.Skipped => Results.Json(
                            result.Response,
                            statusCode: StatusCodes.Status204NoContent),
                        _ => BuildImportSuccessResult(result.Response, isDryRun, unknownColumns)
                    };
                }
                catch (ProductImportInProgressException)
                {
                    return Results.Json(new { reason = "import_in_progress" }, statusCode: StatusCodes.Status423Locked);
                }
                catch (ProductImportPayloadTooLargeException)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
                finally
                {
                    if (ownedStream is not null)
                    {
                        await ownedStream.DisposeAsync().ConfigureAwait(false);
                    }

                    if (nonDryBufferedStream is not null && !ReferenceEquals(nonDryBufferedStream, ownedStream))
                    {
                        await nonDryBufferedStream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            })
           .RequireAuthorization();
    }

    private static IResult BuildImportSuccessResult(
        ProductImportResponse response,
        bool isDryRun,
        HashSet<string> unknown)
    {
        if (!isDryRun)
        {
            return Results.Ok(response);
        }

        var orderedUnknown = unknown
            .OrderBy(static s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var payload = new
        {
            response.Total,
            response.Inserted,
            response.Updated,
            response.WouldInsert,
            response.ErrorCount,
            response.DryRun,
            response.Skipped,
            response.Errors,
            response.UnknownColumns,
            response.ProposedGroups,
            unknownColumns = orderedUnknown
        };

        return Results.Ok(payload);
    }

    private static void MapSearchProductsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/search", async (
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
                .Select(item => new ProductSearchItemDto(item.Sku, item.Code, item.Name))
                .ToArray();

            return Results.Ok(response);
        })
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

    private static void MapGetProductEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/{code}", async (
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
        })
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

    private static bool IsCsvContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var separatorIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = separatorIndex >= 0 ? contentType[..separatorIndex] : contentType;
        var trimmedMediaType = mediaType.Trim();

        var csvIndex = trimmedMediaType.IndexOf('c', StringComparison.OrdinalIgnoreCase);
        if (csvIndex < 0)
        {
            return false;
        }

        var prefix = trimmedMediaType[..csvIndex];
        var suffix = trimmedMediaType[csvIndex..];

        return string.Equals(prefix, "text/", StringComparison.OrdinalIgnoreCase)
            && string.Equals(suffix, "csv", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task LogProductCreationAttemptAsync(
        IClock clock,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        string details,
        string category,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
        var actor = EndpointUtilities.FormatActorLabel(httpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(now);
        var message = $"{actor} a tenté de créer un produit {details} le {timestamp} UTC.";
        await auditLogger.LogAsync(message, userName, category, cancellationToken).ConfigureAwait(false);
    }

    private static async Task LogProductUpdateAttemptAsync(
        IClock clock,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        string target,          // SKU ou Id
        string details,         // ex: "sans nom", "EAN invalide", "inexistant", ...
        string category,        // ex: "products.update.invalid"
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
        var actor = EndpointUtilities.FormatActorLabel(httpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(now);
        var message = $"{actor} a tenté de mettre à jour le produit '{target}' {details} le {timestamp} UTC.";
        await auditLogger.LogAsync(message, userName, category, cancellationToken).ConfigureAwait(false);
    }

    private static RouteHandlerBuilder ApplyCatalogVisibility(RouteHandlerBuilder builder, bool catalogEndpointsPublic)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return catalogEndpointsPublic
            ? builder.AllowAnonymous()
            : builder.RequireAuthorization();
    }

    private static void MapShopScopedProductEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool catalogEndpointsPublic)
    {
        // IMPORT CSV PAR BOUTIQUE (écrase ou merge selon service d'import)
        app.MapPost("/api/shops/{shopId:guid}/products/import", async (
            System.Guid shopId,
            Microsoft.AspNetCore.Http.HttpRequest request,
            System.Data.IDbConnection connection,
            CineBoutique.Inventory.Infrastructure.Locks.IImportLockService importLockService,
            string? dryRun,
            System.Threading.CancellationToken ct) =>
        {
            var lockHandle = await importLockService.TryAcquireForShopAsync(shopId, ct).ConfigureAwait(false);
            if (lockHandle is null)
                return Results.Json(new { reason = "import_in_progress" }, statusCode: StatusCodes.Status423Locked);

            await using (lockHandle)
            {
                await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

                // dryRun (bool)
                bool isDryRun = false;
                if (!string.IsNullOrWhiteSpace(dryRun) && bool.TryParse(dryRun, out var b)) isDryRun = b;

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
                var cmd = new CineBoutique.Inventory.Api.Models.ProductImportCommand(csvStream, isDryRun, user, shopId, CineBoutique.Inventory.Api.Models.ProductImportMode.ReplaceCatalogue);
                var result = await importService.ImportAsync(cmd, ct).ConfigureAwait(false);

                return result.ResultType switch
                {
                    CineBoutique.Inventory.Api.Models.ProductImportResultType.ValidationFailed => Results.BadRequest(result.Response),
                    CineBoutique.Inventory.Api.Models.ProductImportResultType.Skipped          => Results.StatusCode(StatusCodes.Status204NoContent),
                    _                                                                          => Results.Ok(result.Response)
                };
            }
        }).RequireAuthorization();

        // LISTE PAGINÉE/Tri/Filtre
        ApplyCatalogVisibility(app.MapGet("/api/shops/{shopId:guid}/products", async (
            System.Guid shopId,
            int? page,
            int? pageSize,
            string? q,
            string? sortBy,
            string? sortDir,
            System.Data.IDbConnection connection,
            System.Threading.CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            var p  = System.Math.Max(1, page ?? 1);
            var ps = System.Math.Clamp(pageSize ?? 50, 1, 200);
            var off = (p - 1) * ps;
            var filter = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var sort = (sortBy ?? "sku").Trim().ToLowerInvariant();
            sort = sort switch
            {
                "ean"    => "ean",
                "name"   => "name",
                "descr"  => "descr",
                "digits" => "digits",
                _         => "sku"
            };
            var dir  = string.Equals(sortDir, "desc", System.StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

            const string whereClause = """
WHERE "ShopId"=@ShopId AND (
    @Filter IS NULL OR @Filter='' OR
    COALESCE("Sku",'') ILIKE '%'||@Filter||'%' OR
    COALESCE("Ean",'') ILIKE '%'||@Filter||'%' OR
    COALESCE("CodeDigits",'') ILIKE '%'||@Filter||'%' OR
    COALESCE("Description",'') ILIKE '%'||@Filter||'%' OR
    COALESCE("Name",'') ILIKE '%'||@Filter||'%'
)
""";

            var total = await connection.ExecuteScalarAsync<long>(
                new Dapper.CommandDefinition($$"""
SELECT COUNT(*)
FROM "Product"
{{whereClause}};
""", new { ShopId = shopId, Filter = filter }, cancellationToken: ct)).ConfigureAwait(false);

            var data = await connection.QueryAsync(
                new Dapper.CommandDefinition($$"""
SELECT "Id","Sku","Name","Ean","Description","CodeDigits"
FROM "Product"
{{whereClause}}
ORDER BY
  CASE WHEN @Sort='ean'   THEN "Ean"
       WHEN @Sort='name'  THEN "Name"
       WHEN @Sort='descr' THEN "Description"
       WHEN @Sort='digits'THEN "CodeDigits"
       ELSE "Sku" END {{dir}},
  "Sku" ASC
LIMIT @Limit OFFSET @Offset;
""", new { ShopId = shopId, Filter = filter, Sort = sort, Limit = ps, Offset = off }, cancellationToken: ct)).ConfigureAwait(false);

            var totalPages = total == 0 ? 0 : (int)System.Math.Ceiling(total / (double)ps);
            return Results.Ok(new {
                items = data,
                page = p,
                pageSize = ps,
                total,
                totalPages,
                sortBy = sort,
                sortDir = dir,
                q = filter
            });
        }), catalogEndpointsPublic);

        ApplyCatalogVisibility(app.MapGet("/api/shops/{shopId:guid}/products/export", async (
            System.Guid shopId,
            System.Data.IDbConnection connection,
            System.Threading.CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            const string sql = """
    SELECT "Sku","Ean","Name","Description","CodeDigits"
    FROM "Product"
    WHERE "ShopId"=@ShopId
    ORDER BY "Sku";
    """;

            var rows = await connection.QueryAsync(
                new Dapper.CommandDefinition(sql, new { ShopId = shopId }, cancellationToken: ct)
            ).ConfigureAwait(false);

            // Construire CSV ; séparateur ; ; latin1
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("sku;ean;name;description;codeDigits");
            foreach (dynamic r in rows)
            {
                string esc(string? s) => (s ?? string.Empty).Replace("\"", "\"\"");
                sb.Append('"').Append(esc((string)r.Sku)).Append('"').Append(';')
                  .Append('"').Append(esc((string?)r.Ean)).Append('"').Append(';')
                  .Append('"').Append(esc((string)r.Name)).Append('"').Append(';')
                  .Append('"').Append(esc((string?)r.Description)).Append('"').Append(';')
                  .Append('"').Append(esc((string?)r.CodeDigits)).Append('"')
                  .AppendLine();
            }

            var bytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());
            return Results.File(bytes, "text/csv; charset=ISO-8859-1", $"products_{shopId}.csv");
        }), catalogEndpointsPublic);

        // COMPTEUR PAR BOUTIQUE (+ présence de catalogue)
        ApplyCatalogVisibility(app.MapGet("/api/shops/{shopId:guid}/products/count", async (
            System.Guid shopId,
            System.Data.IDbConnection connection,
            System.Threading.CancellationToken ct) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

            const string countSql = "SELECT COUNT(*) FROM \"Product\" WHERE \"ShopId\"=@ShopId;";
            var count = await connection.ExecuteScalarAsync<long>(
                new Dapper.CommandDefinition(countSql, new { ShopId = shopId }, cancellationToken: ct)).ConfigureAwait(false);

            bool hasCatalog;
            try
            {
                const string histSql = "SELECT EXISTS (SELECT 1 FROM \"ProductImportHistory\" WHERE \"ShopId\"=@ShopId);";
                hasCatalog = await connection.ExecuteScalarAsync<bool>(
                    new Dapper.CommandDefinition(histSql, new { ShopId = shopId }, cancellationToken: ct)).ConfigureAwait(false);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
            {
                const string fallbackSql = "SELECT EXISTS (SELECT 1 FROM \"ProductImport\" WHERE \"ShopId\"=@ShopId);";
                hasCatalog = await connection.ExecuteScalarAsync<bool>(
                    new Dapper.CommandDefinition(fallbackSql, new { ShopId = shopId }, cancellationToken: ct)).ConfigureAwait(false);
            }

            return Results.Ok(new { count, hasCatalog });
        }), catalogEndpointsPublic);
    }
}
