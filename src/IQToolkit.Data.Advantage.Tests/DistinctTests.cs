using System;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class DistinctTests : IDisposable
    {
        public DistinctTests()
        {
            TestSetup.EnsureDatabase();
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        private AdvantageQueryProvider GetProvider()
        {
            return AdvantageQueryProvider.Create($"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX");
        }

        [Fact]
        public void SelectDistinctWithoutNavigation()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");
            
            // Customers: Alice (London), Charlie (London), Bob (Paris)
            // Distinct Cities: London, Paris (2)
            
            var list = customers.Select(c => c.City).Distinct().ToList();

            Assert.Equal(2, list.Count);
            Assert.Contains("London", list.Select(c => c.Trim()));
            Assert.Contains("Paris", list.Select(c => c.Trim()));
        }

        [Fact]
        public void SelectDistinctWithNavigation()
        {
            var provider = GetProvider();
            var orders = provider.GetTable<Order>("Orders");
            
            // Orders: 
            // 101 -> Cust 1 (Alice)
            // 102 -> Cust 1 (Alice)
            // 103 -> Cust 2 (Bob)
            
            // Expected distinct names: Alice, Bob
            
            var list = orders.Select(o => o.Customer.Name).Distinct().ToList();

            Assert.Equal(2, list.Count);
            Assert.Contains("Alice", list.Select(n => n.Trim()));
            Assert.Contains("Bob", list.Select(n => n.Trim()));
        }
    }
}
