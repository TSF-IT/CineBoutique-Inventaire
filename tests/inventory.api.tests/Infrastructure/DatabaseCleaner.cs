using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

internal static class DatabaseCleaner
{
    private const string CleanupSql =
        """
        DO $do$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Audit') THEN
                EXECUTE 'TRUNCATE TABLE "Audit" RESTART IDENTITY CASCADE;';
            END IF;
        END $do$;

        TRUNCATE TABLE "Conflict" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "CountLine" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "CountingRun" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "InventorySession" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "Location" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "ShopUser" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
        TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
        """;

    public static Task CleanAsync(IDbConnection connection, CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(CleanupSql, cancellationToken: cancellationToken);
        return connection.ExecuteAsync(command);
    }
}
