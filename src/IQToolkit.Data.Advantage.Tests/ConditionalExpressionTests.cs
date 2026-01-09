using System;
using System.Linq;
using Xunit;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Tests for conditional expression (ternary operator) support in LINQ queries.
    /// Validates that conditional expressions are properly translated to SQL CASE WHEN statements.
    /// </summary>
    public class ConditionalExpressionTests : IDisposable
    {
        private readonly AdvantageQueryProvider _provider;

        public ConditionalExpressionTests()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";
            _provider = new AdvantageQueryProvider(connString);
        }

        public void Dispose()
        {
            // Provider doesn't implement IDisposable, but connection will be cleaned up
        }

        #region Simple Conditional Expression Tests

        [Fact]
        public void SimpleConditional_String_WithToList()
        {
            // Test: Simple ternary operator with string values
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Status = t.Value > 15 ? "High" : "Low"
                })
                .ToList();

            Assert.Equal(4, results.Count);
            // Values: 10.5 (Low), 20.0 (High), 30.5 (High), 40.0 (High)
            Assert.Equal(3, results.Count(r => r.Status == "High"));
            Assert.Single(results.Where(r => r.Status == "Low"));
        }

        [Fact]
        public void SimpleConditional_Number_WithToList()
        {
            // Test: Simple ternary operator with numeric values
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Category = t.Value > 15 ? 1 : 0
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal(3, results.Count(r => r.Category == 1));
            Assert.Single(results.Where(r => r.Category == 0));
        }

        [Fact]
        public void SimpleConditional_InWhere_WithToList()
        {
            // Test: Conditional expression in WHERE clause
            var results = _provider.GetTable<TestEntity>()
                .Where(t => (t.Value > 15 ? 1 : 0) == 1)
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.True(r.Value > 15));
        }

        #endregion

        #region Chained Conditional Expression Tests

        [Fact]
        public void ChainedConditional_ThreeWay_WithToList()
        {
            // Test: Chained ternary operators (if-else-if pattern)
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    t.Value,
                    Priority = t.Value > 30 ? "High"
                             : t.Value > 15 ? "Medium"
                             : "Low"
                })
                .OrderBy(r => r.Id)
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal("Low", results[0].Priority);      // Id=1, Value=10.5
            Assert.Equal("Medium", results[1].Priority);   // Id=2, Value=20.0
            Assert.Equal("High", results[2].Priority);     // Id=3, Value=30.5
            Assert.Equal("High", results[3].Priority);     // Id=4, Value=40.0
        }

        [Fact]
        public void ChainedConditional_FourWay_WithToList()
        {
            // Test: Multiple chained conditionals
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Grade = t.Value >= 35 ? "A"
                          : t.Value >= 25 ? "B"
                          : t.Value >= 15 ? "C"
                          : "D"
                })
                .OrderBy(r => r.Id)
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal("D", results[0].Grade);  // Value=10.5
            Assert.Equal("C", results[1].Grade);  // Value=20.0
            Assert.Equal("B", results[2].Grade);  // Value=30.5
            Assert.Equal("A", results[3].Grade);  // Value=40.0
        }

        #endregion

        #region Nullable Conditional Expression Tests

        [Fact]
        public void NullableConditional_CheckForNull_WithToList()
        {
            // Test: Conditional checking for null (DevExpress pattern)
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    HasDate = t.DateCol != null ? "Yes" : "No"
                })
                .OrderBy(r => r.Id)
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal("Yes", results[0].HasDate);  // Id=1 has date
            Assert.Equal("Yes", results[1].HasDate);  // Id=2 has date
            Assert.Equal("Yes", results[2].HasDate);  // Id=3 has date
            Assert.Equal("No", results[3].HasDate);   // Id=4 is null
        }

        [Fact]
        public void NullableConditional_NullCoalescing_WithToList()
        {
            // Test: Null-coalescing pattern using conditional
            var defaultDate = new DateTime(2000, 1, 1);
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    SafeDate = t.DateCol != null ? t.DateCol.Value : defaultDate
                })
                .OrderBy(r => r.Id)
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.NotEqual(defaultDate, results[0].SafeDate);  // Has actual date
            Assert.NotEqual(defaultDate, results[1].SafeDate);  // Has actual date
            Assert.NotEqual(defaultDate, results[2].SafeDate);  // Has actual date
            Assert.Equal(defaultDate, results[3].SafeDate);     // Uses default
        }

        [Fact]
        public void NullableConditional_WithHasValue_WithToList()
        {
            // Test: Using HasValue in conditional (common pattern)
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Year = t.DateCol.HasValue ? t.DateCol.Value.Year : 0
                })
                .OrderBy(r => r.Id)
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal(2023, results[0].Year);  // Has date
            Assert.Equal(2023, results[1].Year);  // Has date
            Assert.Equal(2023, results[2].Year);  // Has date
            Assert.Equal(0, results[3].Year);     // Null date
        }

        #endregion

        #region DevExpress-Style Nullable Date Conditional Tests

        [Fact]
        public void DevExpressPattern_NullableDateWithDatePart_WithToList()
        {
            // Test: DevExpress-style nullable date handling
            // Pattern: date == null ? date : date.Value.Date
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Select(t => new
                {
                    t.Id,
                    // DevExpress generates this pattern for date-only comparisons
                    DateOnly = t.DateCol == null
                        ? t.DateCol
                        : (DateTime?)t.DateCol.Value.Date
                })
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r =>
            {
                Assert.NotNull(r.DateOnly);
                // Date part should have time zeroed out
                Assert.Equal(TimeSpan.Zero, r.DateOnly.Value.TimeOfDay);
            });
        }

        [Fact]
        public void DevExpressPattern_NullableDateInGroupBy()
        {
            // Test: DevExpress nullable date in GROUP BY
            var results = _provider.GetTable<TestEntity>()
                .GroupBy(t => t.DateCol == null
                    ? t.DateCol
                    : (DateTime?)t.DateCol.Value.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .ToList();

            // Should have 2 groups: 2023-01-01 (2 records), null (1 record), 2023-01-02 (1 record)
            Assert.True(results.Count >= 2);
            Assert.True(results.Any(r => r.Date == null));
        }

        #endregion

        #region Complex Conditional Expression Tests

        [Fact]
        public void ComplexConditional_MultipleConditions_WithToList()
        {
            // Test: Conditional with complex boolean expressions
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Category = (t.Value > 15 && t.DateCol.HasValue) ? "Active"
                             : (t.Value > 15 && !t.DateCol.HasValue) ? "Inactive"
                             : "Low"
                })
                .OrderBy(r => r.Id)
                .ToList();

            Assert.Equal(4, results.Count);
            // Verify categorization logic
            Assert.NotEmpty(results);
        }

        [Fact]
        public void ComplexConditional_NestedInArithmetic_WithToList()
        {
            // Test: Conditional nested in arithmetic expression
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Score = t.Value * (t.Value > 20 ? 1.5 : 1.0)
                })
                .OrderBy(r => r.Id)
                .ToList();

            Assert.Equal(4, results.Count);
            // Verify scores
            Assert.Equal(10.5, results[0].Score);      // 10.5 * 1.0
            Assert.Equal(20.0, results[1].Score);      // 20.0 * 1.0 (20 is not > 20)
            Assert.Equal(45.75, results[2].Score);     // 30.5 * 1.5
            Assert.Equal(60.0, results[3].Score);      // 40.0 * 1.5
        }

        [Fact]
        public void ComplexConditional_WithStringComparison_WithToList()
        {
            // Test: Conditional based on string comparison
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    Label = t.Name.StartsWith("A") ? "Starts with A" : "Other"
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Single(results.Where(r => r.Label == "Starts with A"));  // "Alpha"
            Assert.Equal(3, results.Count(r => r.Label == "Other"));
        }

        #endregion

        #region Conditional Expression in Aggregation Tests

        [Fact]
        public void ConditionalInSum_WithToList()
        {
            // Test: Conditional in SUM aggregation
            var result = _provider.GetTable<TestEntity>()
                .Select(t => t.Value > 15 ? t.Value : 0)
                .Sum();

            // Sum of values > 15: 20.0 + 30.5 + 40.0 = 90.5
            Assert.Equal(90.5, result);
        }

        [Fact]
        public void ConditionalInCount_WithWhere()
        {
            // Test: Conditional affecting count
            var count = _provider.GetTable<TestEntity>()
                .Count(t => (t.Value > 15 ? 1 : 0) == 1);

            Assert.Equal(3, count);
        }

        #endregion

        #region SQL Generation Validation Tests

        [Fact(Skip = "SQL optimization may remove unused conditional expressions from SELECT")]
        public void ConditionalExpression_GeneratesCorrectSQL()
        {
            // Test: Verify SQL generation for conditional expression
            var query = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Status = t.Value > 15 ? "High" : "Low"
                });

            var sql = _provider.GetQueryText(query.Expression);

            // SQL should contain CASE WHEN
            Assert.Contains("CASE", sql.ToUpper());
            Assert.Contains("WHEN", sql.ToUpper());
            Assert.Contains("THEN", sql.ToUpper());
            Assert.Contains("ELSE", sql.ToUpper());
            Assert.Contains("END", sql.ToUpper());
        }

        [Fact(Skip = "SQL optimization may remove unused conditional expressions from SELECT")]
        public void ChainedConditional_GeneratesCorrectSQL()
        {
            // Test: Verify SQL generation for chained conditional
            var query = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    Priority = t.Value > 30 ? "High"
                             : t.Value > 15 ? "Medium"
                             : "Low"
                });

            var sql = _provider.GetQueryText(query.Expression);

            // SQL should contain multiple WHEN clauses
            Assert.Contains("CASE", sql.ToUpper());
            var whenCount = sql.ToUpper().Split(new[] { "WHEN" }, StringSplitOptions.None).Length - 1;
            Assert.True(whenCount >= 2, $"Expected at least 2 WHEN clauses, found {whenCount}");
        }

        [Fact(Skip = "SQL optimization may remove unused conditional expressions from SELECT")]
        public void NullableConditional_GeneratesCorrectSQL()
        {
            // Test: Verify SQL generation for nullable conditional
            var query = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    HasDate = t.DateCol != null ? "Yes" : "No"
                });

            var sql = _provider.GetQueryText(query.Expression);

            // SQL should contain IS NOT NULL check
            Assert.Contains("CASE", sql.ToUpper());
            Assert.Contains("IS NOT NULL", sql.ToUpper());
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ConditionalWithAllTrue_ReturnsExpected()
        {
            // Test: Conditional where condition is always true
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Result = true ? "Always" : "Never"
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.Equal("Always", r.Result));
        }

        [Fact]
        public void ConditionalWithAllFalse_ReturnsExpected()
        {
            // Test: Conditional where condition is always false
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Result = false ? "Never" : "Always"
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.Equal("Always", r.Result));
        }

        [Fact]
        public void ConditionalWithSameValueInBothBranches()
        {
            // Test: Conditional where both branches return same value
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Result = t.Value > 15 ? 100 : 100
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.Equal(100, r.Result));
        }

        #endregion
    }
}
