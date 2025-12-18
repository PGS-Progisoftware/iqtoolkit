using System;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class MappingTests : IDisposable
    {
        public MappingTests()
        {
            TestSetup.EnsureDatabase();
        }

        public void Dispose()
        {
        }

        private AdvantageQueryProvider GetProvider()
        {
            return AdvantageQueryProvider.Create($"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;");
        }

        [Fact]
        public void MaxLength_Truncation()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            // Name is Char(20)
            string longName = "ThisNameIsLongerThanTwentyCharacters";
            var newCustomer = new Customer { CustomerId = 5, Name = longName, City = "Test" };
            
            // Advantage/DBF usually truncates silently or throws depending on settings.
            // Let's see what happens.
            customers.Insert(newCustomer);

            var inserted = customers.Single(c => c.CustomerId == 5);
            
            // Expecting truncation to 20 chars
            string expected = longName.Substring(0, 20);
            Assert.Equal(expected, inserted.Name);
        }

        [Fact]
        public void PrimaryKey_Selection()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            // Should find by PK
            var customer = customers.SingleOrDefault(c => c.CustomerId == 1);
            Assert.NotNull(customer);
            Assert.Equal("Alice", customer.Name.Trim());
        }
    }
}
