using System;
using System.IO;
using System.Data;
using Advantage.Data.Provider;
using Xunit;

// Disable parallel execution to avoid file locking issues with DBF files
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IQToolkit.Data.Advantage.Tests
{
    public static class TestSetup
    {
        public static string DataDir = Path.Combine(Path.GetTempPath(), "IQToolkit_Advantage_Tests");
        private static object _lock = new object();

        public static void EnsureDatabase()
        {
            lock (_lock)
            {
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
                    }
                }
                // _initialized = true;
            }
        }
    }
}
