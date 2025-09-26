using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202509260001)]
public sealed class SeedDefaultAdminUser : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
INSERT INTO admin_users (id, email, display_name, created_at, updated_at)
SELECT gen_random_uuid(), 'admin@local', 'admin', CURRENT_TIMESTAMP AT TIME ZONE 'UTC', NULL
WHERE NOT EXISTS (SELECT 1 FROM admin_users WHERE email = 'admin@local');
");
    }

    public override void Down()
    {
        Execute.Sql(@"DELETE FROM admin_users WHERE email = 'admin@local';");
    }
}
