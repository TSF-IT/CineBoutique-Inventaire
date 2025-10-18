using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations
{
  [Migration(2025101903)]
  public sealed class _20251019_03_SetDefaultTimestampsOnProduct : Migration
  {
    public override void Up()
    {
      // Définir des DEFAULTs pour éviter les 23502 lors des INSERT sans timestamps
      Execute.Sql(@"ALTER TABLE ""Product""
        ALTER COLUMN ""CreatedAtUtc"" SET DEFAULT (NOW() AT TIME ZONE 'UTC');");
      Execute.Sql(@"ALTER TABLE ""Product""
        ALTER COLUMN ""UpdatedAtUtc"" SET DEFAULT (NOW() AT TIME ZONE 'UTC');");

      // Sécurité : corriger d'éventuelles lignes nulles si la table contient déjà des données
      Execute.Sql(@"UPDATE ""Product""
        SET ""CreatedAtUtc"" = NOW() AT TIME ZONE 'UTC'
        WHERE ""CreatedAtUtc"" IS NULL;");
      Execute.Sql(@"UPDATE ""Product""
        SET ""UpdatedAtUtc"" = NOW() AT TIME ZONE 'UTC'
        WHERE ""UpdatedAtUtc"" IS NULL;");
    }

    public override void Down()
    {
      // Revenir à l'état sans DEFAULT (les colonnes restent NOT NULL)
      Execute.Sql(@"ALTER TABLE ""Product"" ALTER COLUMN ""CreatedAtUtc"" DROP DEFAULT;");
      Execute.Sql(@"ALTER TABLE ""Product"" ALTER COLUMN ""UpdatedAtUtc"" DROP DEFAULT;");
    }
  }
}
