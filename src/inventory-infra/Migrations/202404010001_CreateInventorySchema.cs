using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202404010001)]
public sealed class CreateInventorySchema : Migration
{
    public override void Up()
    {
        Create.Table("Product")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("Sku").AsString(32).NotNullable()
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("Ean").AsString(13).Nullable()
            .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable();

        if (!Schema.Table("Product").Index("IX_Product_Sku").Exists())
        {
            Create.Index("IX_Product_Sku").OnTable("Product").WithOptions().Unique()
                .OnColumn("Sku").Ascending();
        }

        if (!Schema.Table("Product").Index("IX_Product_Ean").Exists())
        {
            Create.Index("IX_Product_Ean").OnTable("Product").WithOptions().Unique()
                .OnColumn("Ean").Ascending();
        }

        Create.Table("Location")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("Code").AsString(32).NotNullable().Unique()
            .WithColumn("Label").AsString(128).NotNullable();

        if (!Schema.Table("Location").Index("IX_Location_Code").Exists())
        {
            Create.Index("IX_Location_Code").OnTable("Location").WithOptions().Unique()
                .OnColumn("Code").Ascending();
        }

        Create.Table("InventorySession")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("StartedAtUtc").AsDateTimeOffset().NotNullable()
            .WithColumn("CompletedAtUtc").AsDateTimeOffset().Nullable();

        Create.Table("CountingRun")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("InventorySessionId").AsGuid().NotNullable()
            .WithColumn("LocationId").AsGuid().NotNullable()
            .WithColumn("StartedAtUtc").AsDateTimeOffset().NotNullable()
            .WithColumn("CompletedAtUtc").AsDateTimeOffset().Nullable();

        if (!Schema.Table("CountingRun").Constraint("FK_CountingRun_InventorySession").Exists())
        {
            Create.ForeignKey("FK_CountingRun_InventorySession")
                .FromTable("CountingRun").ForeignColumn("InventorySessionId")
                .ToTable("InventorySession").PrimaryColumn("Id");
        }

        if (!Schema.Table("CountingRun").Constraint("FK_CountingRun_Location").Exists())
        {
            Create.ForeignKey("FK_CountingRun_Location")
                .FromTable("CountingRun").ForeignColumn("LocationId")
                .ToTable("Location").PrimaryColumn("Id");
        }

        Create.Table("CountLine")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("CountingRunId").AsGuid().NotNullable()
            .WithColumn("ProductId").AsGuid().NotNullable()
            .WithColumn("Quantity").AsDecimal(18, 3).NotNullable()
            .WithColumn("CountedAtUtc").AsDateTimeOffset().NotNullable();

        if (!Schema.Table("CountLine").Constraint("FK_CountLine_CountingRun").Exists())
        {
            Create.ForeignKey("FK_CountLine_CountingRun")
                .FromTable("CountLine").ForeignColumn("CountingRunId")
                .ToTable("CountingRun").PrimaryColumn("Id");
        }

        if (!Schema.Table("CountLine").Constraint("FK_CountLine_Product").Exists())
        {
            Create.ForeignKey("FK_CountLine_Product")
                .FromTable("CountLine").ForeignColumn("ProductId")
                .ToTable("Product").PrimaryColumn("Id");
        }

        Create.Table("Conflict")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("CountLineId").AsGuid().NotNullable()
            .WithColumn("Status").AsString(64).NotNullable()
            .WithColumn("Notes").AsString(1024).Nullable()
            .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable()
            .WithColumn("ResolvedAtUtc").AsDateTimeOffset().Nullable();

        if (!Schema.Table("Conflict").Constraint("FK_Conflict_CountLine").Exists())
        {
            Create.ForeignKey("FK_Conflict_CountLine")
                .FromTable("Conflict").ForeignColumn("CountLineId")
                .ToTable("CountLine").PrimaryColumn("Id");
        }

        Create.Table("Audit")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("EntityName").AsString(256).NotNullable()
            .WithColumn("EntityId").AsString(128).NotNullable()
            .WithColumn("EventType").AsString(64).NotNullable()
            .WithColumn("Payload").AsCustom("jsonb").Nullable()
            .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable();

        if (!Schema.Table("Audit").Index("IX_Audit_Entity").Exists())
        {
            Create.Index("IX_Audit_Entity").OnTable("Audit")
                .OnColumn("EntityName").Ascending()
                .OnColumn("EntityId").Ascending();
        }
    }

    public override void Down()
    {
        Delete.Table("Audit").IfExists();
        Delete.Table("Conflict").IfExists();
        Delete.Table("CountLine").IfExists();
        Delete.Table("CountingRun").IfExists();
        Delete.Table("InventorySession").IfExists();
        Delete.Table("Location").IfExists();
        Delete.Table("Product").IfExists();
    }
}
