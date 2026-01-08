using System;
using System.Linq;
using Xunit;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Tests for nullable type handling in LINQ queries.
    /// Validates that nullable.Value.Property patterns are properly translated to SQL.
    /// </summary>
    public class NullableTests : IDisposable
    {
        private readonly AdvantageQueryProvider _provider;

        public NullableTests()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";
            _provider = new AdvantageQueryProvider(connString);
        }

        public void Dispose()
        {
            // Provider doesn't implement IDisposable, but connection will be cleaned up
        }

        #region Nullable DateTime.Value.Property Tests

        [Fact]
        public void NullableDateTime_Value_Year_Comparison()
        {
            // Test: nullable.Value.Year > constant
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Year > 2022)
                .ToList();

            Assert.Equal(3, results.Count); // All non-null dates are in 2023
            Assert.All(results, r => Assert.True(r.DateCol.HasValue && r.DateCol.Value.Year == 2023));
        }

        [Fact]
        public void NullableDateTime_Value_Year_Equals()
        {
            // Test: nullable.Value.Year == constant
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Year == 2023)
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(2023, r.DateCol.Value.Year));
        }

        [Fact]
        public void NullableDateTime_Value_Month_Comparison()
        {
            // Test: nullable.Value.Month == constant
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Month == 1)
                .ToList();

            Assert.Equal(3, results.Count); // All 2023 dates are in January
            Assert.All(results, r => Assert.Equal(1, r.DateCol.Value.Month));
        }

        [Fact]
        public void NullableDateTime_Value_Day_Comparison()
        {
            // Test: nullable.Value.Day comparison
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Day == 1)
                .ToList();

            Assert.Equal(2, results.Count); // Two records with day = 1
            Assert.All(results, r => Assert.Equal(1, r.DateCol.Value.Day));
        }

        [Fact]
        public void NullableDateTime_Value_Multiple_Properties()
        {
            // Test: Multiple property accesses in same query
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Year == 2023 && t.DateCol.Value.Month == 1 && t.DateCol.Value.Day == 2)
                .ToList();

            Assert.Single(results);
            Assert.Equal(3, results[0].Id); // Gamma record
        }

        [Fact]
        public void NullableDateTime_Value_Year_In_Select()
        {
            // Test: nullable.Value.Property in select projection
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Select(t => new { t.Id, t.Name, Year = t.DateCol.Value.Year })
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(2023, r.Year));
        }

        [Fact]
        public void NullableDateTime_Value_Month_In_OrderBy()
        {
            // Test: nullable.Value.Property in OrderBy
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .OrderBy(t => t.DateCol.Value.Day)
                .Select(t => new { t.Id, Day = t.DateCol.Value.Day })
                .ToList();

            Assert.Equal(3, results.Count);
            // Should be ordered by day: 1, 1, 2
            Assert.Equal(1, results[0].Day);
            Assert.Equal(1, results[1].Day);
            Assert.Equal(2, results[2].Day);
        }

        #endregion

        #region Nullable with HasValue Guard Tests

        [Fact]
        public void NullableDateTime_HasValue_Guard_Before_Value_Access()
        {
            // Test: Proper pattern with HasValue check
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue && t.DateCol.Value.Year > 2022)
                .ToList();

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void NullableDateTime_Count_With_HasValue()
        {
            // Test: Count with nullable check
            var count = _provider.GetTable<TestEntity>()
                .Count(t => t.DateCol.HasValue && t.DateCol.Value.Year == 2023);

            Assert.Equal(3, count);
        }

        [Fact]
        public void NullableDateTime_Any_With_Value_Property()
        {
            // Test: Any with nullable.Value access
            var hasRecords = _provider.GetTable<TestEntity>()
                .Any(t => t.DateCol.HasValue && t.DateCol.Value.Year == 2023);

            Assert.True(hasRecords);
        }

        [Fact]
        public void NullableDateTime_First_With_Value_Property()
        {
            // Test: First with nullable.Value access
            var first = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Year == 2023)
                .OrderBy(t => t.Id)
                .First();

            Assert.Equal(1, first.Id);
            Assert.Equal("Alpha", first.Name);
        }

        #endregion

        #region Null Handling Tests

        [Fact]
        public void NullableDateTime_Excludes_Null_Records()
        {
            // Test: Queries with .Value should naturally exclude nulls (will throw in LINQ to Objects)
            // In SQL, YEAR(NULL) returns NULL, and NULL > 2022 is false
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Year > 2022)
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.DoesNotContain(results, r => r.Id == 4); // Record 4 has NULL date
        }

        [Fact]
        public void NullableDateTime_GetValueOrDefault_Pattern()
        {
            // Test: GetValueOrDefault() usage
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new 
                { 
                    t.Id, 
                    Year = t.DateCol.GetValueOrDefault().Year 
                })
                .ToList();

            Assert.Equal(4, results.Count);
            // Record 4 (null date) should have year 1 (default DateTime year)
            var nullRecord = results.First(r => r.Id == 4);
            Assert.Equal(1, nullRecord.Year);
        }

        #endregion

        #region Complex Query Tests

        [Fact]
        public void NullableDateTime_Complex_Where_With_And()
        {
            // Test: Complex condition with AND
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Year == 2023 && t.Value > 15)
                .ToList();

            Assert.Equal(2, results.Count); // Beta and Gamma
            Assert.All(results, r => Assert.True(r.Value > 15));
        }

        [Fact]
        public void NullableDateTime_Complex_Where_With_Or()
        {
            // Test: Complex condition with OR
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.Value.Day == 1 || t.Value > 25)
                .ToList();

            Assert.Equal(3, results.Count); // Alpha, Beta (day=1), Gamma (value>25)
        }

        [Fact]
        public void NullableDateTime_GroupBy_With_Value_Property()
        {
            // Test: GroupBy with nullable.Value.Property
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .GroupBy(t => t.DateCol.Value.Day)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .OrderBy(x => x.Day)
                .ToList();

            Assert.Equal(2, results.Count);
            Assert.Equal(1, results[0].Day);
            Assert.Equal(2, results[0].Count); // Two records with day 1
            Assert.Equal(2, results[1].Day);
            Assert.Equal(1, results[1].Count); // One record with day 2
        }

        #endregion

        #region Nullable Numeric Tests

        [Fact]
        public void NullableInt_GetValueOrDefault()
        {
            // Test nullable int (using Id cast to nullable)
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new 
                { 
                    t.Id,
                    // Cast to nullable to test the pattern
                    NullableId = (int?)t.Id,
                    HasValue = ((int?)t.Id).HasValue
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.True(r.HasValue));
        }

        #endregion

        #region Error Case Tests

        [Fact]
        public void NullableDateTime_Value_On_Null_Throws_In_Memory()
        {
            // This test shows that .Value on null should be avoided in real code
            // SQL will handle it by treating NULL comparison as false
            var allRecords = _provider.GetTable<TestEntity>().ToList();
            var nullRecord = allRecords.First(r => r.Id == 4);

            // In-memory LINQ would throw InvalidOperationException
            Assert.Throws<InvalidOperationException>(() => nullRecord.DateCol.Value);
        }

        #endregion

        #region Date Comparison with DateTime Constants

        [Fact]
        public void NullableDateTime_Compare_With_DateTime_Constant()
        {
            // Test: nullable date comparison with DateTime constant
            var cutoffDate = new DateTime(2023, 1, 2);
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol >= cutoffDate)
                .ToList();

            Assert.Single(results);
            Assert.Equal(3, results[0].Id); // Only Gamma on 2023-01-02
        }

        [Fact]
        public void NullableDateTime_Compare_Less_Than()
        {
            // Test: less than comparison
            var cutoffDate = new DateTime(2023, 1, 2);
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol < cutoffDate)
                .ToList();

            Assert.Equal(2, results.Count); // Alpha and Beta on 2023-01-01
        }

        [Fact]
        public void NullableDateTime_Between_Dates()
        {
            // Test: BETWEEN pattern
            var startDate = new DateTime(2023, 1, 1);
            var endDate = new DateTime(2023, 1, 1);
            
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol >= startDate && t.DateCol <= endDate)
                .ToList();

            Assert.Equal(2, results.Count); // Alpha and Beta
        }

        #endregion
    }
}
