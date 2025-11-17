using FirebirdSql.Data.FirebirdClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DbMetaTool
{
    public static class MetadataLoader
    {
        public static Metadata LoadMetaData(string jsonPath)
        {
            if (!Directory.Exists(jsonPath))
                return null;

            var path = Path.Combine(jsonPath, "databaseMeta.json");
            string json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<Metadata>(json);
        }

        public static List<string> GenerateDomains(IEnumerable<DomainMeta> domains)
        {
            return domains
                .Where(d => !d.name.StartsWith("RDB$"))   // ignore system domains
                .Select(d =>
                {
                    string sql = $@"
                    EXECUTE BLOCK AS
                    BEGIN
                      IF (NOT EXISTS(SELECT 1 FROM RDB$FIELDS WHERE RDB$FIELD_NAME = '{d.name}')) THEN
                      BEGIN
                        EXECUTE STATEMENT 'CREATE DOMAIN {d.name} AS {d.type}" + (d.notNull ? " NOT NULL" : "") + @"';
                      END
                    END;";
                    return sql.Trim();
                }).ToList();
        }

        public static List<string> GenerateTables(IEnumerable<TableMeta> tables)
        {
            var list = new List<string>();

            foreach (var t in tables)
            {
                // Generate column definitions
                var cols = t.columns.Select(c =>
                    $"    {c.name} {c.type}" +
                    (c.defaultValue != null ? $" DEFAULT {c.defaultValue}" : "") +
                    (c.notNull ? " NOT NULL" : "")
                );

                // Join columns into table definition
                string tableDef = string.Join(",\n", cols);

                // Wrap in EXECUTE BLOCK to check if table exists
                string sql = $@"
                    EXECUTE BLOCK AS
                    BEGIN
                      IF (NOT EXISTS(SELECT 1 FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = '{t.name}')) THEN
                      BEGIN
                        EXECUTE STATEMENT 'CREATE TABLE {t.name} ({tableDef})';
                      END
                    END;";
                list.Add(sql.Trim());
            }

            return list;
        }

        public static List<string> GenerateProcedures(IEnumerable<ProcedureMeta> procs)
        {
            return procs.Select(p =>
            {
                string source = p.source?.Trim() ?? "";

                // Remove any leftover '^' characters from ISQL exports
                source = source.Replace("^", "");

                // Ensure the source ends with a semicolon
                if (!source.EndsWith(";"))
                    source += ";";

                // Build the final CREATE OR ALTER PROCEDURE SQL
                string sql = $"CREATE OR ALTER PROCEDURE {p.name} \n{source}";

                return sql;
            }).ToList();
        }
    }
}
