using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using IQToolkit.Data.Advantage;
using Locasyst.Models;

namespace Locasyst
{
    class Program
    {
        static void Main(string[] args)
        {
                Console.WriteLine("Locasyst Navigation Property Test");
                Console.WriteLine("=================================");

                // Connection string - adjust as needed
                string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\DATA\\LYON;ServerType=remote;TableType=CDX";
                
                // Test with different record counts for better performance analysis
                int[] testSizes = { 5, 50, 100 };
            
            try
            {
                var provider = new AdvantageQueryProvider(connectionString);
                provider.Log = Console.Out; // Log generated SQL to the console

                // Performance test with different record counts
                Console.WriteLine("\nPERFORMANCE TEST WITH DIFFERENT RECORD COUNTS");
                Console.WriteLine("=============================================");
                
                foreach (int recordCount in testSizes)
                {
                    Console.WriteLine($"\n--- Testing with {recordCount} records ---");
                    
                    // Test 1: Navigation Property Projection
                    var navQuery = from locgen in provider.GetTable<Locgen>()
                                  select new
                                  {
                                      locgen.NUMLOC,
                                      ClientName = locgen.Client.NOM,
                                      locgen.DATEDEP,
                                      locgen.TOTALHT
                                  };

                    var sw1 = Stopwatch.StartNew();
                    var navResults = navQuery.Take(recordCount).ToList();
                    sw1.Stop();
                    
                    // Test 2: Explicit JOIN
                    var joinQuery = from locgen in provider.GetTable<Locgen>()
                                   join client in provider.GetTable<LocClt>() 
                                   on locgen.CODECLT equals client.CODECLT
                                   select new
                                   {
                                       locgen.NUMLOC,
                                       ClientName = client.NOM,
                                       locgen.DATEDEP,
                                       locgen.TOTALHT
                                   };

                    var sw2 = Stopwatch.StartNew();
                    var joinResults = joinQuery.Take(recordCount).ToList();
                    sw2.Stop();
                    
                    // Test 3: Full Entity Access
                    var fullQuery = from locgen in provider.GetTable<Locgen>()
                                   select new
                                   {
                                       locgen.NUMLOC,
                                       Client = locgen.Client,
                                       locgen.DATEDEP,
                                       locgen.TOTALHT
                                   };

                    var sw3 = Stopwatch.StartNew();
                    var fullResults = fullQuery.Take(recordCount).ToList();
                    sw3.Stop();
                    
                    Console.WriteLine($"Navigation Property: {sw1.ElapsedMilliseconds}ms");
                    Console.WriteLine($"Explicit JOIN:       {sw2.ElapsedMilliseconds}ms");
                    Console.WriteLine($"Full Entity:         {sw3.ElapsedMilliseconds}ms");
                    
                    // Show first result for verification
                    if (navResults.Count > 0)
                    {
                        Console.WriteLine($"Sample: {navResults[0].NUMLOC} - {navResults[0].ClientName}");
                    }
                }

                Console.WriteLine("\n1. Testing Navigation Property - Fetching Client Name Only");
                Console.WriteLine("----------------------------------------------------------");
                
                // Test 1: Using navigation property to get just the client name
                var locgenWithClientName = from locgen in provider.GetTable<Locgen>()
                                         select new
                                         {
                                             locgen.NUMLOC,
                                             ClientName = locgen.Client.NOM,  // This should only fetch NOM field
                                             locgen.DATEDEP,
                                             locgen.DATEFIN,
                                             locgen.STATUT,
                                             locgen.TOTALHT
                                         };

                Console.WriteLine("Query 1 - Navigation Property Projection:");
                Console.WriteLine("Expected: Should generate JOIN and SELECT only NOM field from LocClt");
                
                var stopwatch1 = Stopwatch.StartNew();
                var results1 = locgenWithClientName.Take(5).ToList();
                stopwatch1.Stop();
                
                Console.WriteLine($"Retrieved {results1.Count} records in {stopwatch1.ElapsedMilliseconds}ms");
                foreach (var result in results1)
                {
                    Console.WriteLine($"  {result.NUMLOC} - {result.ClientName} - {result.DATEDEP} - {result.TOTALHT}");
                }

                Console.WriteLine("\n2. Testing Explicit JOIN - Fetching Client Name Only");
                Console.WriteLine("----------------------------------------------------");
                
                // Test 2: Using explicit JOIN to get just the client name
                var locgenWithExplicitJoin = from locgen in provider.GetTable<Locgen>()
                                           join client in provider.GetTable<LocClt>() 
                                           on locgen.CODECLT equals client.CODECLT
                                           select new
                                           {
                                               locgen.NUMLOC,
                                               ClientName = client.NOM,  // Direct access to client.NOM
                                               locgen.DATEDEP,
                                               locgen.DATEFIN,
                                               locgen.STATUT,
                                               locgen.TOTALHT
                                           };

                Console.WriteLine("Query 2 - Explicit JOIN Projection:");
                Console.WriteLine("Expected: Should generate JOIN and SELECT only NOM field from LocClt");
                
                var stopwatch2 = Stopwatch.StartNew();
                var results2 = locgenWithExplicitJoin.Take(5).ToList();
                stopwatch2.Stop();
                
                Console.WriteLine($"Retrieved {results2.Count} records in {stopwatch2.ElapsedMilliseconds}ms");
                foreach (var result in results2)
                {
                    Console.WriteLine($"  {result.NUMLOC} - {result.ClientName} - {result.DATEDEP} - {result.TOTALHT}");
                }

                Console.WriteLine("\n3. Testing Full Entity Access - Fetching Entire Client");
                Console.WriteLine("------------------------------------------------------");
                
                // Test 3: Accessing the full client entity (this should fetch all fields)
                var locgenWithFullClient = from locgen in provider.GetTable<Locgen>()
                                         select new
                                         {
                                             locgen.NUMLOC,
                                             Client = locgen.Client,  // This should fetch entire LocClt entity
                                             locgen.DATEDEP,
                                             locgen.TOTALHT
                                         };

                Console.WriteLine("Query 3 - Full Entity Access:");
                Console.WriteLine("Expected: Should generate JOIN and SELECT all fields from LocClt");
                
                var stopwatch3 = Stopwatch.StartNew();
                var results3 = locgenWithFullClient.Take(3).ToList();
                stopwatch3.Stop();
                
                Console.WriteLine($"Retrieved {results3.Count} records in {stopwatch3.ElapsedMilliseconds}ms");
                foreach (var result in results3)
                {
                    Console.WriteLine($"  {result.NUMLOC} - {result.Client?.NOM} - {result.DATEDEP} - {result.TOTALHT}");
                    if (result.Client != null)
                    {
                        Console.WriteLine($"    Client details: {result.Client.ADR1}, {result.Client.VILLE}, {result.Client.TEL}");
                    }
                }

                // Performance Summary
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("PERFORMANCE SUMMARY");
                Console.WriteLine(new string('=', 60));
                Console.WriteLine($"1. Navigation Property Projection: {stopwatch1.ElapsedMilliseconds}ms");
                Console.WriteLine($"2. Explicit JOIN Projection:       {stopwatch2.ElapsedMilliseconds}ms");
                Console.WriteLine($"3. Full Entity Access:             {stopwatch3.ElapsedMilliseconds}ms");
                Console.WriteLine(new string('=', 60));
                
                // Analysis
                Console.WriteLine("\nANALYSIS:");
                if (stopwatch1.ElapsedMilliseconds < stopwatch3.ElapsedMilliseconds)
                {
                    Console.WriteLine("✅ Navigation Property Projection is FASTER than Full Entity Access");
                    Console.WriteLine("   This suggests IQToolkit is only fetching the NOM field, not the entire LocClt entity.");
                }
                else
                {
                    Console.WriteLine("⚠️  Navigation Property Projection is SLOWER than Full Entity Access");
                    Console.WriteLine("   This suggests IQToolkit might be fetching the entire LocClt entity.");
                }
                
                if (Math.Abs(stopwatch1.ElapsedMilliseconds - stopwatch2.ElapsedMilliseconds) < 10)
                {
                    Console.WriteLine("✅ Navigation Property and Explicit JOIN have similar performance");
                    Console.WriteLine("   This suggests both approaches generate similar SQL.");
                }
                else
                {
                    Console.WriteLine($"⚠️  Performance difference between Navigation ({stopwatch1.ElapsedMilliseconds}ms) and JOIN ({stopwatch2.ElapsedMilliseconds}ms)");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}