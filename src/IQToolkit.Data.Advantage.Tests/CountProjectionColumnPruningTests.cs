using System;
using System.IO;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class CountProjectionColumnPruningTests
    {
        [Fact]
        public void Count_With_Relationship_In_Projection_Does_Not_Select_Projected_Columns()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";

            var provider = new AdvantageQueryProvider(connString);
            var sw = new StringWriter();
            provider.Log = sw;

            _ = provider.GetTable<Order>()
                .Select(o => new
                {
                    o.OrderId,
                    CustomerName = o.Customer.Name
                })
                .Take(1)
                .Count();

            var log = sw.ToString();

            Assert.Contains("SELECT COUNT(*)", log);
            Assert.Contains("SELECT TOP 1 1 AS", log);
            Assert.DoesNotContain("JOIN [Customers]", log);
            Assert.DoesNotContain(".[Name]", log);
        }

        [Fact]
        public void Count_With_CompositeField_In_Projection_Does_Not_Select_Projected_Columns()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";

            var provider = new AdvantageQueryProvider(connString);
            var sw = new StringWriter();
            provider.Log = sw;

            _ = provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    t.CompositeDate
                })
                .Take(1)
                .Count();

            var log = sw.ToString();

            Assert.Contains("SELECT COUNT(*)", log);
            Assert.Contains("SELECT TOP 1 1 AS", log);
            Assert.DoesNotContain(".[DateCol]", log);
            Assert.DoesNotContain(".[TimeCol]", log);
        }
    }
}
