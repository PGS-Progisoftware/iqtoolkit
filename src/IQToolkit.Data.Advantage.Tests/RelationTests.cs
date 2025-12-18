using System;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class RelationTests : IDisposable
    {
        public RelationTests()
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
        public void Join_CustomersOrders()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");
            var orders = provider.GetTable<Order>("Orders");

            var query = from c in customers
                        join o in orders on c.CustomerId equals o.CustomerId
                        select new { c.Name, o.Total };

            var list = query.ToList();

            Assert.Equal(3, list.Count); // 2 orders for Alice, 1 for Bob
            Assert.Contains(list, x => x.Name.Trim() == "Alice" && x.Total == 100.00m);
            Assert.Contains(list, x => x.Name.Trim() == "Alice" && x.Total == 200.00m);
            Assert.Contains(list, x => x.Name.Trim() == "Bob" && x.Total == 150.00m);
        }

        [Fact]
        public void Join_CustomersOrders_WithFilter()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");
            var orders = provider.GetTable<Order>("Orders");

            // Filter orders > 100.00 (Should be 200.00 and 150.00)
            var query = from c in customers
                        join o in orders on c.CustomerId equals o.CustomerId
                        where o.Total > 100.00m
                        select new { c.Name, o.Total };

            var list = query.ToList();

            Assert.Equal(2, list.Count);
            Assert.Contains(list, x => x.Name.Trim() == "Alice" && x.Total == 200.00m);
            Assert.Contains(list, x => x.Name.Trim() == "Bob" && x.Total == 150.00m);
            Assert.DoesNotContain(list, x => x.Total == 100.00m);
        }

        [Fact]
        public void GroupJoin_CustomersOrders()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");
            var orders = provider.GetTable<Order>("Orders");

            var query = from c in customers
                        join o in orders on c.CustomerId equals o.CustomerId into g
                        select new { Customer = c.Name, OrderCount = g.Count() };

            var list = query.ToList();

            Assert.Equal(3, list.Count); // Alice, Bob, Charlie
            Assert.Contains(list, x => x.Customer.Trim() == "Alice" && x.OrderCount == 2);
            Assert.Contains(list, x => x.Customer.Trim() == "Bob" && x.OrderCount == 1);
            Assert.Contains(list, x => x.Customer.Trim() == "Charlie" && x.OrderCount == 0);
        }

        [Fact]
        public void Association_OneToMany_Projection()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");

            // Test navigation property Customer.Orders in projection
            var query = from c in customers
                        where c.Name == "Alice"
                        select new { c.Name, Orders = c.Orders };

            var result = query.Single();
            Assert.Equal("Alice", result.Name.Trim());
            Assert.NotNull(result.Orders);
            Assert.Equal(2, result.Orders.Count);
            Assert.Contains(result.Orders, o => o.Total == 100.00m);
            Assert.Contains(result.Orders, o => o.Total == 200.00m);
        }

        [Fact]
        public void Association_ManyToOne_Projection()
        {
            var provider = GetProvider();
            var orders = provider.GetTable<Order>("Orders");

            // Test navigation property Order.Customer in projection
            var query = from o in orders
                        where o.Total == 100.00m
                        select new { o.OrderId, CustomerName = o.Customer.Name };

            var result = query.Single();
            Assert.Equal("Alice", result.CustomerName.Trim());
        }

        [Fact]
        public void Association_FilterOnAssociation()
        {
            var provider = GetProvider();
            var orders = provider.GetTable<Order>("Orders");

            // Test filtering based on associated entity property
            var query = from o in orders
                        where o.Customer.Name == "Bob"
                        select o;

            var list = query.ToList();
            Assert.Single(list);
            Assert.Equal(150.00m, list[0].Total);
        }

        [Fact]
        public void Association_FilterAttribute()
        {
            var provider = GetProvider();
            var orders = provider.GetTable<Order>("Orders");

            // Project OrderId and the filtered association
            var query = from o in orders
                        select new { o.OrderId, o.CustomerInLondon };

            var list = query.ToList();

            // Alice (101) is in London, so CustomerInLondon should be populated
            var aliceOrder = list.Single(x => x.OrderId == 101);
            Assert.NotNull(aliceOrder.CustomerInLondon);
            Assert.Equal("Alice", aliceOrder.CustomerInLondon.Name.Trim());

            // Bob (103) is in Paris, so CustomerInLondon should be null (filtered out)
            var bobOrder = list.Single(x => x.OrderId == 103);
            Assert.Null(bobOrder.CustomerInLondon);
        }

        [Fact]
        public void CompositeKey_Selection()
        {
            var provider = GetProvider();
            var parents = provider.GetTable<CompositeParent>("CompositeParents");

            // Select by composite key (1, 10)
            var parent = parents.SingleOrDefault(p => p.KeyA == 1 && p.KeyB == 10);
            Assert.NotNull(parent);
            Assert.Equal("Parent1", parent.Name.Trim());

            // Select by composite key (1, 20)
            var parent2 = parents.SingleOrDefault(p => p.KeyA == 1 && p.KeyB == 20);
            Assert.NotNull(parent2);
            Assert.Equal("Parent2", parent2.Name.Trim());
        }

        [Fact]
        public void CompositeKey_Association_ParentToChild()
        {
            var provider = GetProvider();
            var parents = provider.GetTable<CompositeParent>("CompositeParents");

            // Parent1 (1, 10) should have 2 children
            var query = from p in parents
                        where p.KeyA == 1 && p.KeyB == 10
                        select new { p.Name, Children = p.Children };

            var result = query.Single();
            Assert.Equal("Parent1", result.Name.Trim());
            Assert.NotNull(result.Children);
            Assert.Equal(2, result.Children.Count);
            Assert.Contains(result.Children, c => c.Data.Trim() == "Child1_P1");
            Assert.Contains(result.Children, c => c.Data.Trim() == "Child2_P1");
        }

        [Fact]
        public void CompositeKey_Association_ChildToParent()
        {
            var provider = GetProvider();
            var children = provider.GetTable<CompositeChild>("CompositeChildren");

            // Child3 (Parent 1, 20) should have Parent2
            var query = from c in children
                        where c.ChildId == 3
                        select new { c.Data, ParentName = c.Parent.Name };

            var result = query.Single();
            Assert.Equal("Child1_P2", result.Data.Trim());
            Assert.Equal("Parent2", result.ParentName.Trim());
        }
    }
}
