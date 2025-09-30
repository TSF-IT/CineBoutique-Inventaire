// Migrations/202409301200_AddUniqueActiveRunIndex.cs
using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202409301200)]
public sealed class AddUniqueActiveRunIndex : Migration
{
    public override void Up()
    {
        // 1) Normaliser et verrouiller OperatorDisplayName
        Execute.Sql(@"UPDATE ""CountingRun"" SET ""OperatorDisplayName"" = 'Unknown' WHERE ""OperatorDisplayName"" IS NULL;");
        Alter.Table("CountingRun")
            .AlterColumn("OperatorDisplayName").AsString(200).NotNullable();

        // 2) Index unique partiel pour empêcher 2 runs ouverts sur le même triplet logique dans une même session
        //    (InventorySessionId, LocationId, CountType, OperatorDisplayName) où CompletedAtUtc IS NULL
        Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_countingrun_active_triplet
ON ""CountingRun"" (""InventorySessionId"", ""LocationId"", ""CountType"", ""OperatorDisplayName"")
WHERE ""CompletedAtUtc"" IS NULL;");
    }

    public override void Down()
    {
        Execute.Sql(@"DROP INDEX IF EXISTS ux_countingrun_active_triplet;");
        // Revenir à nullable si tu veux strictement un down symétrique:
        // Alter.Table("CountingRun").AlterColumn("OperatorDisplayName").AsString(200).Nullable();
    }
}
