using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Query shapes produced by a DevExpress server-mode grid over a projection:
    /// the grid composes Where / OrderBy / Skip / Take / Select(col).Distinct() on top of a projection
    /// that already contains associations and correlated subqueries.
    /// </summary>
    public class GridQueryPatternTests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        public GridQueryPatternTests(ITestOutputHelper output)
        {
            _output = output;
            TestSetup.EnsureDatabase();
        }

        public void Dispose()
        {
        }

        private AdvantageQueryProvider GetProvider()
        {
            return AdvantageQueryProvider.Create($"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;");
        }

        private string Sql(AdvantageQueryProvider provider, IQueryable query)
        {
            var sql = provider.GetQueryText(query.Expression);
            _output.WriteLine(sql);
            return sql;
        }

        public class CustomerRow
        {
            public int CustomerId { get; set; }
            public string Name { get; set; }
            public string City { get; set; }
            public bool HasOrders { get; set; }
        }

        private static IQueryable<CustomerRow> CustomerRows(AdvantageQueryProvider provider)
        {
            var customers = provider.GetTable<Customer>("Customers");
            var orders = provider.GetTable<Order>("Orders");
            return from c in customers
                   where c.CustomerId > 0
                   select new CustomerRow
                   {
                       CustomerId = c.CustomerId,
                       Name = c.Name,
                       City = c.City,
                       HasOrders = orders.Any(o => o.CustomerId == c.CustomerId)
                   };
        }

        public class OrderRow
        {
            public int OrderId { get; set; }
            public string CustomerName { get; set; }
            public decimal Total { get; set; }
        }

        private static IQueryable<OrderRow> OrderRows(AdvantageQueryProvider provider)
        {
            var orders = provider.GetTable<Order>("Orders");
            return from o in orders
                   where o.OrderId > 0
                   select new OrderRow { OrderId = o.OrderId, CustomerName = o.Customer.Name, Total = o.Total };
        }

        public class ParentRow
        {
            public int ParentId { get; set; }
            public string LibLang { get; set; }
            public string LangueLibelle { get; set; }
        }

        private static IQueryable<ParentRow> ParentRows(AdvantageQueryProvider provider)
        {
            var parents = provider.GetTable<AssocParent>("AssocParents");
            return from p in parents
                   where p.ParentId > 0
                   select new ParentRow { ParentId = p.ParentId, LibLang = p.LibLang, LangueLibelle = p.Langue.Libelle };
        }

        // ---------------------------------------------------------------- Contains -> IN

        [Fact]
        public void Contains_OnList_TranslatesToIn()
        {
            var provider = GetProvider();
            var ids = new List<int> { 101, 103 };
            var query = provider.GetTable<Order>("Orders").Where(o => ids.Contains(o.OrderId));

            Assert.Contains(" IN (", Sql(provider, query));
            Assert.Equal(new[] { 101, 103 }, query.Select(o => o.OrderId).ToList().OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Contains_OnStringList_InProjection_TranslatesToIn()
        {
            var provider = GetProvider();
            var names = new List<string> { "Alice", "Charlie" };
            var query = CustomerRows(provider).Where(r => names.Contains(r.Name));

            Assert.Contains(" IN (", Sql(provider, query));
            Assert.Equal(new[] { 1, 3 }, query.Select(r => r.CustomerId).ToList().OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Contains_OnHashSet_TranslatesToIn()
        {
            var provider = GetProvider();
            var cities = new HashSet<string> { "Paris" };
            var list = provider.GetTable<Customer>("Customers").Where(c => cities.Contains(c.City)).ToList();

            Assert.Single(list);
            Assert.Equal(2, list[0].CustomerId);
        }

        [Fact]
        public void Contains_OnArray_StillTranslatesToIn()
        {
            var provider = GetProvider();
            var ids = new[] { 102 };
            var list = provider.GetTable<Order>("Orders").Where(o => ids.Contains(o.OrderId)).ToList();

            Assert.Single(list);
        }

        [Fact]
        public void Contains_OnEmptyList_ReturnsNoRows()
        {
            var provider = GetProvider();
            var ids = new List<int>();
            var list = provider.GetTable<Order>("Orders").Where(o => ids.Contains(o.OrderId)).ToList();

            Assert.Empty(list);
        }

        [Fact]
        public void Contains_NegatedList_TranslatesToNotIn()
        {
            var provider = GetProvider();
            var ids = new List<int> { 101, 103 };
            var list = provider.GetTable<Order>("Orders").Where(o => !ids.Contains(o.OrderId)).ToList();

            Assert.Single(list);
            Assert.Equal(102, list[0].OrderId);
        }

        [Fact]
        public void StringContains_StillTranslatesToLike()
        {
            var provider = GetProvider();
            var query = provider.GetTable<Customer>("Customers").Where(c => c.Name.Contains("li"));

            Assert.Contains("LIKE", Sql(provider, query), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, query.ToList().Count); // Alice, Charlie
        }

        // ---------------------------------------------------------------- EXISTS under TOP

        [Fact]
        public void Exists_InProjection_PagedByBaseColumn_IsEvaluatedAfterTop()
        {
            var provider = GetProvider();
            var query = CustomerRows(provider).OrderByDescending(r => r.CustomerId).Take(2);

            var sql = Sql(provider, query);
            var list = query.ToList();

            // The EXISTS must sit in the SELECT that carries the TOP, not in a derived table under it:
            // ADS evaluates a derived table for every row before applying the TOP.
            var topSelect = sql.Substring(sql.IndexOf("TOP", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("EXISTS", topSelect, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"FROM\s*\(\s*SELECT[^()]*EXISTS", topSelect);

            Assert.Equal(new[] { 3, 2 }, list.Select(r => r.CustomerId).ToArray());
            Assert.Equal(new[] { false, true }, list.Select(r => r.HasOrders).ToArray());
        }

        [Fact]
        public void Exists_InProjection_WithAssociation_PagedByBaseColumn()
        {
            var provider = GetProvider();
            var orders = provider.GetTable<Order>("Orders");
            var customers = provider.GetTable<Customer>("Customers");
            var query = (from o in orders
                         where o.OrderId > 0
                         select new
                         {
                             o.OrderId,
                             CustomerName = o.Customer.Name,
                             OtherOrders = orders.Any(x => x.CustomerId == o.CustomerId && x.OrderId != o.OrderId)
                         })
                        .OrderBy(r => r.OrderId).Take(2);

            Sql(provider, query);
            var list = query.ToList();

            Assert.Equal(new[] { 101, 102 }, list.Select(r => r.OrderId).ToArray());
            Assert.Equal(new[] { "Alice", "Alice" }, list.Select(r => r.CustomerName.Trim()).ToArray());
            Assert.Equal(new[] { true, true }, list.Select(r => r.OtherOrders).ToArray());
        }

        [Fact]
        public void Exists_InProjection_OrderedByExists_StaysCorrect()
        {
            var provider = GetProvider();
            var list = CustomerRows(provider).OrderByDescending(r => r.HasOrders).ThenBy(r => r.CustomerId).Take(2).ToList();

            Assert.Equal(new[] { 1, 2 }, list.Select(r => r.CustomerId).ToArray());
        }

        [Fact]
        public void Exists_InProjection_FilteredOnExists_StaysCorrect()
        {
            var provider = GetProvider();
            var query = CustomerRows(provider).Where(r => !r.HasOrders);

            Assert.Equal(new[] { 3 }, query.Select(r => r.CustomerId).ToList().ToArray());
            Assert.Equal(1, query.Count());
        }

        [Fact]
        public void Exists_InProjection_SkipTake_StaysCorrect()
        {
            var provider = GetProvider();
            var list = CustomerRows(provider).OrderBy(r => r.CustomerId).Skip(1).Take(2).ToList();

            Assert.Equal(new[] { 2, 3 }, list.Select(r => r.CustomerId).ToArray());
            Assert.Equal(new[] { true, false }, list.Select(r => r.HasOrders).ToArray());
        }

        [Fact]
        public void Exists_InProjection_Count_StaysCorrect()
        {
            var provider = GetProvider();

            Assert.Equal(3, CustomerRows(provider).Count());
            Assert.Equal(2, CustomerRows(provider).Count(r => r.HasOrders));
        }

        [Fact]
        public void ScalarCount_InProjection_PagedByBaseColumn_StaysCorrect()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");
            var orders = provider.GetTable<Order>("Orders");
            var query = (from c in customers
                         where c.CustomerId > 0
                         select new { c.CustomerId, OrderCount = orders.Count(o => o.CustomerId == c.CustomerId) })
                        .OrderBy(r => r.CustomerId).Take(3);

            Sql(provider, query);
            var list = query.ToList();

            Assert.Equal(new[] { 2, 1, 0 }, list.Select(r => r.OrderCount).ToArray());
        }

        [Fact]
        public void Conditional_InProjection_PagedByBaseColumn_StaysCorrect()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");
            var query = (from c in customers
                         where c.CustomerId > 0
                         select new { c.CustomerId, Rank = c.CustomerId == 3 ? 0 : 1 })
                        .OrderByDescending(r => r.CustomerId).Take(2);

            Sql(provider, query);
            var list = query.ToList();

            Assert.Equal(new[] { 3, 2 }, list.Select(r => r.CustomerId).ToArray());
            Assert.Equal(new[] { 0, 1 }, list.Select(r => r.Rank).ToArray());
        }

        [Fact]
        public void Conditional_InProjection_OrderedByConditional_StaysCorrect()
        {
            var provider = GetProvider();
            var customers = provider.GetTable<Customer>("Customers");
            var list = (from c in customers
                        where c.CustomerId > 0
                        select new { c.CustomerId, Rank = c.City == "Paris" ? 0 : 1 })
                       .OrderBy(r => r.Rank).ThenBy(r => r.CustomerId).Take(2).ToList();

            Assert.Equal(new[] { 2, 1 }, list.Select(r => r.CustomerId).ToArray());
        }

        // ---------------------------------------------------------------- DISTINCT on a projected association

        [Fact]
        public void Distinct_OnProjectedAssociationColumn_ReturnsDistinctValues()
        {
            var provider = GetProvider();
            var query = OrderRows(provider).Select(r => r.CustomerName).Distinct();

            Sql(provider, query);
            var list = query.ToList();

            Assert.Equal(new[] { "Alice", "Bob" }, list.Select(n => n.Trim()).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void Distinct_OnProjectedFilteredAssociationColumn_ReturnsDistinctValues()
        {
            var provider = GetProvider();
            var query = ParentRows(provider).Select(r => r.LangueLibelle).Distinct();

            Sql(provider, query);
            var list = query.ToList();

            // Parents 1 and 3 have LibLang 'FR' -> France; parent 2 is blank -> NULL.
            Assert.Equal(2, list.Count);
            Assert.Contains(list, x => x != null && x.Trim() == "France");
            Assert.Contains(list, x => x == null);
        }

        [Fact]
        public void Distinct_OnFilteredAssociation_Direct_HonoursFilterAndBlankKey()
        {
            var provider = GetProvider();
            var query = provider.GetTable<AssocParent>("AssocParents").Select(p => p.Langue.Libelle).Distinct();

            Sql(provider, query);
            var list = query.ToList();

            // Same values as the projected form: France (parents 1 and 3) and NULL (blank LibLang on parent 2),
            // never the libelles of the blank-code PAYS rows or of DEVISE rows.
            Assert.Equal(2, list.Count);
            Assert.Contains(list, x => x != null && x.Trim() == "France");
            Assert.Contains(list, x => x == null);
        }

        [Fact]
        public void Distinct_OnProjectedBaseColumn_ReturnsDistinctValues()
        {
            var provider = GetProvider();
            var list = CustomerRows(provider).Select(r => r.City).Distinct().ToList();

            Assert.Equal(new[] { "London", "Paris" }, list.Select(c => c.Trim()).OrderBy(c => c).ToArray());
        }

        [Fact]
        public void GroupByKey_OnProjectedAssociationColumn_ReturnsDistinctValues()
        {
            var provider = GetProvider();
            var query = OrderRows(provider).GroupBy(r => r.CustomerName).Select(g => g.Key);

            Sql(provider, query);
            var list = query.ToList();

            Assert.Equal(new[] { "Alice", "Bob" }, list.Select(n => n.Trim()).OrderBy(n => n).ToArray());
        }

        // ---------------------------------------------------------------- associations stay joined after TOP

        [Fact]
        public void Association_PagedByBaseColumn_JoinsAfterTop()
        {
            var provider = GetProvider();
            var query = OrderRows(provider).OrderByDescending(r => r.OrderId).Take(2);

            var sql = Sql(provider, query);
            var list = query.ToList();

            Assert.Equal(new[] { 103, 102 }, list.Select(r => r.OrderId).ToArray());
            Assert.Equal(new[] { "Bob", "Alice" }, list.Select(r => r.CustomerName.Trim()).ToArray());
            Assert.True(sql.IndexOf("TOP", StringComparison.OrdinalIgnoreCase) < sql.IndexOf("LEFT OUTER JOIN", StringComparison.OrdinalIgnoreCase));
        }
    }
}
