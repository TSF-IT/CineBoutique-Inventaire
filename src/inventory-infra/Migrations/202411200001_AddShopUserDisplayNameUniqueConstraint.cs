using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202411200001)]
public sealed class AddShopUserDisplayNameUniqueConstraint : Migration
{
    private const string TableName = "ShopUser";
    private const string UniqueConstraintName = "uq_shopuser_shopid_displayname";
    private const string IndexName = "ix_shopuser_shopid_displayname";

    public override void Up()
    {
        Execute.Sql($"""
        ALTER TABLE "{TableName}"
            ADD CONSTRAINT IF NOT EXISTS "{UniqueConstraintName}"
            UNIQUE ("ShopId", "DisplayName");
        """);

        Execute.Sql($"""
        CREATE INDEX IF NOT EXISTS "{IndexName}"
            ON "{TableName}" ("ShopId", "DisplayName");
        """);
    }

    public override void Down()
    {
        Execute.Sql($"""DROP INDEX IF EXISTS "{IndexName}";""");
        Execute.Sql($"""
        ALTER TABLE "{TableName}"
            DROP CONSTRAINT IF EXISTS "{UniqueConstraintName}";
        """);
    }
}
