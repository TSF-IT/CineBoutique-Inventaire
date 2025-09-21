using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202404010002, "Seed initial Location data: B1..B20, S1..S19")]
public sealed class SeedLocations : Migration
{
    public override void Up()
    {
        for (var i = 1; i <= 20; i++)
        {
            InsertLocationIfMissing($"B{i}");
        }

        for (var i = 1; i <= 19; i++)
        {
            InsertLocationIfMissing($"S{i}");
        }
    }

    public override void Down()
    {
        Execute.Sql("DELETE FROM \"Location\" WHERE \"Code\" LIKE 'B%' OR \"Code\" LIKE 'S%';");
    }

    private void InsertLocationIfMissing(string code)
    {
        var sql = $@"INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\")
SELECT uuid_generate_v4(), '{code}', 'Zone {code}'
WHERE NOT EXISTS (SELECT 1 FROM \"Location\" WHERE \"Code\" = '{code}');";
        Execute.Sql(sql);
    }
}
