using System;
using System.IO;
using System.Data;
using Advantage.Data.Provider;
using Xunit;
using System.Runtime.InteropServices;

// Disable parallel execution to avoid file locking issues with DBF files
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IQToolkit.Data.Advantage.Tests
{
    public static class TestSetup
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        public static string DataDir = Path.Combine(Path.GetTempPath(), "IQToolkit_Advantage_Tests");
        private static object _lock = new object();
        private static bool _libraryLoaded = false;

        public static void EnsureDatabase()
        {
            lock (_lock)
            {
                if (!_libraryLoaded)
                {
                    // Ensure the unmanaged ACE library is loaded.
                    // The provider needs this to communicate with Advantage Local Server.
                    string dllName = IntPtr.Size == 8 ? "ace64.dll" : "ace32.dll";
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);
                    
                    if (File.Exists(path))
                    {
                        IntPtr handle = LoadLibrary(path);
                        if (handle == IntPtr.Zero)
                        {
                            // If failed to load by full path, try just the filename (standard search path)
                            LoadLibrary(dllName);
                        }
                    }
                    else
                    {
                         LoadLibrary(dllName);
                    }
                    _libraryLoaded = true;
                }

                // Always recreate to ensure clean state for each test class

                if (Directory.Exists(DataDir))
                {
                    // Clean up previous run
                    try { Directory.Delete(DataDir, true); } catch { }
                }
                Directory.CreateDirectory(DataDir);

                // Connection string for creating tables
                // TableType=CDX for DBF/CDX support
                // Pooling=False to ensure files are released
                string connString = $"Data Source={DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";

                using (var conn = new AdsConnection(connString))
                {
                    conn.Open();
                    
                    using (var cmd = conn.CreateCommand())
                    {
                        // Create TestTable
                        cmd.CommandText = @"
                            CREATE TABLE TestTable (
                                Id Integer,
                                Name Char(50),
                                Value Double,
                                DateCol Date,
                                TimeCol Char(5)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        // Insert some data
                        // Row 1: 2023-01-01 10:00
                        cmd.CommandText = "INSERT INTO TestTable (Id, Name, Value, DateCol, TimeCol) VALUES (1, 'Alpha', 10.5, '2023-01-01', '10:00')";
                        cmd.ExecuteNonQuery();
                        // Row 2: 2023-01-01 14:30
                        cmd.CommandText = "INSERT INTO TestTable (Id, Name, Value, DateCol, TimeCol) VALUES (2, 'Beta', 20.0, '2023-01-01', '14:30')";
                        cmd.ExecuteNonQuery();
                        // Row 3: 2023-01-02 09:15
                        cmd.CommandText = "INSERT INTO TestTable (Id, Name, Value, DateCol, TimeCol) VALUES (3, 'Gamma', 30.5, '2023-01-02', '09:15')";
                        cmd.ExecuteNonQuery();
                        // Row 4: NULL Date/Time
                        cmd.CommandText = "INSERT INTO TestTable (Id, Name, Value, DateCol, TimeCol) VALUES (4, 'Delta', 40.0, NULL, NULL)";
                        cmd.ExecuteNonQuery();

                        // Create Customers Table
                        cmd.CommandText = @"
                            CREATE TABLE Customers (
                                CustomerId Integer,
                                Name Char(20),
                                City Char(20)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "INSERT INTO Customers (CustomerId, Name, City) VALUES (1, 'Alice', 'London')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO Customers (CustomerId, Name, City) VALUES (2, 'Bob', 'Paris')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO Customers (CustomerId, Name, City) VALUES (3, 'Charlie', 'London')";
                        cmd.ExecuteNonQuery();

                        // Create Orders Table
                        cmd.CommandText = @"
                            CREATE TABLE Orders (
                                OrderId Integer,
                                CustomerId Integer,
                                OrderDate Date,
                                Total Numeric(10, 2)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "INSERT INTO Orders (OrderId, CustomerId, OrderDate, Total) VALUES (101, 1, '2023-01-01', 100.00)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO Orders (OrderId, CustomerId, OrderDate, Total) VALUES (102, 1, '2023-02-01', 200.00)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO Orders (OrderId, CustomerId, OrderDate, Total) VALUES (103, 2, '2023-01-15', 150.00)";
                        cmd.ExecuteNonQuery();

                        // Create CompositeParents Table
                        cmd.CommandText = @"
                            CREATE TABLE CompositeParents (
                                KeyA Integer,
                                KeyB Integer,
                                Name Char(20)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "INSERT INTO CompositeParents (KeyA, KeyB, Name) VALUES (1, 10, 'Parent1')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO CompositeParents (KeyA, KeyB, Name) VALUES (1, 20, 'Parent2')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO CompositeParents (KeyA, KeyB, Name) VALUES (2, 10, 'Parent3')";
                        cmd.ExecuteNonQuery();

                        // Create CompositeChildren Table
                        cmd.CommandText = @"
                            CREATE TABLE CompositeChildren (
                                ChildId Integer,
                                ParentKeyA Integer,
                                ParentKeyB Integer,
                                Data Char(20)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        // Children for Parent1 (1, 10)
                        cmd.CommandText = "INSERT INTO CompositeChildren (ChildId, ParentKeyA, ParentKeyB, Data) VALUES (1, 1, 10, 'Child1_P1')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO CompositeChildren (ChildId, ParentKeyA, ParentKeyB, Data) VALUES (2, 1, 10, 'Child2_P1')";
                        cmd.ExecuteNonQuery();

                        // Children for Parent2 (1, 20)
                        cmd.CommandText = "INSERT INTO CompositeChildren (ChildId, ParentKeyA, ParentKeyB, Data) VALUES (3, 1, 20, 'Child1_P2')";
                        cmd.ExecuteNonQuery();

                        // Create CharDateTimeTable — DTMAJ_RAW is CHAR(12) storing "yyyyMMddHHmm"
                        cmd.CommandText = @"
                            CREATE TABLE CharDateTimeTable (
                                Id Integer,
                                Label Char(50),
                                DTMAJ_RAW Char(12)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        // Row 1: 2023-06-15 10:30
                        cmd.CommandText = "INSERT INTO CharDateTimeTable (Id, Label, DTMAJ_RAW) VALUES (1, 'Alpha', '202306151030')";
                        cmd.ExecuteNonQuery();
                        // Row 2: 2023-06-15 14:45
                        cmd.CommandText = "INSERT INTO CharDateTimeTable (Id, Label, DTMAJ_RAW) VALUES (2, 'Beta', '202306151445')";
                        cmd.ExecuteNonQuery();
                        // Row 3: 2023-06-16 09:00
                        cmd.CommandText = "INSERT INTO CharDateTimeTable (Id, Label, DTMAJ_RAW) VALUES (3, 'Gamma', '202306160900')";
                        cmd.ExecuteNonQuery();
                        // Row 4: NULL date/time
                        cmd.CommandText = "INSERT INTO CharDateTimeTable (Id, Label, DTMAJ_RAW) VALUES (4, 'Delta', NULL)";
                        cmd.ExecuteNonQuery();

                        // AssocParents / AssocCodes — CHAR FK that may be blank.
                        // ADS CHAR compare treats blank parent FK as equal to blank CODIF,
                        // which would multiply a 1:1 LEFT JOIN without a non-empty key predicate.
                        cmd.CommandText = @"
                            CREATE TABLE AssocParents (
                                ParentId Integer,
                                LibLang Char(10),
                                Devise Char(10),
                                NumLoc Char(20)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "INSERT INTO AssocParents (ParentId, LibLang, Devise, NumLoc) VALUES (1, 'FR', 'EUR', '226060778')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocParents (ParentId, LibLang, Devise, NumLoc) VALUES (2, '', '', '226060779')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocParents (ParentId, LibLang, Devise, NumLoc) VALUES (3, 'FR', '', '226060780')";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = @"
                            CREATE TABLE AssocCodes (
                                Id Integer,
                                Code Char(10),
                                Type Char(10),
                                Libelle Char(30)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "INSERT INTO AssocCodes (Id, Code, Type, Libelle) VALUES (1, 'FR', 'PAYS', 'France')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocCodes (Id, Code, Type, Libelle) VALUES (2, 'BE', 'PAYS', 'Belgique')";
                        cmd.ExecuteNonQuery();

                        string[] blankPays = { "France", "Belgique", "Italie", "Espagne", "Allemagne", "Suisse", "Portugal", "Pays-Bas", "Luxembourg", "Autriche", "Pologne" };
                        for (int i = 0; i < blankPays.Length; i++)
                        {
                            cmd.CommandText = $"INSERT INTO AssocCodes (Id, Code, Type, Libelle) VALUES ({3 + i}, '', 'PAYS', '{blankPays[i]}')";
                            cmd.ExecuteNonQuery();
                        }

                        cmd.CommandText = "INSERT INTO AssocCodes (Id, Code, Type, Libelle) VALUES (14, 'EUR', 'DEVISE', 'Euro')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocCodes (Id, Code, Type, Libelle) VALUES (15, 'USD', 'DEVISE', 'Dollar')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocCodes (Id, Code, Type, Libelle) VALUES (16, '', 'DEVISE', 'Franc')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocCodes (Id, Code, Type, Libelle) VALUES (17, '', 'DEVISE', 'Livre')";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = @"
                            CREATE TABLE AssocDetails (
                                DetailId Integer,
                                NumLoc Char(20),
                                Label Char(30)
                            )
                        ";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "INSERT INTO AssocDetails (DetailId, NumLoc, Label) VALUES (1, '226060778', 'Line1')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocDetails (DetailId, NumLoc, Label) VALUES (2, '226060778', 'Line2')";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO AssocDetails (DetailId, NumLoc, Label) VALUES (3, '226060779', 'Line3')";
                        cmd.ExecuteNonQuery();
                    }
                }
                // _initialized = true;
            }
        }
    }
}
