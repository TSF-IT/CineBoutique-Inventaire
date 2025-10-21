using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(2025102301)]
public sealed class Ensure_PgExtensions : Migration
{
    public override void Up()
    {
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS unaccent;");
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS pg_trgm;");
    }

    public override void Down()
    {
        // No-op (on ne désactive pas en down)
    }
}
