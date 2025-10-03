using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202411010001)]
public sealed class AddCountingRunOwnerUser : Migration
{
    private const string CountingRunTable = "CountingRun";
    private const string ShopUserTable = "ShopUser";
    private const string OwnerUserColumn = "OwnerUserId";
    private const string ForeignKeyName = "FK_CountingRun_OwnerUser";

    public override void Up()
    {
        if (!Schema.Table(CountingRunTable).Column(OwnerUserColumn).Exists())
        {
            Alter.Table(CountingRunTable)
                .AddColumn(OwnerUserColumn)
                .AsGuid()
                .Nullable();
        }

        if (!Schema.Table(CountingRunTable).Constraint(ForeignKeyName).Exists())
        {
            Create.ForeignKey(ForeignKeyName)
                .FromTable(CountingRunTable).ForeignColumn(OwnerUserColumn)
                .ToTable(ShopUserTable).PrimaryColumn("Id");
        }

        Execute.Sql(
            """
WITH matched_users AS (
    SELECT
        cr."Id"              AS "CountingRunId",
        su."Id"              AS "OwnerUserId"
    FROM "CountingRun" cr
    JOIN "Location" l ON l."Id" = cr."LocationId"
    JOIN "ShopUser" su ON su."ShopId" = l."ShopId"
    WHERE cr."OwnerUserId" IS NULL
      AND cr."OperatorDisplayName" IS NOT NULL
      AND su."DisplayName" IS NOT NULL
      AND LOWER(BTRIM(cr."OperatorDisplayName")) = LOWER(BTRIM(su."DisplayName"))
), unique_matches AS (
    SELECT mu."CountingRunId", mu."OwnerUserId"
    FROM matched_users mu
    JOIN (
        SELECT "CountingRunId", COUNT(*) AS "MatchCount"
        FROM matched_users
        GROUP BY "CountingRunId"
    ) counts ON counts."CountingRunId" = mu."CountingRunId"
    WHERE counts."MatchCount" = 1
)
UPDATE "CountingRun" cr
SET "OwnerUserId" = um."OwnerUserId"
FROM unique_matches um
WHERE cr."Id" = um."CountingRunId";
""");
    }

    public override void Down()
    {
        if (Schema.Table(CountingRunTable).Constraint(ForeignKeyName).Exists())
        {
            Delete.ForeignKey(ForeignKeyName).OnTable(CountingRunTable);
        }

        if (Schema.Table(CountingRunTable).Column(OwnerUserColumn).Exists())
        {
            Delete.Column(OwnerUserColumn).FromTable(CountingRunTable);
        }
    }
}
