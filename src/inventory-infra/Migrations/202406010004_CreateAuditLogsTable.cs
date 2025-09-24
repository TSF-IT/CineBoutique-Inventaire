using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202406010004)]
public sealed class CreateAuditLogsTable : Migration
{
    private const string TableName = "audit_logs";

    public override void Up()
    {
        if (!Schema.Table(TableName).Exists())
        {
            Create.Table(TableName)
                .WithColumn("id").AsInt64().PrimaryKey().Identity()
                .WithColumn("at").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime)
                .WithColumn("actor").AsString(320).Nullable()
                .WithColumn("message").AsCustom("text").NotNullable()
                .WithColumn("category").AsString(200).Nullable();
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
