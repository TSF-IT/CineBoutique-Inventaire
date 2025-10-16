// Modifications : déplacement des endpoints produits depuis Program.cs avec mutualisation des helpers locaux.
using System;
using System.Data;
using System.Linq;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Api.Services.Products;
using Dapper;
using Npgsql;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class ProductEndpoints
{
    private const string LowerSkuConstraintName = "UX_Product_LowerSku";
    private const string EanNotNullConstraintName = "UX_Product_Ean_NotNull";

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreateProductEndpoint(app);
        MapGetProductEndpoint(app);
        MapUpdateProductEndpoints(app);

        return app;
    }

    private static void MapUpdateProductEndpoints(IEndpointRouteBuilder app)
    {
        // --- MAJ par SKU ---

        var updateBySku = async (
            string sku,
            CreateProductRequest request,
            IDbConnection connection,
            IAuditLogger auditLogger,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
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

        app.MapPost("/api/products/{sku}", updateBySku)
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

        app.MapPut("/api/products/{sku}", updateBySku)
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
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString(), "corps null", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le corps de la requête est requis." });
            }

            var sanitizedName = request.Name?.Trim();
            var sanitizedEan = string.IsNullOrWhiteSpace(request.Ean) ? null : request.Ean.Trim();
            var sanitizedCodeDigits = CodeDigitsSanitizer.Build(sanitizedEan);

            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString(), "sans nom", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit est requis." });
            }

            if (sanitizedName.Length > 256)
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString(), "nom trop long", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit ne peut pas dépasser 256 caractères." });
            }

            if (sanitizedEan is { Length: > 0 } && (sanitizedEan.Length is < 8 or > 13 || !sanitizedEan.All(char.IsDigit)))
            {
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString(), "EAN invalide", "products.update.invalid", cancellationToken).ConfigureAwait(false);
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
                await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString(), "inexistant", "products.update.notfound", cancellationToken).ConfigureAwait(false);
                return Results.NotFound(new { message = $"Aucun produit avec l'Id '{id}'." });
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
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString(), $"EAN déjà utilisé ({sanitizedEan})", "products.update.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Cet EAN est déjà utilisé." });
                }

                if (string.Equals(ex.ConstraintName, LowerSkuConstraintName, StringComparison.Ordinal))
                {
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString(), "SKU déjà utilisé", "products.update.conflict", cancellationToken).ConfigureAwait(false);
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

    private static void MapGetProductEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/{code}", async (
            string code,
            IProductLookupService lookupService,
            IAuditLogger auditLogger,
            IClock clock,
            HttpContext httpContext,
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

}
