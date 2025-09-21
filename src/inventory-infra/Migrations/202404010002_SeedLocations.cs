using System;
using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations
{
    [Migration(202404010002)]
    public class Migration_202404010002_SeedLocations : Migration
    {
        public override void Up()
        {
            // 1) Sécuriser l’unicité du Code (idempotent)
            Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Location_Code""
ON ""public"".""Location"" (""Code"");
");

            // 2) Générer les codes à insérer
            //   - B1..B20
            //   - S1..S19
            var bCodes = new string[20];
            for (int i = 1; i <= 20; i++)
                bCodes[i - 1] = $"B{i}";

            var sCodes = new string[19];
            for (int i = 1; i <= 19; i++)
                sCodes[i - 1] = $"S{i}";

            // 3) Insérer de façon idempotente (WHERE NOT EXISTS)
            foreach (var code in bCodes)
                InsertLocationIfNotExists(code);

            foreach (var code in sCodes)
                InsertLocationIfNotExists(code);
        }

        public override void Down()
        {
            // Supprime uniquement ce que cette migration a ajouté
            Execute.Sql(@"
DELETE FROM ""public"".""Location""
WHERE ""Code"" LIKE 'B%' OR ""Code"" LIKE 'S%';
");
        }

        private void InsertLocationIfNotExists(string code)
        {
            // Label = "Zone {Code}"
            var label = $"Zone {code}";

            // NB: on utilise uuid_generate_v4() pour la colonne Id.
            //     Idempotence via WHERE NOT EXISTS.
            //     On échappe les guillemets correctement avec verbatim string et interpolation simple.
            var sql = $@"
INSERT INTO ""public"".""Location"" (""Id"", ""Code"", ""Label"")
SELECT uuid_generate_v4(), '{code}', '{label}'
WHERE NOT EXISTS (
    SELECT 1 FROM ""public"".""Location"" WHERE ""Code"" = '{code}'
);";

            Execute.Sql(sql);
        }
    }
}
