using System.Data;
using System.Linq;
using CineBoutique.Inventory.Api.Auth;
using CineBoutique.Inventory.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class ShopAdminEndpoints
{
    public static IEndpointRouteBuilder MapShopEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/shops").RequireAuthorization();

        group.MapGet(string.Empty, async (
            HttpContext context,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var shops = (await connection.QueryAsync<ShopDto>(
                    new CommandDefinition(
                        @"SELECT ""Id"", ""Name"", ""CreatedAtUtc"" FROM ""Shop"" ORDER BY lower(""Name"");",
                        cancellationToken: cancellationToken))
                    .ConfigureAwait(false))
                .ToList();

            return Results.Ok(shops);
        }).WithName("GetShops");

        group.MapPost(string.Empty, async (
            CreateShopRequest request,
            HttpContext context,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Le nom de la boutique est requis." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var name = request.Name.Trim();

            try
            {
                var inserted = await connection.QuerySingleAsync<ShopDto>(
                        new CommandDefinition(
                            @"INSERT INTO ""Shop"" (""Name"") VALUES (@Name) RETURNING ""Id"", ""Name"", ""CreatedAtUtc"";",
                            new { Name = name },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                return Results.Created($"/api/shops/{inserted.Id}", inserted);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { message = "Une boutique portant ce nom existe déjà." });
            }
        }).WithName("CreateShop");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateShopRequest request,
            HttpContext context,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Le nom de la boutique est requis." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var name = request.Name.Trim();

            try
            {
                var updated = await connection.QuerySingleOrDefaultAsync<ShopDto>(
                        new CommandDefinition(
                            @"UPDATE ""Shop"" SET ""Name"" = @Name WHERE ""Id"" = @Id RETURNING ""Id"", ""Name"", ""CreatedAtUtc"";",
                            new { Id = id, Name = name },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                return updated is null
                    ? Results.NotFound()
                    : Results.Ok(updated);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { message = "Une boutique portant ce nom existe déjà." });
            }
        }).WithName("UpdateShop");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext context,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var exists = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(
                        "SELECT EXISTS (SELECT 1 FROM \"Shop\" WHERE \"Id\" = @Id);",
                        new { Id = id },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (!exists)
            {
                return Results.NotFound();
            }

            var hasLocations = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(
                        "SELECT EXISTS (SELECT 1 FROM \"Location\" WHERE \"ShopId\" = @Id);",
                        new { Id = id },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var hasUsers = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(
                        "SELECT EXISTS (SELECT 1 FROM \"ShopUser\" WHERE \"ShopId\" = @Id);",
                        new { Id = id },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (hasLocations || hasUsers)
            {
                return Results.Conflict(new { message = "La boutique ne peut pas être supprimée car des ressources y sont rattachées." });
            }

            await connection.ExecuteAsync(
                    new CommandDefinition(
                        "DELETE FROM \"Shop\" WHERE \"Id\" = @Id;",
                        new { Id = id },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return Results.NoContent();
        }).WithName("DeleteShop");

        return app;
    }

    public static IEndpointRouteBuilder MapShopUserEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/shops/{shopId:guid}/users").RequireAuthorization();

        group.MapGet(string.Empty, async (
            Guid shopId,
            HttpContext context,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var users = (await connection.QueryAsync<ShopUserDto>(
                    new CommandDefinition(
                        @"SELECT ""Id"", ""ShopId"", ""Login"", ""DisplayName"", ""IsAdmin"", ""Disabled"", ""CreatedAtUtc""
FROM ""ShopUser""
WHERE ""ShopId"" = @ShopId
ORDER BY lower(""Login"");",
                        new { ShopId = shopId },
                        cancellationToken: cancellationToken))
                    .ConfigureAwait(false))
                .ToList();

            return Results.Ok(users);
        }).WithName("GetShopUsers");

        group.MapPost(string.Empty, async (
            Guid shopId,
            CreateShopUserRequest request,
            HttpContext context,
            IDbConnection connection,
            ISecretHasher secretHasher,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            if (request is null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.BadRequest(new { message = "Le login et le nom d'affichage sont requis." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var login = request.Login.Trim();
            var displayName = request.DisplayName.Trim();
            string? secretHash = null;

            if (!string.IsNullOrWhiteSpace(request.Secret))
            {
                secretHash = secretHasher.Hash(request.Secret.Trim());
            }

            try
            {
                var inserted = await connection.QuerySingleAsync<ShopUserDto>(
                        new CommandDefinition(
                            @"INSERT INTO ""ShopUser"" (""ShopId"", ""Login"", ""DisplayName"", ""IsAdmin"", ""Secret_Hash"", ""Disabled"")
VALUES (@ShopId, @Login, @DisplayName, @IsAdmin, @SecretHash, FALSE)
RETURNING ""Id"", ""ShopId"", ""Login"", ""DisplayName"", ""IsAdmin"", ""Disabled"", ""CreatedAtUtc"";",
                            new
                            {
                                ShopId = shopId,
                                Login = login,
                                DisplayName = displayName,
                                IsAdmin = request.IsAdmin,
                                SecretHash = secretHash
                            },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                return Results.Created($"/api/shops/{shopId}/users/{inserted.Id}", inserted);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { message = "Un utilisateur avec ce login existe déjà pour cette boutique." });
            }
        }).WithName("CreateShopUser");

        group.MapPut("/{id:guid}", async (
            Guid shopId,
            Guid id,
            UpdateShopUserRequest request,
            HttpContext context,
            IDbConnection connection,
            ISecretHasher secretHasher,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            if (request is null || string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Results.BadRequest(new { message = "Le nom d'affichage est requis." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            string? secretHash = null;
            var shouldUpdateSecret = request.Secret is not null;

            if (request.Secret is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Secret))
                {
                    secretHash = null;
                }
                else
                {
                    secretHash = secretHasher.Hash(request.Secret.Trim());
                }
            }

            var updated = await connection.QuerySingleOrDefaultAsync<ShopUserDto>(
                    new CommandDefinition(
                        @"UPDATE ""ShopUser""
SET ""DisplayName"" = @DisplayName,
    ""IsAdmin"" = @IsAdmin,
    ""Disabled"" = @Disabled,
    ""Secret_Hash"" = CASE WHEN @ShouldUpdateSecret THEN @SecretHash ELSE ""Secret_Hash"" END
WHERE ""Id"" = @Id AND ""ShopId"" = @ShopId
RETURNING ""Id"", ""ShopId"", ""Login"", ""DisplayName"", ""IsAdmin"", ""Disabled"", ""CreatedAtUtc"";",
                        new
                        {
                            Id = id,
                            ShopId = shopId,
                            DisplayName = request.DisplayName.Trim(),
                            IsAdmin = request.IsAdmin,
                            Disabled = request.Disabled,
                            ShouldUpdateSecret = shouldUpdateSecret,
                            SecretHash = secretHash
                        },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).WithName("UpdateShopUser");

        group.MapDelete("/{id:guid}", async (
            Guid shopId,
            Guid id,
            HttpContext context,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (!IsAdmin(context))
            {
                return Results.Forbid();
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var affected = await connection.ExecuteAsync(
                    new CommandDefinition(
                        @"UPDATE ""ShopUser"" SET ""Disabled"" = TRUE WHERE ""Id"" = @Id AND ""ShopId"" = @ShopId;",
                        new { Id = id, ShopId = shopId },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return affected == 0 ? Results.NotFound() : Results.NoContent();
        }).WithName("DeleteShopUser");

        return app;
    }

    private static bool IsAdmin(HttpContext context)
    {
        var claim = context?.User?.FindFirst("is_admin")?.Value;
        return claim is not null && claim.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
