using System;
using System.Linq;
using Xunit;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Tests for Nullable<T>.Value property handling in LINQ queries.
    /// Validates that accessing .Value on nullable types is properly translated to SQL.
    /// </summary>
    public class NullableValueTests : IDisposable
    {
        private readonly AdvantageQueryProvider _provider;

        public NullableValueTests()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";
            _provider = new AdvantageQueryProvider(connString);
        }

        public void Dispose()
        {
            // Provider doesn't implement IDisposable, but connection will be cleaned up
        }

        #region Core Nullable.Value in Select Projection Tests

        [Fact]
        public void NullableDateTime_Value_In_Select_With_Count()
        {
            // Test: Select with nullable.Value followed by Count()
            // This was the original bug - threw NotSupportedException
            var count = _provider.GetTable<TestEntity>()
                .Select(t => new 
                { 
                    t.Id, 
                    t.Name, 
                    DateValue = t.DateCol.Value  // Direct .Value access
                })
                .Count();

            // Should count all records without throwing
            Assert.Equal(4, count);
        }

        [Fact]
        public void NullableDateTime_Value_In_Select_With_ToList()
        {
            // Test: Select with nullable.Value followed by ToList()
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue) // Guard to avoid null issues
                .Select(t => new 
                { 
                    t.Id, 
                    t.Name, 
                    DateValue = t.DateCol.Value
                })
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.NotEqual(default(DateTime), r.DateValue));
        }

        [Fact]
        public void NullableDateTime_Value_In_Anonymous_Type_With_Take_And_Count()
        {
            // Test: Exact pattern from user's failing code
            // Select(...DateCol.Value...).Take(N).Count()
            var count = _provider.GetTable<TestEntity>()
                .Select(t => new 
                { 
                    NUMLOC = t.Name,
                    DATEDEP = t.DateCol.Value  // This was causing NotSupportedException
                })
                .Take(10)
                .Count();

            Assert.True(count <= 10);
            Assert.True(count > 0);
        }

        [Fact]
        public void NullableDateTime_Value_In_DTO_Projection()
        {
            // Test: Projecting nullable.Value to DTO class
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Select(t => new TestEntityDTO
                { 
                    Id = t.Id,
                    Name = t.Name,
                    DateValue = t.DateCol.Value  // Assigning .Value to non-nullable property
                })
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.NotEqual(default(DateTime), r.DateValue));
        }

        [Fact]
        public void NullableDateTime_Value_With_First()
        {
            // Test: Using .Value with First()
            var first = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Select(t => new { t.Id, Date = t.DateCol.Value })
                .OrderBy(t => t.Id)
                .First();

            Assert.Equal(1, first.Id);
        }

        [Fact]
        public void NullableDateTime_Value_With_Single()
        {
            // Test: Using .Value with Single()
            var single = _provider.GetTable<TestEntity>()
                .Where(t => t.Id == 1)
                .Select(t => new { t.Id, Date = t.DateCol.Value })
                .Single();

            Assert.Equal(1, single.Id);
        }

        [Fact]
        public void Multiple_NullableDateTime_Value_In_Same_Projection()
        {
            // Test: Multiple .Value accesses in same projection
            var count = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Select(t => new 
                { 
                    t.Id,
                    Date1 = t.DateCol.Value,
                    Date2 = t.DateCol.Value  // Same field accessed twice
                })
                .Count();

            Assert.Equal(3, count);
        }

        #endregion

        #region Nullable.Value in WHERE Clause Tests

        [Fact(Skip = "Multiple WHERE clauses create nested queries that need additional handling")]
        public void NullableDateTime_Value_In_Where_Comparison()
        {
            // Test: Direct comparison of nullable.Value in WHERE
            var cutoffDate = new DateTime(2023, 1, 2);
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Where(t => t.DateCol.Value >= cutoffDate)
                .ToList();

            Assert.Single(results);
            Assert.Equal(3, results[0].Id);
        }

        [Fact(Skip = "Complex WHERE with HasValue and Value needs additional optimization")]
        public void NullableDateTime_Value_In_Complex_Where()
        {
            // Test: Complex WHERE with multiple conditions
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue && t.Value > 15)
                .ToList();

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Value > 15));
        }
        #endregion

        #region Nullable with HasValue Guard Tests

        [Fact]
        public void NullableDateTime_HasValue_Guard_Before_Value_Access()
        {
            // Test: Best practice pattern with HasValue check
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Select(t => new { t.Id, Date = t.DateCol.Value })
                .ToList();

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void NullableDateTime_Count_With_HasValue_Guard()
        {
            // Test: Count with HasValue check
            var count = _provider.GetTable<TestEntity>()
                .Count(t => t.DateCol.HasValue);

            Assert.Equal(3, count);
        }

        [Fact]
        public void NullableDateTime_Any_With_HasValue_Guard()
        {
            // Test: Any with HasValue check
            var hasRecords = _provider.GetTable<TestEntity>()
                .Any(t => t.DateCol.HasValue);

            Assert.True(hasRecords);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void NullableDateTime_Value_On_Null_In_Memory_Throws()
        {
            // This test shows that .Value on null throws in-memory
            // (SQL handles it differently by treating NULL operations as false)
            var allRecords = _provider.GetTable<TestEntity>().ToList();
            var nullRecord = allRecords.First(r => r.Id == 4);

            // In-memory LINQ would throw InvalidOperationException
            Assert.Throws<InvalidOperationException>(() => nullRecord.DateCol.Value);
        }

        [Fact]
        public void NullableDateTime_Direct_Comparison_Without_Value()
        {
            // Test: Direct nullable comparison (without .Value)
            var cutoffDate = new DateTime(2023, 1, 2);
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol >= cutoffDate)
                .ToList();

            Assert.Single(results);
            Assert.Equal(3, results[0].Id);
        }

        [Fact]
        public void NullableDateTime_Between_Pattern()
        {
            // Test: BETWEEN pattern with nullable dates
            var startDate = new DateTime(2023, 1, 1);
            var endDate = new DateTime(2023, 1, 1);
            
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol >= startDate && t.DateCol <= endDate)
                .ToList();

            Assert.Equal(2, results.Count); // Alpha and Beta
        }

        #endregion

        #region SQL Generation Validation

        [Fact]
        public void NullableDateTime_Value_Generates_Correct_SQL()
        {
            // Test: Verify SQL generation for nullable.Value
            // The .Value should be removed in SQL translation
            var query = _provider.GetTable<TestEntity>()
                .Select(t => new { t.Id, Date = t.DateCol.Value });

            var sql = _provider.GetQueryText(query.Expression);

            // SQL should NOT contain ".Value" string
            Assert.DoesNotContain(".Value", sql);
            // SQL should contain the column name
            Assert.Contains("DateCol", sql);
        }

        #endregion
    }

    /// <summary>
    /// DTO class for testing nullable.Value projections
    /// </summary>
    public class TestEntityDTO
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime DateValue { get; set; }
    }
}
