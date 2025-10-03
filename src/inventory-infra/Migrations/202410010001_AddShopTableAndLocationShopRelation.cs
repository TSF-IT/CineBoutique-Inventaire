using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202410010001)]
public sealed class AddShopTableAndLocationShopRelation : Migration
{
    private const string ShopsTable = "Shop";
    private const string LocationsTable = "Location";
    private const string ShopIdColumn = "ShopId";
    private const string ShopNameUniqueIndex = "UQ_Shop_LowerName";
    private const string LocationShopCodeUniqueIndex = "UQ_Location_Shop_Code";
    private const string LocationCodeUniqueIndex = "IX_Location_Code";
    private const string LocationShopForeignKey = "FK_Location_Shop";
    private const string DefaultShopName = "CinéBoutique Paris";

    public override void Up()
    {
        Create.Table(ShopsTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("Name").AsString(256).NotNullable();

        Execute.Sql(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{ShopNameUniqueIndex}" ON "{ShopsTable}" (LOWER("Name"));
            """);

        Alter.Table(LocationsTable)
            .AddColumn(ShopIdColumn).AsGuid().Nullable();

        Execute.Sql($"""DROP INDEX IF EXISTS "{LocationCodeUniqueIndex}";""");

        Execute.Sql(
            $"""
            DO $$
            DECLARE
                paris_id uuid;
            BEGIN
                INSERT INTO "{ShopsTable}" ("Name") VALUES ('{DefaultShopName}')
                ON CONFLICT DO NOTHING;

                SELECT "Id" INTO paris_id
                FROM "{ShopsTable}"
                WHERE LOWER("Name") = LOWER('{DefaultShopName}')
                ORDER BY "Id"
                LIMIT 1;

                UPDATE "{LocationsTable}"
                SET "{ShopIdColumn}" = paris_id
                WHERE "{ShopIdColumn}" IS NULL;
            END $$;
            """);

        Execute.Sql(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{LocationShopCodeUniqueIndex}" ON "{LocationsTable}" ("{ShopIdColumn}", UPPER("Code"));
            """);

        Create.ForeignKey(LocationShopForeignKey)
            .FromTable(LocationsTable).ForeignColumn(ShopIdColumn)
            .ToTable(ShopsTable).PrimaryColumn("Id");
    }

    public override void Down()
    {
        Delete.ForeignKey(LocationShopForeignKey).OnTable(LocationsTable);

        Execute.Sql($"""DROP INDEX IF EXISTS "{LocationShopCodeUniqueIndex}";""");

        Delete.Column(ShopIdColumn).FromTable(LocationsTable);

        Execute.Sql($"""DROP INDEX IF EXISTS "{ShopNameUniqueIndex}";""");

        Delete.Table(ShopsTable);

        Execute.Sql(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{LocationCodeUniqueIndex}" ON "{LocationsTable}" ("Code" ASC);
            """);
    }
}
