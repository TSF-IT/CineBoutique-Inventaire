using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202410010001)]
public sealed class Migration_202410010001_AddShopsAndShopUsers : Migration
{
    public override void Up()
    {
        if (!Schema.Table("Shop").Exists())
        {
            Create.Table("Shop")
                .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
                .WithColumn("Name").AsString(256).NotNullable()
                .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime);

            Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"UQ_Shop_LowerName\" ON \"Shop\" (lower(\"Name\"));");
        }

        if (!Schema.Table("Location").Column("ShopId").Exists())
        {
            Alter.Table("Location").AddColumn("ShopId").AsGuid().Nullable();
        }

        if (!Schema.Table("Location").Constraint("FK_Location_Shop").Exists())
        {
            Create.ForeignKey("FK_Location_Shop")
                .FromTable("Location").ForeignColumn("ShopId")
                .ToTable("Shop").PrimaryColumn("Id");
        }

        Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"UQ_Location_Shop_Code\" ON \"Location\" (\"ShopId\", upper(\"Code\"));");

        if (!Schema.Table("ShopUser").Exists())
        {
            Create.Table("ShopUser")
                .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
                .WithColumn("ShopId").AsGuid().NotNullable()
                .WithColumn("Login").AsString(128).NotNullable()
                .WithColumn("DisplayName").AsString(128).NotNullable()
                .WithColumn("IsAdmin").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("Secret_Hash").AsString(512).Nullable()
                .WithColumn("Disabled").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime);

            Create.ForeignKey("FK_ShopUser_Shop")
                .FromTable("ShopUser").ForeignColumn("ShopId")
                .ToTable("Shop").PrimaryColumn("Id");

            Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"UQ_ShopUser_Login\" ON \"ShopUser\" (\"ShopId\", lower(\"Login\"));");
        }

        if (!Schema.Table("CountingRun").Column("OwnerUserId").Exists())
        {
            Alter.Table("CountingRun").AddColumn("OwnerUserId").AsGuid().Nullable();
        }

        if (!Schema.Table("CountingRun").Constraint("FK_CountingRun_Owner").Exists())
        {
            Create.ForeignKey("FK_CountingRun_Owner")
                .FromTable("CountingRun").ForeignColumn("OwnerUserId")
                .ToTable("ShopUser").PrimaryColumn("Id");
        }
    }

    public override void Down()
    {
        if (Schema.Table("CountingRun").Constraint("FK_CountingRun_Owner").Exists())
        {
            Delete.ForeignKey("FK_CountingRun_Owner").OnTable("CountingRun");
        }

        if (Schema.Table("CountingRun").Column("OwnerUserId").Exists())
        {
            Delete.Column("OwnerUserId").FromTable("CountingRun");
        }

        if (Schema.Table("ShopUser").Exists())
        {
            Execute.Sql("DROP INDEX IF EXISTS \"UQ_ShopUser_Login\";");
            if (Schema.Table("ShopUser").Constraint("FK_ShopUser_Shop").Exists())
            {
                Delete.ForeignKey("FK_ShopUser_Shop").OnTable("ShopUser");
            }

            Delete.Table("ShopUser").IfExists();
        }

        Execute.Sql("DROP INDEX IF EXISTS \"UQ_Location_Shop_Code\";");

        if (Schema.Table("Location").Constraint("FK_Location_Shop").Exists())
        {
            Delete.ForeignKey("FK_Location_Shop").OnTable("Location");
        }

        if (Schema.Table("Location").Column("ShopId").Exists())
        {
            Delete.Column("ShopId").FromTable("Location");
        }

        Execute.Sql("DROP INDEX IF EXISTS \"UQ_Shop_LowerName\";");

        if (Schema.Table("Shop").Exists())
        {
            Delete.Table("Shop").IfExists();
        }
    }
}
