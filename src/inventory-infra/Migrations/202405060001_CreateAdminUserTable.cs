using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202405060001)]
public sealed class CreateAdminUserTable : Migration
{
    public override void Up()
    {
        if (!Schema.Table("AdminUser").Exists())
        {
            Create.Table("AdminUser")
                .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
                .WithColumn("Email").AsString(256).NotNullable()
                .WithColumn("DisplayName").AsString(128).NotNullable()
                .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable()
                .WithColumn("UpdatedAtUtc").AsDateTimeOffset().Nullable();
        }

        if (!Schema.Table("AdminUser").Index("IX_AdminUser_Email").Exists())
        {
            Create.Index("IX_AdminUser_Email")
                .OnTable("AdminUser")
                .WithOptions().Unique()
                .OnColumn("Email").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("AdminUser").IfExists();
    }
}
