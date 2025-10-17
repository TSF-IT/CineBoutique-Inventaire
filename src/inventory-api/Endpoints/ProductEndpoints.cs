// Modifications : déplacement des endpoints produits depuis Program.cs avec mutualisation des helpers locaux.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services.Products;
using CineBoutique.Inventory.Infrastructure.Database;
using FluentValidation.Results;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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

        MapCreateProductEndpoint(app);
        MapSearchProductsEndpoint(app);
        MapSuggestProductsEndpoint(app);
        MapGetProductEndpoint(app);
        MapUpdateProductEndpoints(app);
        MapImportProductsEndpoint(app);

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

    private static void MapImportProductsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/products/import", async (
                HttpContext httpContext,
                IProductImportService importService,
                CancellationToken cancellationToken) =>
            {
                if (httpContext is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
                }

                var request = httpContext.Request;
                const long maxCsvSizeBytes = 25L * 1024L * 1024L;

                if (request.ContentLength is { } contentLength && contentLength > maxCsvSizeBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                static bool TryParseDryRun(HttpRequest req, out bool dryRun, out bool isInvalid)
                {
                    dryRun = false;
                    isInvalid = false;

                    if (!req.Query.TryGetValue("dryRun", out var values) || values.Count == 0)
                    {
                        return true;
                    }

                    var raw = values[^1];
                    if (string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        dryRun = true;
                        return true;
                    }

                    if (string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        dryRun = false;
                        return true;
                    }

                    isInvalid = true;
                    return false;
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

                if (!TryParseDryRun(request, out var dryRun, out var invalidDryRun) || invalidDryRun)
                {
                    return BuildFailure("INVALID_DRY_RUN");
                }

                Stream? ownedStream = null;
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
                    var command = new ProductImportCommand(streamToImport, dryRun, username);
                    var result = await importService.ImportAsync(command, cancellationToken).ConfigureAwait(false);

                    return result.ResultType switch
                    {
                        ProductImportResultType.ValidationFailed => Results.BadRequest(result.Response),
                        ProductImportResultType.Skipped => Results.Json(
                            result.Response,
                            statusCode: StatusCodes.Status204NoContent),
                        _ => Results.Ok(result.Response)
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
                }
            })
            .RequireAuthorization("Admin")
            .WithName("ImportProducts")
            .WithTags("Produits")
            .Produces<ProductImportResponse>(StatusCodes.Status200OK)
            .Produces<ProductImportResponse>(StatusCodes.Status400BadRequest)
            .Produces<ProductImportResponse>(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces(StatusCodes.Status423Locked)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Importe le catalogue produits depuis un fichier CSV.";
                operation.Description = "Remplace l'intégralité de la table Product à partir d'un CSV encodé en ISO-8859-1 (Latin-1) utilisant ';' comme séparateur, contenant au minimum les colonnes barcode_rfid;item;descr. Accessible uniquement aux administrateurs.";

                operation.RequestBody ??= new OpenApiRequestBody();
                operation.RequestBody.Required = true;
                operation.RequestBody.Description = "Fichier CSV encodé en ISO-8859-1 (Latin-1) avec séparateur ';' et entête barcode_rfid;item;descr.";
                operation.RequestBody.Content["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties =
                        {
                            ["file"] = new OpenApiSchema
                            {
                                Type = "string",
                                Format = "binary",
                                Description = "Fichier CSV à importer"
                            }
                        },
                        Required = new HashSet<string>(StringComparer.Ordinal) { "file" }
                    }
                };

                operation.RequestBody.Content["text/csv"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Format = "binary",
                        Description = "Flux CSV brut"
                    }
                };

                var successSchema = new OpenApiSchema
                {
                    Type = "object",
                    Properties =
                    {
                        ["total"] = new OpenApiSchema { Type = "integer", Format = "int32" },
                        ["inserted"] = new OpenApiSchema { Type = "integer", Format = "int32" },
                        ["updated"] = new OpenApiSchema { Type = "integer", Format = "int32" },
                        ["wouldInsert"] = new OpenApiSchema { Type = "integer", Format = "int32" },
                        ["errorCount"] = new OpenApiSchema { Type = "integer", Format = "int32" },
                        ["dryRun"] = new OpenApiSchema { Type = "boolean" },
                        ["skipped"] = new OpenApiSchema { Type = "boolean" },
                        ["proposedGroups"] = new OpenApiSchema
                        {
                            Type = "array",
                            Items = new OpenApiSchema
                            {
                                Type = "object",
                                Properties =
                                {
                                    ["groupe"] = new OpenApiSchema { Type = "string" },
                                    ["sousGroupe"] = new OpenApiSchema { Type = "string" }
                                }
                            }
                        },
                        ["unknownColumns"] = new OpenApiSchema
                        {
                            Type = "array",
                            Items = new OpenApiSchema { Type = "string" }
                        },
                        ["errors"] = new OpenApiSchema
                        {
                            Type = "array",
                            Items = new OpenApiSchema
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.Schema,
                                    Id = nameof(ProductImportError)
                                }
                            }
                        }
                    }
                };

                successSchema.Example = new OpenApiObject
                {
                    ["total"] = new OpenApiInteger(1204),
                    ["inserted"] = new OpenApiInteger(1204),
                    ["updated"] = new OpenApiInteger(0),
                    ["wouldInsert"] = new OpenApiInteger(0),
                    ["errorCount"] = new OpenApiInteger(0),
                    ["dryRun"] = new OpenApiBoolean(false),
                    ["skipped"] = new OpenApiBoolean(false),
                    ["errors"] = new OpenApiArray(),
                    ["unknownColumns"] = new OpenApiArray
                    {
                        new OpenApiString("couleurSecondaire"),
                        new OpenApiString("tva"),
                        new OpenApiString("marque")
                    },
                    ["proposedGroups"] = new OpenApiArray
                    {
                        new OpenApiObject
                        {
                            ["groupe"] = new OpenApiString("Café"),
                            ["sousGroupe"] = new OpenApiString("Grains 1kg")
                        }
                    }
                };

                operation.Responses[StatusCodes.Status200OK.ToString()] = new OpenApiResponse
                {
                    Description = "Import réussi.",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = successSchema
                        }
                    }
                };

                operation.Responses[StatusCodes.Status204NoContent.ToString()] = new OpenApiResponse
                {
                    Description = "Import ignoré (déjà appliqué).",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = successSchema
                        }
                    }
                };

                operation.Responses[StatusCodes.Status400BadRequest.ToString()] = new OpenApiResponse
                {
                    Description = "Le fichier est invalide.",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = successSchema
                        }
                    }
                };

                operation.Responses[StatusCodes.Status413PayloadTooLarge.ToString()] = new OpenApiResponse
                {
                    Description = "Le fichier dépasse la taille maximale autorisée."
                };

                operation.Responses[StatusCodes.Status423Locked.ToString()] = new OpenApiResponse
                {
                    Description = "Un import est déjà en cours.",
                    Content =
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties =
                                {
                                    ["reason"] = new OpenApiSchema { Type = "string" }
                                }
                            }
                        }
                    }
                };

                operation.Parameters ??= new List<OpenApiParameter>();
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "dryRun",
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = "Lorsque true, valide le fichier sans appliquer les modifications.",
                    Schema = new OpenApiSchema { Type = "boolean" }
                });

                return operation;
            });
    }

    private static void MapSuggestProductsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/suggest", async (
            string? q,
            int? limit,
            IProductSuggestionService suggestionService,
            CancellationToken cancellationToken) =>
        {
            var sanitizedQuery = q?.Trim();
            if (string.IsNullOrWhiteSpace(sanitizedQuery))
            {
                var validation = new ValidationResult(new[]
                {
                    new ValidationFailure("q", "Le paramètre 'q' est obligatoire.")
                });

                return EndpointUtilities.ValidationProblem(validation);
            }

            var effectiveLimit = limit ?? 8;
            if (effectiveLimit < 1 || effectiveLimit > 50)
            {
                var validation = new ValidationResult(new[]
                {
                    new ValidationFailure("limit", "Le paramètre 'limit' doit être compris entre 1 et 50.")
                });

                return EndpointUtilities.ValidationProblem(validation);
            }

            var items = await suggestionService
                .SuggestAsync(sanitizedQuery, effectiveLimit, cancellationToken)
                .ConfigureAwait(false);

            var response = items
                .Select(item => new ProductSuggestionDto(item.Sku, item.Ean, item.Name, item.Group, item.SubGroup))
                .ToArray();

            return Results.Ok(response);
        })
        .WithName("SuggestProducts")
        .WithTags("Produits")
        .Produces<IReadOnlyList<ProductSuggestionDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithOpenApi(operation =>
        {
            operation.Summary = "Propose des produits à partir d'une saisie partielle.";
            operation.Description = "Combine une recherche préfixe sur le SKU et le code barre avec une similarité trigram sur le nom du produit et les libellés de groupe pour proposer des suggestions rapides.";

            operation.Parameters ??= new List<OpenApiParameter>();

            var queryParameter = operation.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, "q", StringComparison.OrdinalIgnoreCase));
            if (queryParameter is null)
            {
                queryParameter = new OpenApiParameter
                {
                    Name = "q",
                    In = ParameterLocation.Query,
                    Required = true
                };
                operation.Parameters.Add(queryParameter);
            }

            queryParameter.Description = "Texte recherché (SKU, EAN/code barre, nom ou groupe).";
            queryParameter.Required = true;

            var limitParameter = operation.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, "limit", StringComparison.OrdinalIgnoreCase));
            if (limitParameter is null)
            {
                limitParameter = new OpenApiParameter
                {
                    Name = "limit",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = "integer" }
                };
                operation.Parameters.Add(limitParameter);
            }

            limitParameter.Description = "Nombre maximum de suggestions (défaut : 8, min 1, max 50).";
            limitParameter.Schema ??= new OpenApiSchema { Type = "integer" };
            limitParameter.Schema.Minimum = 1;
            limitParameter.Schema.Maximum = 50;
            limitParameter.Schema.Default = new OpenApiInteger(8);

            operation.Responses ??= new OpenApiResponses();
            operation.Responses[StatusCodes.Status200OK.ToString()] = new OpenApiResponse
            {
                Description = "Liste ordonnée de suggestions de produits.",
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
                                    Id = nameof(ProductSuggestionDto)
                                }
                            }
                        },
                        Example = new OpenApiArray
                        {
                            new OpenApiObject
                            {
                                ["sku"] = new OpenApiString("CB-0001"),
                                ["ean"] = new OpenApiString("0001234567890"),
                                ["name"] = new OpenApiString("Café grains 1kg"),
                                ["group"] = new OpenApiString("Cafés"),
                                ["subGroup"] = new OpenApiString("Grains 1kg")
                            },
                            new OpenApiObject
                            {
                                ["sku"] = new OpenApiString("CAF-0102"),
                                ["ean"] = new OpenApiString("9876543210000"),
                                ["name"] = new OpenApiString("Machine expresso café"),
                                ["group"] = new OpenApiString("Machines"),
                                ["subGroup"] = new OpenApiString("Expressos")
                            }
                        }
                    }
                }
            };

            return operation;
        });
    }

    private static void MapSearchProductsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/search", async (
            string? code,
            int? limit,
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

            var effectiveLimit = limit.GetValueOrDefault(20);
            if (effectiveLimit <= 0)
            {
                var validation = new ValidationResult(new[]
                {
                    new ValidationFailure("limit", "Le paramètre 'limit' doit être strictement positif.")
                });

                return EndpointUtilities.ValidationProblem(validation);
            }

            var items = await searchService
                .SearchAsync(sanitizedCode, effectiveLimit, cancellationToken)
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
            operation.Description = "Applique successivement trois stratégies : correspondance exacte sur le SKU, correspondance exacte sur le code brut (EAN/Code) puis comparaison sur CodeDigits lorsque le code contient des chiffres. Les résultats sont fusionnés sans doublon et limités par le paramètre 'limit'.";

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
                        parameter.Description = "Nombre maximum de résultats (défaut : 20).";
                        if (parameter.Schema is not null)
                        {
                            parameter.Schema.Minimum = 1;
                            parameter.Schema.Default = new OpenApiInteger(20);
                        }
                    }
                }
            }

            operation.Responses ??= new OpenApiResponses();
            operation.Responses[StatusCodes.Status200OK.ToString()] = new OpenApiResponse
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

        var separatorIndex = contentType.IndexOf(';');
        var mediaType = separatorIndex >= 0 ? contentType[..separatorIndex] : contentType;
        return string.Equals(mediaType.Trim(), "text/csv", StringComparison.OrdinalIgnoreCase);
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
