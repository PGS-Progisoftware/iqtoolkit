using System;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class SelectTests : IDisposable
    {
        public SelectTests()
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
        public void SelectAll()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            var query = from t in table select t;
            
            string sql = provider.GetQueryText(query.Expression);
            var list = query.ToList();

            Assert.NotNull(sql);
            Assert.Equal(4, list.Count);
            Assert.Contains(list, t => t.Name.Trim() == "Alpha");
        }

        [Fact]
        public void SelectColumns()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            var query = from t in table select new { t.Name, t.Value };
            
            string sql = provider.GetQueryText(query.Expression);
            var list = query.ToList();
            
            Assert.NotNull(sql);
            Assert.Equal(4, list.Count);
            Assert.Equal("Alpha", list[0].Name.Trim());
        }

        [Fact]
        public void SelectWhere()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            var query = from t in table 
                        where t.Value > 15.0
                        select t;
            
            string sql = provider.GetQueryText(query.Expression);
            var list = query.ToList();
            
            Assert.NotNull(sql);
            Assert.Contains("WHERE", sql);
            Assert.Equal(3, list.Count); // Beta (20.0), Gamma (30.5), Delta (40.0)
            Assert.DoesNotContain(list, t => t.Name.Trim() == "Alpha");
        }

        [Fact]
        public void SelectOrderBy()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            var query = from t in table 
                        orderby t.Value descending
                        select t;
            
            string sql = provider.GetQueryText(query.Expression);
            var list = query.ToList();
            
            Assert.NotNull(sql);
            Assert.Contains("ORDER BY", sql);
            // Delta (40.0) is now first
            Assert.Equal("Delta", list[0].Name.Trim());
            // Gamma (30.5) is second
            Assert.Equal("Gamma", list[1].Name.Trim());
        }

        [Fact]
        public void SelectCalculated()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            var query = from t in table 
                        select new { NewValue = t.Value * 2 };
            
            string sql = provider.GetQueryText(query.Expression);
            var list = query.ToList();
            
            Assert.NotNull(sql);
            Assert.Equal(21.0, list.First(x => x.NewValue == 21.0).NewValue); // Alpha 10.5 * 2
        }
    }
}
