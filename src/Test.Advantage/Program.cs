using System;
using System.Data.Common;
using System.Linq;
using IQToolkit.Data.Advantage;
using System.Collections.Generic;

namespace Test.Advantage
{
    public class Country
    {
        public int id { get; set; }
        public string name { get; set; }
    }

    public class Person
    {
        public int id { get; set; }
        public string name { get; set; }
        public int countryId { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // Use local server connection for Advantage with TableType=CDX
            string connectionString = "Data Source=C:\\ADSData\\MyData;ServerType=remote;User ID=adssys;Password=;";
            try
            {
                var provider = new AdvantageQueryProvider(connectionString);
                Console.WriteLine("Connection to local ADS server created successfully.");

                // Enable SQL logging to console
                //provider.Log = Console.Out;

                var conn = provider.Connection;
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    // Create Country table
                    cmd.CommandText = @"CREATE TABLE Country (
                        id Integer,
                        name VARCHAR(100)
                    )";
                    try { cmd.ExecuteNonQuery(); Console.WriteLine("Table 'Country' created."); }
                    catch (Exception ex) { if (ex.Message.Contains("already exists")) Console.WriteLine("Table 'Country' already exists."); else throw; }

                    // Create unique index on Country.id (index name max 10 chars for dBASE/Advantage)
                    try
                    {
                        cmd.CommandText = "CREATE UNIQUE INDEX ctryid_idx ON Country (id)";
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Unique index 'ctryid_idx' on 'Country.id' created.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("already exists"))
                            Console.WriteLine("Index 'ctryid_idx' already exists.");
                        else
                            throw;
                    }

                    // Create Person table
                    cmd.CommandText = @"CREATE TABLE Person (
                        id Integer,
                        name VARCHAR(100),
                        countryId Integer
                    )";
                    try { cmd.ExecuteNonQuery(); Console.WriteLine("Table 'Person' created."); }
                    catch (Exception ex) { if (ex.Message.Contains("already exists")) Console.WriteLine("Table 'Person' already exists."); else throw; }

                    // Create unique index on Person.id (index name max 10 chars)
                    try
                    {
                        cmd.CommandText = "CREATE UNIQUE INDEX prsnid_idx ON Person (id)";
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Unique index 'prsnid_idx' on 'Person.id' created.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("already exists"))
                            Console.WriteLine("Index 'prsnid_idx' already exists.");
                        else
                            throw;
                    }

                    // Create non-unique index on Person.name for ORDER BY support
                    try
                    {
                        cmd.CommandText = "CREATE INDEX prsnname ON Person (name)";
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Index 'prsnname' on 'Person.name' created.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("already exists"))
                            Console.WriteLine("Index 'prsnname' already exists.");
                        else
                            throw;
                    }

                    // Create non-unique index on Country.name for ORDER BY support
                    try
                    {
                        cmd.CommandText = "CREATE INDEX ctryname ON Country (name)";
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Index 'ctryname' on 'Country.name' created.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("already exists"))
                            Console.WriteLine("Index 'ctryname' already exists.");
                        else
                            throw;
                    }

                    // Create composite index on Person (name, id) for ORDER BY support (index name max 10 chars)
                    try
                    {
                        cmd.CommandText = "CREATE INDEX prsn_n_id ON Person (name, id)";
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Index 'prsn_n_id' on 'Person(name, id)' created.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("already exists"))
                            Console.WriteLine("Index 'prsn_n_id' already exists.");
                        else
                            throw;
                    }
                }
                conn.Close();

                // Insert European countries if not already present
                var countryTable = provider.GetTable<Country>();
                var europeanCountries = new List<Country>
                {
                    new Country { id = 1, name = "France" },
                    new Country { id = 2, name = "Germany" },
                    new Country { id = 3, name = "Italy" },
                    new Country { id = 4, name = "Spain" },
                    new Country { id = 5, name = "United Kingdom" },
                    new Country { id = 6, name = "Netherlands" },
                    new Country { id = 7, name = "Belgium" },
                    new Country { id = 8, name = "Switzerland" },
                    new Country { id = 9, name = "Austria" },
                    new Country { id = 10, name = "Sweden" },
                    new Country { id = 11, name = "Norway" },
                    new Country { id = 12, name = "Denmark" },
                    new Country { id = 13, name = "Finland" },
                    new Country { id = 14, name = "Portugal" },
                    new Country { id = 15, name = "Greece" },
                    new Country { id = 16, name = "Ireland" },
                    new Country { id = 17, name = "Poland" },
                    new Country { id = 18, name = "Czech Republic" },
                    new Country { id = 19, name = "Hungary" },
                    new Country { id = 20, name = "Slovakia" }
                };
                foreach (var c in europeanCountries)
                {
                    if (!countryTable.Any(x => x.id == c.id))
                        countryTable.Insert(c);
                }
                Console.WriteLine("Inserted European countries into Country table (if not already present).\n");

                // Insert 3000 random persons using IEntityTable<Person> with unique ids
                var personTable = provider.GetTable<Person>();
                var rand = new Random();
                var countryIds = countryTable.Select(c => c.id).ToList();
                var usedIds = new HashSet<int>();
                int totalToInsert = 3000;
                int inserted = 0;
                while (inserted < totalToInsert)
                {
                    int newId;
                    do
                    {
                        newId = rand.Next(100000, 999999);
                    } while (!usedIds.Add(newId)); // Only add if unique

                    var person = new Person
                    {
                        id = newId,
                        name = "Name" + rand.Next(100000, 999999),
                        countryId = countryIds[rand.Next(countryIds.Count)]
                    };
                    personTable.Insert(person);
                    inserted++;
                }
                Console.WriteLine($"Inserted {totalToInsert} random persons with unique ids using IEntityTable<Person>.\n");

                // Query how many rows there are
                int count = personTable.Count();
                Console.WriteLine($"Person table row count: {count}");

                // Display all persons
                Console.WriteLine("All persons:");
                foreach (var row in personTable)
                {
                    Console.WriteLine($"id={row.id}, name={row.name}, countryId={row.countryId}");
                }

                // Query all persons living in Switzerland with a single LINQ query
                var swissPersons = from p in personTable
                                   join c in countryTable on p.countryId equals c.id
                                   where c.name == "Switzerland"
                                   select p;
                Console.WriteLine("\nPersons living in Switzerland:");
                foreach (var p in swissPersons)
                {
                    Console.WriteLine($"id={p.id}, name={p.name}, countryId={p.countryId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect, create table, or insert/query: {ex.Message}");
            }
        }
    }
}
