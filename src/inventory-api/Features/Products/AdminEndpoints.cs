using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Minimal;
using CineBoutique.Inventory.Api.Infrastructure.Shops;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services.Products;
using CineBoutique.Inventory.Api.Validation;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Npgsql;

namespace CineBoutique.Inventory.Api.Features.Products;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapProductAdminEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreateProductEndpoint(app);
        MapUpdateProductEndpoints(app);

        return app;
    }

    private const string LowerSkuConstraintName = "UX_Product_Shop_LowerSku";
    private const string EanNotNullConstraintName = "UX_Product_Shop_Ean_NotNull";
    private sealed class ProductLookupLogger
    {
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
                var sanitizedEan = InventoryCodeValidator.Normalize(request.Ean);
                if (sanitizedEan is not null && !InventoryCodeValidator.TryValidate(sanitizedEan, out var eanError))
                {
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, sanitizedSku, "EAN invalide", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                    return Results.BadRequest(new { message = eanError });
                }

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
               })
               .AddEndpointFilter<RequireOperatorHeadersFilter>()
               .RequireAuthorization("Admin");

            app.MapPut("/api/products/{code}", updateBySku)
               .WithName("UpdateProductBySku")
               .WithTags("Produits")
               .Produces<ProductDto>(StatusCodes.Status200OK)
               .Produces(StatusCodes.Status400BadRequest)
               .Produces(StatusCodes.Status404NotFound)
               .Produces(StatusCodes.Status409Conflict)
               .AddEndpointFilter<RequireOperatorHeadersFilter>()
               .RequireAuthorization("Admin");

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
                var sanitizedEan = InventoryCodeValidator.Normalize(request.Ean);
                if (sanitizedEan is not null && !InventoryCodeValidator.TryValidate(sanitizedEan, out var eanError))
                {
                    await LogProductUpdateAttemptAsync(clock, auditLogger, httpContext, id.ToString("D"), "EAN invalide", "products.update.invalid", cancellationToken).ConfigureAwait(false);
                    return Results.BadRequest(new { message = eanError });
                }

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
               .Produces(StatusCodes.Status409Conflict)
               .AddEndpointFilter<RequireOperatorHeadersFilter>()
               .RequireAuthorization("Admin");

            app.MapPut("/api/products/by-id/{id:guid}", updateById)
               .WithName("UpdateProductById")
               .WithTags("Produits")
               .Produces<ProductDto>(StatusCodes.Status200OK)
               .Produces(StatusCodes.Status400BadRequest)
               .Produces(StatusCodes.Status404NotFound)
               .Produces(StatusCodes.Status409Conflict)
               .AddEndpointFilter<RequireOperatorHeadersFilter>()
               .RequireAuthorization("Admin");
        }

        private static void MapCreateProductEndpoint(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/products", async (
                CreateProductRequest request,
                IDbConnection connection,
                IAuditLogger auditLogger,
                IClock clock,
                IShopResolver shopResolver,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (request is null)
                {
                    return Results.BadRequest(new { message = "Le corps de la requête est requis." });
                }

                var sanitizedSku = request.Sku?.Trim();
                var sanitizedName = request.Name?.Trim();
                var sanitizedEan = InventoryCodeValidator.Normalize(request.Ean);
                if (sanitizedEan is not null && !InventoryCodeValidator.TryValidate(sanitizedEan, out var eanError))
                {
                    await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, "avec un EAN invalide", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                    return Results.BadRequest(new { message = eanError });
                }

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

                await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

                Guid? shopId = null;
                try
                {
                    shopId = await shopResolver.GetDefaultForBackCompatAsync(connection, cancellationToken).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
                {
                    // La table des shops n'est pas encore créée : on laisse shopId à null et on gèrera plus bas.
                }
                catch (InvalidOperationException)
                {
                    // Absence de boutique disponible : on gère plus bas.
                }

                if (!shopId.HasValue)
                {
                    await LogProductCreationAttemptAsync(clock, auditLogger, httpContext, "sans boutique", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Aucune boutique disponible",
                        detail: "Impossible de créer un produit sans boutique active.");
                }

                const string insertSql = @"INSERT INTO ""Product"" (""Id"", ""ShopId"", ""Sku"", ""Name"", ""Ean"", ""CodeDigits"", ""CreatedAtUtc"")
    VALUES (@Id, @ShopId, @Sku, @Name, @Ean, @CodeDigits, @CreatedAtUtc)
    ON CONFLICT (""ShopId"", LOWER(""Sku"")) DO NOTHING
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
                                CreatedAtUtc = now,
                                ShopId = shopId.Value
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
            })
            .AddEndpointFilter<RequireOperatorHeadersFilter>()
            .RequireAuthorization("Admin");
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
                    .Select(item => new ProductSearchItemDto(item.Sku, item.Code, item.Name, item.Group, item.SubGroup))
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
            })
            .RequireAuthorization();
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
            })
            .RequireAuthorization();
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
