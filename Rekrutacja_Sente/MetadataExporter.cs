using FirebirdSql.Data.FirebirdClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DbMetaTool
{
    public static class MetadataExporter
    {
        public static Metadata Save(string connectionString, string destination)
        {
            using var conn = new FbConnection(connectionString);
            conn.Open();

            // 1. Load metadata from DB
            var metadata = new Metadata
            {
                Domains = LoadDomains(conn),
                Tables = LoadTables(conn),
                Procedures = LoadProcedures(conn)
            };

            // 2. Serialize to JSON
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var path = Path.Combine(destination, "databaseMeta.json");

            // 3. Save to file
            if (File.Exists(path))
                File.Create(path);

            File.WriteAllText(path, json);

            Console.WriteLine($"Metadata saved to: {destination}");

            return metadata;
        }

        // -------------------------------------------------------------
        // DOMAIN LOADER
        // -------------------------------------------------------------
        private static List<DomainMeta> LoadDomains(FbConnection conn)
        {
            var list = new List<DomainMeta>();

            string sql = @"
            SELECT 
                TRIM(RDB$FIELD_NAME) AS NAME,
                RDB$FIELD_TYPE,
                RDB$FIELD_LENGTH,
                RDB$FIELD_SCALE,
                RDB$FIELD_SUB_TYPE,
                RDB$CHARACTER_LENGTH,
                RDB$DEFAULT_SOURCE,
                RDB$NULL_FLAG
            FROM RDB$FIELDS
            WHERE RDB$SYSTEM_FLAG = 0
            ORDER BY NAME";

            using var cmd = new FbCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new DomainMeta
                {
                    name = reader["NAME"].ToString(),
                    type = MapFbType(
                        GetInt(reader["RDB$FIELD_TYPE"]),
                        GetInt(reader["RDB$FIELD_SUB_TYPE"]),
                        GetInt(reader["RDB$FIELD_LENGTH"]),
                        GetInt(reader["RDB$CHARACTER_LENGTH"]),
                        GetInt(reader["RDB$FIELD_SCALE"])
                    ),
                    defaultValue = reader["RDB$DEFAULT_SOURCE"] == DBNull.Value ? null : reader["RDB$DEFAULT_SOURCE"].ToString(),
                    notNull = reader["RDB$NULL_FLAG"] != DBNull.Value
                });
            }

            return list;
        }

        // -------------------------------------------------------------
        // TABLE LOADER
        // -------------------------------------------------------------
        private static List<TableMeta> LoadTables(FbConnection conn)
        {
            var result = new List<TableMeta>();

            string sqlTables = @"
            SELECT TRIM(RDB$RELATION_NAME)
            FROM RDB$RELATIONS
            WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_BLR IS NULL
            ORDER BY 1";

            using var cmd = new FbCommand(sqlTables, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                string tableName = reader.GetString(0);
                var table = new TableMeta { name = tableName };

                table.columns = LoadColumns(conn, tableName);
                result.Add(table);
            }

            return result;
        }

        private static List<ColumnMeta> LoadColumns(FbConnection conn, string tableName)
        {
            var columns = new List<ColumnMeta>();

            string sql = $@"
            SELECT 
                TRIM(R.RDB$FIELD_NAME),
                F.RDB$FIELD_TYPE,
                F.RDB$FIELD_SUB_TYPE,
                F.RDB$FIELD_LENGTH,
                F.RDB$CHARACTER_LENGTH,
                F.RDB$FIELD_SCALE,
                R.RDB$DEFAULT_SOURCE,
                R.RDB$NULL_FLAG
            FROM RDB$RELATION_FIELDS R
            JOIN RDB$FIELDS F ON R.RDB$FIELD_SOURCE = F.RDB$FIELD_NAME
            WHERE R.RDB$RELATION_NAME = '{tableName}'
            ORDER BY R.RDB$FIELD_POSITION";

            using var cmd = new FbCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                columns.Add(new ColumnMeta
                {
                    name = reader.GetString(0),
                    type = MapFbType(
                        GetInt(reader["RDB$FIELD_TYPE"]),
                        GetInt(reader["RDB$FIELD_SUB_TYPE"]),
                        GetInt(reader["RDB$FIELD_LENGTH"]),
                        GetInt(reader["RDB$CHARACTER_LENGTH"]),
                        GetInt(reader["RDB$FIELD_SCALE"])
                    ),
                    defaultValue = reader["RDB$DEFAULT_SOURCE"] == DBNull.Value ? null : reader["RDB$DEFAULT_SOURCE"].ToString(),
                    notNull = reader["RDB$NULL_FLAG"] != DBNull.Value
                });
            }

            return columns;
        }

        // -------------------------------------------------------------
        // PROCEDURE LOADER
        // -------------------------------------------------------------
        private static List<ProcedureMeta> LoadProcedures(FbConnection conn)
        {
            var list = new List<ProcedureMeta>();

            string sql = @"
                SELECT 
                    TRIM(RDB$PROCEDURE_NAME) AS NAME,
                    RDB$PROCEDURE_SOURCE AS SRC
                FROM RDB$PROCEDURES
                WHERE RDB$SYSTEM_FLAG = 0
                ORDER BY 1";

            using var cmd = new FbCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            while (rdr.Read())
            {
                string name = rdr.GetString(0);
                string source = rdr["SRC"]?.ToString()?.Trim() ?? "";

                // Remove ISQL ^ terminators
                source = source.Replace("^", "");

                // =====================================================
                // 1. LOAD OUTPUT PARAMETERS
                // =====================================================
                var outParams = new List<(string name, string type)>();

                using (var cmdOut = new FbCommand(@"
                    SELECT 
                        TRIM(RDB$PARAMETER_NAME),
                        RDB$FIELD_SOURCE
                    FROM RDB$PROCEDURE_PARAMETERS
                    WHERE RDB$PROCEDURE_NAME = @P
                      AND RDB$PARAMETER_TYPE = 1  -- OUTPUT
                    ORDER BY RDB$PARAMETER_NUMBER", conn))
                {
                    cmdOut.Parameters.AddWithValue("P", name);

                    using var rOut = cmdOut.ExecuteReader();
                    while (rOut.Read())
                    {
                        string pName = rOut.GetString(0);
                        string pType = rOut.GetString(1);
                        outParams.Add((pName, pType));
                    }
                }

                // Build RETURNS (...) header
                string returnsHeader = "";
                if (outParams.Count > 0)
                {
                    string parts = string.Join(", ", outParams.Select(p => $"{p.name} {p.type}"));
                    returnsHeader = $"RETURNS ({parts})\n";
                }

                // =====================================================
                // 2. Detect local variables used in the source
                // =====================================================
                var variableMatches = System.Text.RegularExpressions.Regex.Matches(source, @":([A-Z0-9_]+)");
                var declaredVariables = new HashSet<string>();

                string varDecl = "";

                foreach (System.Text.RegularExpressions.Match match in variableMatches)
                {
                    string varName = match.Groups[1].Value;

                    // Do not declare OUTPUT parameters
                    if (outParams.Any(o => o.name.Equals(varName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (!declaredVariables.Contains(varName))
                    {
                        varDecl += $"DECLARE VARIABLE {varName} VARCHAR(100);\n";
                        declaredVariables.Add(varName);
                    }
                }

                // =====================================================
                // 3. Rebuild full procedure source
                // =====================================================
                string finalSource =
                    returnsHeader +
                    "AS\n" +
                    varDecl +
                    source;

                // Save result
                list.Add(new ProcedureMeta
                {
                    name = name,
                    source = finalSource
                });
            }

            return list;
        }

        // -------------------------------------------------------------
        // TYPE MAPPER
        // -------------------------------------------------------------
        private static string MapFbType(int type, int subType, int length, int charLen, int scale)
        {
            return type switch
            {
                7 => scale < 0 ? "DECIMAL" : "SMALLINT",
                8 => scale < 0 ? "NUMERIC" : "INTEGER",
                10 => "FLOAT",
                12 => "DATE",
                13 => "TIME",
                14 => $"CHAR({charLen})",
                16 => scale < 0 ? "BIGINT" : "BIGINT",
                37 => $"VARCHAR({charLen})",
                27 => "DOUBLE PRECISION",
                _ => "BLOB"
            };
        }

        private static int GetInt(object value)
        {
            return value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }
    }
}
