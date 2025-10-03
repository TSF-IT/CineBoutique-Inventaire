using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202410150001)]
public sealed class CreateShopUserTable : Migration
{
    private const string ShopUsersTable = "ShopUser";
    private const string ShopsTable = "Shop";
    private const string ShopUsersUniqueIndex = "UQ_ShopUser_Shop_LowerLogin";
    private const string ShopUsersForeignKey = "FK_ShopUser_Shop";

    public override void Up()
    {
        Create.Table(ShopUsersTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("ShopId").AsGuid().NotNullable()
            .WithColumn("Login").AsString(128).NotNullable()
            .WithColumn("DisplayName").AsString(256).NotNullable()
            .WithColumn("IsAdmin").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Secret_Hash").AsString(512).NotNullable().WithDefaultValue(string.Empty)
            .WithColumn("Disabled").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.ForeignKey(ShopUsersForeignKey)
            .FromTable(ShopUsersTable).ForeignColumn("ShopId")
            .ToTable(ShopsTable).PrimaryColumn("Id");

        Execute.Sql(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{ShopUsersUniqueIndex}" ON "{ShopUsersTable}" ("ShopId", LOWER("Login"));
            """);
    }

    public override void Down()
    {
        Execute.Sql($"""DROP INDEX IF EXISTS "{ShopUsersUniqueIndex}";""");

        Delete.ForeignKey(ShopUsersForeignKey).OnTable(ShopUsersTable);

        Delete.Table(ShopUsersTable).IfExists();
    }
}
