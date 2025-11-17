using FirebirdSql.Data.FirebirdClient;
using System;
using System.IO;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"

        public static int Main(string[] args)
        {
            // --- Default connection that works ---
            //var connectionString = new FbConnectionStringBuilder
            //{
            //    Database = @"C:\db\fb5\DATABASE.FDB",
            //    ServerType = FbServerType.Default,  // 0
            //    UserID = "SYSDBA",
            //    Password = "masterkey",
            //    DataSource = "localhost",           // nazwa hosta / IP serwera
            //    Port = 3050,                        // domyślny port Firebird
            //    ClientLibrary = "fbclient.dll"
            //}.ToString();
            //Console.WriteLine(connectionString);


            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            BuildDatabase(dbDir, scriptsDir);
                            //Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                            return 0;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            ExportScripts(connStr, outputDir);
                            //Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            //Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }

        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {
            try
            {
                if (!Directory.Exists(databaseDirectory))
                    Directory.CreateDirectory(databaseDirectory);

                string databasePath = Path.Combine(databaseDirectory, "database.fdb");

                var connectionString = new FbConnectionStringBuilder
                {
                    Database = databasePath,
                    ServerType = FbServerType.Default,
                    UserID = "SYSDBA",
                    Password = "masterkey",
                    DataSource = "localhost",
                    Port = 3050,
                    ClientLibrary = "fbclient.dll"
                }.ToString();

                // Create
                if (!File.Exists(databasePath))
                {
                    FbConnection.CreateDatabase(connectionString);
                }
                else
                {
                    Console.WriteLine("Database allready exists");
                }

                // Load meta
                var meta = MetadataLoader.LoadMetaData(scriptsDirectory);
                if (meta != null)
                {
                    var domainsSql = MetadataLoader.GenerateDomains(meta.Domains);
                    var tablesSql = MetadataLoader.GenerateTables(meta.Tables);
                    var procSql = MetadataLoader.GenerateProcedures(meta.Procedures);

                    using var conn = new FbConnection(connectionString);
                    conn.Open();
                    ExecuteList(domainsSql, conn);
                    ExecuteList(tablesSql, conn);
                    ExecuteList(procSql, conn);

                    Console.WriteLine("Loaded metadata");
                }
                else
                {
                    Console.WriteLine("Metadata not found");
                }

                Console.WriteLine("The database has been built successfully.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            try
            {
                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                MetadataExporter.Save(connectionString, outputDirectory);
                Console.WriteLine("Metadata export ended successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            try
            {
                var meta = MetadataLoader.LoadMetaData(scriptsDirectory);
                if (meta != null)
                {
                    var domainsSql = MetadataLoader.GenerateDomains(meta.Domains);
                    var tablesSql = MetadataLoader.GenerateTables(meta.Tables);
                    var procSql = MetadataLoader.GenerateProcedures(meta.Procedures);

                    using var conn = new FbConnection(connectionString);
                    conn.Open();
                    ExecuteList(domainsSql, conn);
                    ExecuteList(tablesSql, conn);
                    ExecuteList(procSql, conn);

                    Console.WriteLine("Updated metadata");
                }
                else
                {
                    Console.WriteLine("Metadata not found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }


        private static void ExecuteList(IEnumerable<string> commands, FbConnection conn)
        {
            foreach (var sql in commands)
            {
                try
                {
                    using var cmd = new FbCommand(sql, conn);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
            }
        }
    }
}
