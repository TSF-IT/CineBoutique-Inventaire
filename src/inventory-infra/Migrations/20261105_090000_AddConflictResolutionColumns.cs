using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(20261105090000)]
public sealed class AddConflictResolutionColumns : Migration
{
    private const string ConflictTable = "Conflict";

    public override void Up()
    {
        if (!Schema.Table(ConflictTable).Column("ResolvedQuantity").Exists())
        {
            Alter.Table(ConflictTable)
                .AddColumn("ResolvedQuantity").AsInt32().Nullable();
        }

        if (!Schema.Table(ConflictTable).Column("IsResolved").Exists())
        {
            Alter.Table(ConflictTable)
                .AddColumn("IsResolved").AsBoolean().NotNullable().WithDefaultValue(false);

            Execute.Sql("""
UPDATE "Conflict"
SET "IsResolved" = TRUE
WHERE "ResolvedAtUtc" IS NOT NULL;
""");
        }
    }

    public override void Down()
    {
        if (Schema.Table(ConflictTable).Column("IsResolved").Exists())
        {
            Delete.Column("IsResolved").FromTable(ConflictTable);
        }

        if (Schema.Table(ConflictTable).Column("ResolvedQuantity").Exists())
        {
            Delete.Column("ResolvedQuantity").FromTable(ConflictTable);
        }
    }
}
