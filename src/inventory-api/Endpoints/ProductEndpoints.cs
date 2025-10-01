// Modifications : déplacement des endpoints produits depuis Program.cs avec mutualisation des helpers locaux.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi.Models;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapCreateProductEndpoint(app);
        MapGetProductEndpoint(app);

        return app;
    }

    private static void MapCreateProductEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/products", async (
            CreateProductRequest request,
            IDbConnection connection,
            IAuditLogger auditLogger,
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

            if (string.IsNullOrWhiteSpace(sanitizedSku))
            {
                await LogProductCreationAttemptAsync(auditLogger, httpContext, "sans SKU", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le SKU est requis." });
            }

            if (sanitizedSku.Length > 32)
            {
                await LogProductCreationAttemptAsync(auditLogger, httpContext, "avec un SKU trop long", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le SKU ne peut pas dépasser 32 caractères." });
            }

            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                await LogProductCreationAttemptAsync(auditLogger, httpContext, "sans nom", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit est requis." });
            }

            if (sanitizedName.Length > 256)
            {
                await LogProductCreationAttemptAsync(auditLogger, httpContext, "avec un nom trop long", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le nom du produit ne peut pas dépasser 256 caractères." });
            }

            if (sanitizedEan is { Length: > 0 } && (sanitizedEan.Length is < 8 or > 13 || !sanitizedEan.All(char.IsDigit)))
            {
                await LogProductCreationAttemptAsync(auditLogger, httpContext, "avec un EAN invalide", "products.create.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "L'EAN doit contenir entre 8 et 13 chiffres." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            const string skuCheckSql = "SELECT 1 FROM \"Product\" WHERE LOWER(\"Sku\") = LOWER(@Sku) LIMIT 1;";
            var skuExists = await connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(skuCheckSql, new { Sku = sanitizedSku }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (skuExists.HasValue)
            {
                await LogProductCreationAttemptAsync(auditLogger, httpContext, $"avec un SKU déjà utilisé ({sanitizedSku})", "products.create.conflict", cancellationToken).ConfigureAwait(false);
                return Results.Conflict(new { message = "Ce SKU est déjà utilisé." });
            }

            if (!string.IsNullOrWhiteSpace(sanitizedEan))
            {
                const string eanCheckSql = "SELECT 1 FROM \"Product\" WHERE \"Ean\" = @Ean LIMIT 1;";
                var eanExists = await connection.ExecuteScalarAsync<int?>(
                    new CommandDefinition(eanCheckSql, new { Ean = sanitizedEan }, cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (eanExists.HasValue)
                {
                    await LogProductCreationAttemptAsync(auditLogger, httpContext, $"avec un EAN déjà utilisé ({sanitizedEan})", "products.create.conflict", cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new { message = "Cet EAN est déjà utilisé." });
                }
            }

            const string insertSql = @"INSERT INTO ""Product"" (""Id"", ""Sku"", ""Name"", ""Ean"", ""CreatedAtUtc"")
VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc)
RETURNING ""Id"", ""Sku"", ""Name"", ""Ean"";";

            var now = DateTimeOffset.UtcNow;
            var createdProduct = await connection.QuerySingleAsync<ProductDto>(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        Id = Guid.NewGuid(),
                        Sku = sanitizedSku,
                        Name = sanitizedName,
                        Ean = sanitizedEan,
                        CreatedAtUtc = now
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
            var actor = EndpointUtilities.FormatActorLabel(userName);
            var timestamp = EndpointUtilities.FormatTimestamp(now);
            var eanLabel = string.IsNullOrWhiteSpace(createdProduct.Ean) ? "non renseigné" : createdProduct.Ean;
            var creationMessage = $"{actor} a créé le produit \"{createdProduct.Name}\" (SKU {createdProduct.Sku}, EAN {eanLabel}) le {timestamp} UTC.";
            await auditLogger.LogAsync(creationMessage, userName, "products.create.success", cancellationToken).ConfigureAwait(false);

            var location = $"/api/products/{Uri.EscapeDataString(createdProduct.Sku)}";
            return Results.Created(location, createdProduct);
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
            IDbConnection connection,
            IAuditLogger auditLogger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                var nowInvalid = DateTimeOffset.UtcNow;
                var invalidUser = EndpointUtilities.GetAuthenticatedUserName(httpContext);
                var invalidActor = EndpointUtilities.FormatActorLabel(invalidUser);
                var invalidTimestamp = EndpointUtilities.FormatTimestamp(nowInvalid);
                var invalidMessage = $"{invalidActor} a tenté de scanner un code produit vide le {invalidTimestamp} UTC.";
                await auditLogger.LogAsync(invalidMessage, invalidUser, "products.scan.invalid", cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { message = "Le code produit est requis." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var sanitizedCode = code.Trim();
            var candidateEans = BuildCandidateEanCodes(sanitizedCode);

            ProductDto? product = null;
            if (candidateEans.Length > 0)
            {
                var dynamicParameters = new DynamicParameters();
                var parameterNames = new string[candidateEans.Length];

                for (var index = 0; index < candidateEans.Length; index++)
                {
                    var parameterName = $"Code{index}";
                    parameterNames[index] = parameterName;
                    dynamicParameters.Add(parameterName, candidateEans[index]);
                }

                var conditions = string.Join(" OR ", parameterNames.Select(name => $"\"Ean\" = @{name}"));
                var sql = $"SELECT \"Id\", \"Sku\", \"Name\", \"Ean\" FROM \"Product\" WHERE {conditions} LIMIT 1";

                product = await connection
                    .QuerySingleOrDefaultAsync<ProductDto>(
                        new CommandDefinition(sql, dynamicParameters, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }

            if (product is null)
            {
                product = await connection.QuerySingleOrDefaultAsync<ProductDto>(
                    new CommandDefinition(
                        "SELECT \"Id\", \"Sku\", \"Name\", \"Ean\" FROM \"Product\" WHERE LOWER(\"Sku\") = LOWER(@Code) LIMIT 1",
                        new { Code = sanitizedCode },
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow;
            var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
            var actor = EndpointUtilities.FormatActorLabel(userName);
            var timestamp = EndpointUtilities.FormatTimestamp(now);

            if (product is not null)
            {
                var productLabel = product.Name;
                var skuLabel = string.IsNullOrWhiteSpace(product.Sku) ? "non renseigné" : product.Sku;
                var eanLabel = string.IsNullOrWhiteSpace(product.Ean) ? "non renseigné" : product.Ean;
                var successMessage = $"{actor} a scanné le code {sanitizedCode} et a identifié le produit \"{productLabel}\" (SKU {skuLabel}, EAN {eanLabel}) le {timestamp} UTC.";
                await auditLogger.LogAsync(successMessage, userName, "products.scan.success", cancellationToken).ConfigureAwait(false);
                return Results.Ok(product);
            }

            var notFoundMessage = $"{actor} a scanné le code {sanitizedCode} sans correspondance produit le {timestamp} UTC.";
            await auditLogger.LogAsync(notFoundMessage, userName, "products.scan.not_found", cancellationToken).ConfigureAwait(false);

            return Results.NotFound();
        })
        .WithName("GetProductByCode")
        .WithTags("Produits")
        .Produces<ProductDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi(op =>
        {
            op.Summary = "Recherche un produit par code scanné.";
            op.Description = "Retourne un produit à partir de son SKU ou d'un code EAN scanné.";
            return op;
        });
    }

    private static async Task LogProductCreationAttemptAsync(
        IAuditLogger auditLogger,
        HttpContext httpContext,
        string details,
        string category,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
        var actor = EndpointUtilities.FormatActorLabel(userName);
        var timestamp = EndpointUtilities.FormatTimestamp(now);
        var message = $"{actor} a tenté de créer un produit {details} le {timestamp} UTC.";
        await auditLogger.LogAsync(message, userName, category, cancellationToken).ConfigureAwait(false);
    }

    private static string[] BuildCandidateEanCodes(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Array.Empty<string>();
        }

        var trimmed = code.Trim();
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        void AddCandidate(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(value);
            }
        }

        AddCandidate(trimmed);

        var normalized = trimmed.TrimStart('0');
        if (normalized.Length == 0 && trimmed.Length > 0)
        {
            normalized = "0";
        }

        if (normalized.Length > 0)
        {
            AddCandidate(normalized);

            var targetLength = Math.Max(normalized.Length, trimmed.Length);
            for (var length = normalized.Length + 1; length <= targetLength; length++)
            {
                AddCandidate(normalized.PadLeft(length, '0'));
            }
        }

        if (trimmed.Length is > 8 and < 13)
        {
            AddCandidate(trimmed.PadLeft(13, '0'));
        }

        if (trimmed.Length < 8)
        {
            AddCandidate(trimmed.PadLeft(8, '0'));
        }

        if (trimmed.Length < 13)
        {
            AddCandidate(trimmed.PadLeft(13, '0'));
        }

        return candidates.ToArray();
    }
}
