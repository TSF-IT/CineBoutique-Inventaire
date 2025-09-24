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
                .WithColumn("id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
                .WithColumn("ts").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime)
                .WithColumn("user").AsString(256).Nullable()
                .WithColumn("action").AsString(128).Nullable()
                .WithColumn("details").AsCustom("text").NotNullable();
        }

        if (!Schema.Table(TableName).Index("ix_audit_logs_ts").Exists())
        {
            Create.Index("ix_audit_logs_ts").OnTable(TableName)
                .OnColumn("ts").Descending();
        }

        if (!Schema.Table(TableName).Index("ix_audit_logs_user").Exists())
        {
            Create.Index("ix_audit_logs_user").OnTable(TableName)
                .OnColumn("user").Ascending();
        }

        if (!Schema.Table(TableName).Index("ix_audit_logs_action").Exists())
        {
            Create.Index("ix_audit_logs_action").OnTable(TableName)
                .OnColumn("action").Ascending();
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
