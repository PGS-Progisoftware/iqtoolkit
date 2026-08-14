using System;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class CompositeTests : IDisposable
    {
        public CompositeTests()
        {
            TestSetup.EnsureDatabase();
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        private AdvantageQueryProvider GetProvider()
        {
            return AdvantageQueryProvider.Create($"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;");
        }

        [Fact]
        public void SelectCompositeDate_DtoProjection_MatchesDirectRead()
        {
            var provider = GetProvider();

            foreach (var id in new[] { 1, 2, 3 })
            {
                var direct = provider.GetTable<TestEntity>()
                    .Where(t => t.Id == id)
                    .Select(t => t.CompositeDate)
                    .Single();

                var dto = provider.GetTable<TestEntity>()
                    .Where(t => t.Id == id)
                    .Select(t => new { DTModification = t.CompositeDate })
                    .Single()
                    .DTModification;

                Assert.Equal(direct, dto);
                Assert.NotEqual(TimeSpan.Zero, dto.Value.TimeOfDay);
            }
        }

        [Fact]
        public void SelectCompositeDate_Direct_Projection_ToList()
        {
            var exception = Record.Exception(() =>
            {
                var results = GetProvider().GetTable<TestEntity>()
                    .Select(t => new { DTModification = t.CompositeDate })
                    .Take(1)
                    .ToList();
                Assert.Single(results);
            });
            Assert.Null(exception);
        }

        [Fact]
        public void SelectCompositeDate()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Select the composite field directly
            var result = (from t in table
                        where t.Id == 1
                        select t.CompositeDate).Single();

            // Row 1: 2023-01-01 10:00
            var expected = new DateTime(2023, 1, 1, 10, 0, 0);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SelectCompositeDate_InAnonymousProjection_PreservesTime()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");

            var result = table
                .Where(t => t.Id == 1)
                .Select(t => new { t.Id, Combined = t.CompositeDate })
                .Single();

            Assert.Equal(new DateTime(2023, 1, 1, 10, 0, 0), result.Combined);
        }

        [Fact]
        public void WhereCompositeDate_GreaterThan()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: > 2023-01-01 12:00
            // Should match Row 2 (14:30) and Row 3 (Jan 2)
            // Should NOT match Row 1 (10:00)
            var cutoff = new DateTime(2023, 1, 1, 12, 0, 0);
            
            var list = (from t in table
                        where t.CompositeDate > cutoff
                        select t).ToList();

            Assert.Equal(2, list.Count);
            Assert.Contains(list, t => t.Id == 2);
            Assert.Contains(list, t => t.Id == 3);
            Assert.DoesNotContain(list, t => t.Id == 1);
        }

        [Fact]
        public void WhereCompositeDate_LessThan()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: < 2023-01-01 12:00
            // Should match Row 1 (10:00)
            // Also matches Row 4 (NULL/Blank) because blank dates are treated as MinValue in DBF
            var cutoff = new DateTime(2023, 1, 1, 12, 0, 0);
            
            var list = (from t in table
                        where t.CompositeDate < cutoff
                        select t).ToList();

            Assert.Equal(2, list.Count);
            Assert.Contains(list, t => t.Id == 1);
            Assert.Contains(list, t => t.Id == 4);
        }

        [Fact]
        public void WhereCompositeDate_Equals()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: == 2023-01-01 14:30
            var target = new DateTime(2023, 1, 1, 14, 30, 0);
            
            var list = (from t in table
                        where t.CompositeDate == target
                        select t).ToList();

            Assert.Single(list);
            Assert.Equal(2, list[0].Id);
        }

        [Fact]
        public void WhereCompositeDate_NotEqual()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: != 2023-01-01 14:30
            // Should match Row 1 (10:00) and Row 3 (Jan 2)
            // Should NOT match Row 2 (14:30)
            // Also matches Row 4 (NULL/Blank) because blank dates != target date
            var target = new DateTime(2023, 1, 1, 14, 30, 0);
            
            var list = (from t in table
                        where t.CompositeDate != target
                        select t).ToList();

            Assert.Equal(3, list.Count);
            Assert.Contains(list, t => t.Id == 1);
            Assert.Contains(list, t => t.Id == 3);
            Assert.Contains(list, t => t.Id == 4);
            Assert.DoesNotContain(list, t => t.Id == 2);
        }

        [Fact]
        public void WhereCompositeDate_GreaterThanOrEqual()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: >= 2023-01-01 14:30
            // Should match Row 2 (14:30) and Row 3 (Jan 2)
            var cutoff = new DateTime(2023, 1, 1, 14, 30, 0);
            
            var list = (from t in table
                        where t.CompositeDate >= cutoff
                        select t).ToList();

            Assert.Equal(2, list.Count);
            Assert.Contains(list, t => t.Id == 2);
            Assert.Contains(list, t => t.Id == 3);
            Assert.DoesNotContain(list, t => t.Id == 1);
        }

        [Fact]
        public void WhereCompositeDate_LessThanOrEqual()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: <= 2023-01-01 14:30
            // Should match Row 1 (10:00) and Row 2 (14:30)
            // Also matches Row 4 (NULL/Blank) because blank dates are treated as MinValue
            var cutoff = new DateTime(2023, 1, 1, 14, 30, 0);
            
            var list = (from t in table
                        where t.CompositeDate <= cutoff
                        select t).ToList();

            Assert.Equal(3, list.Count);
            Assert.Contains(list, t => t.Id == 1);
            Assert.Contains(list, t => t.Id == 2);
            Assert.Contains(list, t => t.Id == 4);
            Assert.DoesNotContain(list, t => t.Id == 3);
        }

        [Fact]
        public void WhereCompositeDate_EqualsNull()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: == null
            // Should match Row 4
            var list = (from t in table
                        where t.CompositeDate == null
                        select t).ToList();

            Assert.Single(list);
            Assert.Equal(4, list[0].Id);
        }

        [Fact]
        public void WhereCompositeDate_NotEqualNull()
        {
            var provider = GetProvider();
            var table = provider.GetTable<TestEntity>("TestTable");
            
            // Filter: != null
            // Should match Rows 1, 2, 3
            var list = (from t in table
                        where t.CompositeDate != null
                        select t).ToList();

            Assert.Equal(3, list.Count);
            Assert.Contains(list, t => t.Id == 1);
            Assert.Contains(list, t => t.Id == 2);
            Assert.Contains(list, t => t.Id == 3);
            Assert.DoesNotContain(list, t => t.Id == 4);
        }

        [Fact]
        public void UpdatePartial_PkAndCompositeDate_MatchingStamp_Updates()
        {
            var table = GetProvider().GetTable<TestEntity>("TestTable");
            var expected = new DateTime(2023, 1, 1, 10, 0, 0);

            var affected = table.UpdatePartial(
                t => t.Id == 1 && t.CompositeDate == expected,
                t => new { Name = "Updated" });

            Assert.Equal(1, affected);
            Assert.Equal("Updated", table.Where(t => t.Id == 1).Single().Name.Trim());
        }

        [Fact]
        public void UpdatePartial_PkAndCompositeDate_StaleStamp_DoesNotUpdate()
        {
            var table = GetProvider().GetTable<TestEntity>("TestTable");
            var stale = new DateTime(2023, 1, 1, 14, 30, 0);

            var affected = table.UpdatePartial(
                t => t.Id == 1 && t.CompositeDate == stale,
                t => new { Name = "Nope" });

            Assert.Equal(0, affected);
            Assert.Equal("Alpha", table.Where(t => t.Id == 1).Single().Name.Trim());
        }

        [Fact]
        public void UpdatePartial_PkAndCompositeDate_UpdatesStampInSet()
        {
            var table = GetProvider().GetTable<TestEntity>("TestTable");
            var expected = new DateTime(2023, 1, 1, 10, 0, 0);
            var next = new DateTime(2024, 6, 1, 11, 30, 0);

            var affected = table.UpdatePartial(
                t => t.Id == 1 && t.CompositeDate == expected,
                t => new { CompositeDate = (DateTime?)next, Name = "Stamped" });

            Assert.Equal(1, affected);

            var updated = table.Where(t => t.Id == 1).Single();
            Assert.Equal(next, updated.CompositeDate);
            Assert.Equal("Stamped", updated.Name.Trim());

            var staleRetry = table.UpdatePartial(
                t => t.Id == 1 && t.CompositeDate == expected,
                t => new { Name = "Lost" });

            Assert.Equal(0, staleRetry);
            Assert.Equal("Stamped", table.Where(t => t.Id == 1).Single().Name.Trim());
        }

        [Fact]
        public void UpdatePartial_PkAndBackingColumns_Matching_Updates()
        {
            var table = GetProvider().GetTable<TestEntity>("TestTable");
            var date = new DateTime(2023, 1, 1);

            var affected = table.UpdatePartial(
                t => t.Id == 1 && t.DateCol == date && t.TimeCol == "10:00",
                t => new { Name = "ByColumns" });

            Assert.Equal(1, affected);
            Assert.Equal("ByColumns", table.Where(t => t.Id == 1).Single().Name.Trim());
        }

        [Fact]
        public void UpdatePartial_WithoutPk_Throws()
        {
            var table = GetProvider().GetTable<TestEntity>("TestTable");
            var expected = new DateTime(2023, 1, 1, 10, 0, 0);

            var ex = Assert.Throws<NotSupportedException>(() =>
                table.UpdatePartial(
                    t => t.CompositeDate == expected,
                    t => new { Name = "Nope" }));

            Assert.Contains("primary key", ex.Message);
        }
    }
}
