using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class CrudTests : IDisposable
    {
        public CrudTests()
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
        public void Insert()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            var newCustomer = new Customer { CustomerId = 4, Name = "David", City = "Berlin" };
            customers.Insert(newCustomer);

            var inserted = customers.Single(c => c.CustomerId == 4);
            Assert.Equal("David", inserted.Name.Trim());
            Assert.Equal("Berlin", inserted.City.Trim());
        }

        [Fact]
        public void Update()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            var customer = customers.Single(c => c.CustomerId == 1);
            customer.City = "Manchester";
            
            customers.Update(customer);

            var updated = customers.Single(c => c.CustomerId == 1);
            Assert.Equal("Manchester", updated.City.Trim());
        }

        [Fact]
        public void Delete()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            var customer = customers.Single(c => c.CustomerId == 1);
            customers.Delete(customer);

            var exists = customers.Any(c => c.CustomerId == 1);
            Assert.False(exists);
        }

        [Fact]
        public void InsertOrUpdate()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            // Case 1: Insert (New ID)
            var newCustomer = new Customer { CustomerId = 10, Name = "NewGuy", City = "Rome" };
            customers.InsertOrUpdate(newCustomer);

            var inserted = customers.Single(c => c.CustomerId == 10);
            Assert.Equal("NewGuy", inserted.Name.Trim());

            // Case 2: Update (Existing ID)
            newCustomer.City = "Milan";
            customers.InsertOrUpdate(newCustomer);

            var updated = customers.Single(c => c.CustomerId == 10);
            Assert.Equal("Milan", updated.City.Trim());
        }

        [Fact]
        public void BatchUpdatePartial_SingleColumn()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            var updates = new Dictionary<int, string>
            {
                [1] = "BatchCity1",
                [2] = "BatchCity2",
                [3] = "BatchCity3",
            };

            var results = customers.BatchUpdatePartial(c => c.City, updates).ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(1, r));

            Assert.Equal("BatchCity1", customers.GetById(1).City.Trim());
            Assert.Equal("BatchCity2", customers.GetById(2).City.Trim());
            Assert.Equal("BatchCity3", customers.GetById(3).City.Trim());
        }

        [Fact]
        public void UpdatePartial_PkOnly_StillWorks()
        {
            var customers = GetProvider().GetTable<Customer>("Customers");

            var affected = customers.UpdatePartial(
                c => c.CustomerId == 1,
                c => new { City = "Madrid" });

            Assert.Equal(1, affected);
            Assert.Equal("Madrid", customers.GetById(1).City.Trim());
        }

        [Fact]
        public void UpdatePartial_PkAndExtraColumn_Matching_Updates()
        {
            var customers = GetProvider().GetTable<Customer>("Customers");

            var affected = customers.UpdatePartial(
                c => c.CustomerId == 1 && c.City == "London",
                c => new { City = "Oslo" });

            Assert.Equal(1, affected);
            Assert.Equal("Oslo", customers.GetById(1).City.Trim());
        }

        [Fact]
        public void UpdatePartial_PkAndExtraColumn_Mismatch_DoesNotUpdate()
        {
            var customers = GetProvider().GetTable<Customer>("Customers");

            var affected = customers.UpdatePartial(
                c => c.CustomerId == 1 && c.City == "Paris",
                c => new { City = "Oslo" });

            Assert.Equal(0, affected);
            Assert.Equal("London", customers.GetById(1).City.Trim());
        }

        [Fact]
        public void GetById()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            // Test GetById (Alice is ID 1)
            var customer = customers.GetById(1);
            
            Assert.NotNull(customer);
            Assert.Equal(1, customer.CustomerId);
            Assert.Equal("Alice", customer.Name.Trim());

            // Test GetById for non-existent
            var missing = customers.GetById(999);
            Assert.Null(missing);
        }
    }
}
