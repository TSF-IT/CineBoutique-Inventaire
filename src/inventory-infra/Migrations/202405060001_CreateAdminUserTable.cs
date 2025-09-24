using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202405060001)]
public sealed class CreateAdminUserTable : Migration
{
    private const string TableName = "admin_users";
    private const string EmailIndexName = "ix_admin_users_email";

    public override void Up()
    {
        if (!Schema.Table(TableName).Exists())
        {
            Create.Table(TableName)
                .WithColumn("id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
                .WithColumn("email").AsString(320).NotNullable()
                .WithColumn("display_name").AsString(200).NotNullable()
                .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime)
                .WithColumn("updated_at").AsDateTimeOffset().Nullable();
        }

        if (!Schema.Table(TableName).Index(EmailIndexName).Exists())
        {
            Create.Index(EmailIndexName)
                .OnTable(TableName)
                .WithOptions().Unique()
                .OnColumn("email").Ascending();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Exists())
        {
            Delete.Table(TableName);
        }
    }
}
