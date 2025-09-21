using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202405010003)]
public sealed class EnhanceCountingRunStatus : Migration
{
    public override void Up()
    {
        if (!Schema.Table("CountingRun").Column("CountType").Exists())
        {
            Alter.Table("CountingRun")
                .AddColumn("CountType")
                .AsInt16()
                .NotNullable()
                .WithDefaultValue(1);
        }

        if (!Schema.Table("CountingRun").Column("OperatorDisplayName").Exists())
        {
            Alter.Table("CountingRun")
                .AddColumn("OperatorDisplayName")
                .AsString(128)
                .Nullable();
        }

        Execute.Sql("UPDATE \"CountingRun\" SET \"CountType\" = 1 WHERE \"CountType\" IS NULL;");

        Execute.Sql(
            "CREATE INDEX IF NOT EXISTS \"IX_CountingRun_Location_CountType_Open\" ON \"CountingRun\" (\"LocationId\", \"CountType\") WHERE \"CompletedAtUtc\" IS NULL;");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS \"IX_CountingRun_Location_CountType_Open\";");

        if (Schema.Table("CountingRun").Column("OperatorDisplayName").Exists())
        {
            Delete.Column("OperatorDisplayName").FromTable("CountingRun");
        }

        if (Schema.Table("CountingRun").Column("CountType").Exists())
        {
            Delete.Column("CountType").FromTable("CountingRun");
        }
    }
}
