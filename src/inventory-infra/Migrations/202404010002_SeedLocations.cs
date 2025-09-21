using System.Collections.Generic;
using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202404010002, "Seed initial Location data: B1..B20, S1..S19")]
public sealed class SeedLocations : Migration
{
    private static readonly IReadOnlyCollection<string> LocationCodes = BuildLocationCodes();

    public override void Up()
    {
        EnsureLocationCodeUniqueIndex();

        foreach (var code in LocationCodes)
        {
            InsertLocationIfMissing(code);
        }
    }

    public override void Down()
    {
        Execute.Sql("DELETE FROM \"Location\" WHERE \"Code\" LIKE 'B%' OR \"Code\" LIKE 'S%';");
    }

    private static IReadOnlyCollection<string> BuildLocationCodes()
    {
        var codes = new List<string>(39);

        for (var i = 1; i <= 20; i++)
        {
            codes.Add($"B{i}");
        }

        for (var i = 1; i <= 19; i++)
        {
            codes.Add($"S{i}");
        }

        return codes;
    }

    private void InsertLocationIfMissing(string code)
    {
        var label = $"Zone {code}";
        var escapedCode = EscapeSqlLiteral(code);
        var escapedLabel = EscapeSqlLiteral(label);

        var sql = $@"INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\")
SELECT uuid_generate_v4(), '{escapedCode}', '{escapedLabel}'
WHERE NOT EXISTS (SELECT 1 FROM \"Location\" WHERE \"Code\" = '{escapedCode}');";

        Execute.Sql(sql);
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private void EnsureLocationCodeUniqueIndex()
    {
        Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Location_Code\" ON \"public\".\"Location\" (\"Code\" ASC);");
    }
}
