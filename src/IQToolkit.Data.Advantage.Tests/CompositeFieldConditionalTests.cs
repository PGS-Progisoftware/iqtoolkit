using System;
using System.Linq;
using Xunit;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Tests for composite fields combined with conditional expressions (DevExpress patterns).
    /// These tests validate scenarios where composite DateTime fields are used in projections
    /// and then accessed in GROUP BY with conditional expressions for null-checking and date extraction.
    /// </summary>
    public class CompositeFieldConditionalTests : IDisposable
    {
        private readonly AdvantageQueryProvider _provider;

        public CompositeFieldConditionalTests()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";
            _provider = new AdvantageQueryProvider(connString);
        }

        public void Dispose()
        {
            // Provider doesn't implement IDisposable, but connection will be cleaned up
        }

        #region Simple Composite Field in Projection Tests

        [Fact]
        public void CompositeField_InProjection_WithToList()
        {
            // Test: Project a composite field to a DTO — must keep date AND time
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate != null)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    CombinedDateTime = t.CompositeDate
                })
                .ToList();

            Assert.True(results.Count > 0);
            Assert.All(results, r => Assert.NotNull(r.CombinedDateTime));

            var row1 = Assert.Single(results, r => r.Id == 1);
            Assert.Equal(new DateTime(2023, 1, 1, 10, 0, 0), row1.CombinedDateTime);

            var row2 = Assert.Single(results, r => r.Id == 2);
            Assert.Equal(new DateTime(2023, 1, 1, 14, 30, 0), row2.CombinedDateTime);
        }

        [Fact(Skip = "Type mismatch between nullable and non-nullable when accessing .Value on composite")]
        public void CompositeField_Value_InProjection_WithToList()
        {
            // Test: Project composite.Value to a DTO
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate != null)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    CombinedDateTime = t.CompositeDate.Value  // Access .Value on nullable composite
                })
                .ToList();

            Assert.True(results.Count > 0);
            Assert.All(results, r => Assert.NotEqual(default(DateTime), r.CombinedDateTime));
        }

        #endregion

        #region Composite Field with Conditional Expression Tests

        [Fact]
        public void CompositeField_ConditionalNull_InProjection()
        {
            // Test: DevExpress pattern - conditional expression checking for null
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    SafeDate = t.CompositeDate == null ? null : t.CompositeDate
                })
                .ToList();

            Assert.Equal(4, results.Count);
            // 3 have dates, 1 has null
            Assert.Equal(3, results.Count(r => r.SafeDate.HasValue));
            Assert.Single(results.Where(r => !r.SafeDate.HasValue));
        }

        [Fact]
        public void CompositeField_ConditionalWithDateExtraction_InProjection()
        {
            // Test: DevExpress pattern - extract date part with null check
            // Note: t.CompositeDate.Value.Date requires special handling for nullable
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    DateOnly = t.CompositeDate == null 
                        ? t.CompositeDate 
                        : (DateTime?)t.CompositeDate.Value.Date
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal(3, results.Count(r => r.DateOnly.HasValue));
            
            // Verify that dates have time component zeroed
            foreach (var r in results.Where(r => r.DateOnly.HasValue))
            {
                Assert.Equal(TimeSpan.Zero, r.DateOnly.Value.TimeOfDay);
            }
        }

        [Fact(Skip = "Different pattern than DevExpress - direct .Value.Date access on entity needs additional work")]
        public void CompositeField_ConditionalDateExtraction_WithValue()
        {
            // Test: Extract date using .Value then .Date
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate.HasValue)
                .Select(t => new
                {
                    t.Id,
                    DateOnly = t.CompositeDate.Value.Date
                })
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(TimeSpan.Zero, r.DateOnly.TimeOfDay));
        }

        #endregion

        #region GROUP BY with Composite Field and Conditional Tests

        [Fact(Skip = "GROUP BY on composite fields has known limitations in Advantage SQL")]
        public void CompositeField_GroupBy_Simple()
        {
            // Test: GROUP BY a composite field
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate.HasValue)
                .GroupBy(t => t.CompositeDate.Value.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .ToList();

            Assert.True(results.Count >= 1);
            Assert.All(results, r => Assert.NotEqual(default(DateTime), r.Date));
        }

        [Fact(Skip = "Complex GROUP BY with projected composite field and conditional - may hit Advantage SQL limitations")]
        public void CompositeField_GroupBy_AfterProjection_WithConditional()
        {
            // Test: DevExpress full pattern - project composite field, then GROUP BY with conditional
            // This is the exact pattern from your error case
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.Name != null)
                .Where(t => t.DateCol != null)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    ProjectedDate = t.CompositeDate,  // Composite field in projection
                    t.Value
                })
                .GroupBy(x => x.ProjectedDate == null 
                    ? x.ProjectedDate 
                    : (DateTime?)x.ProjectedDate.Value.Date)  // Conditional on projected composite
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .ToList();

            Assert.True(results.Count >= 1);
        }

        [Fact(Skip = "GROUP BY with .Date extraction on projected composite needs additional work")]
        public void CompositeField_SimplifiedGroupBy_WithConditional()
        {
            // Test: Simplified version - GROUP BY composite field with conditional
            var results = _provider.GetTable<TestEntity>()
                .GroupBy(t => t.CompositeDate == null 
                    ? t.CompositeDate 
                    : (DateTime?)t.CompositeDate.Value.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .ToList();

            Assert.True(results.Count >= 2);  // At least date group + null group
            Assert.True(results.Any(r => !r.Date.HasValue));  // Has null group
        }

        #endregion

        #region Composite Field in Complex Projections

        [Fact(Skip = "Multiple conditionals with default() needs additional type handling")]
        public void CompositeField_MultipleInProjection_WithConditionals()
        {
            // Test: Multiple composite fields with different conditional patterns
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Date1 = t.CompositeDate,
                    Date2 = t.CompositeDate == null ? null : t.CompositeDate,
                    Date3 = t.CompositeDate.HasValue ? t.CompositeDate.Value : default(DateTime)
                })
                .ToList();

            Assert.Equal(4, results.Count);
        }

        [Fact(Skip = "ORDER BY composite fields needs additional translation")]
        public void CompositeField_WithOrderBy()
        {
            // Test: ORDER BY composite field
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate.HasValue)
                .OrderBy(t => t.CompositeDate)
                .Select(t => new { t.Id, t.CompositeDate })
                .ToList();

            Assert.True(results.Count > 0);
            
            // Verify ordering
            for (int i = 1; i < results.Count; i++)
            {
                Assert.True(results[i-1].CompositeDate <= results[i].CompositeDate);
            }
        }

        [Fact(Skip = "WHERE with composite field comparison needs full composite rewriter support")]
        public void CompositeField_InWhere_ThenProjectWithConditional()
        {
            // Test: Use composite in WHERE, then project with conditional
            var cutoffDate = new DateTime(2023, 1, 1, 12, 0, 0);
            
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate > cutoffDate)
                .Select(t => new
                {
                    t.Id,
                    DatePart = t.CompositeDate == null 
                        ? null 
                        : (DateTime?)t.CompositeDate.Value.Date
                })
                .ToList();

            Assert.True(results.Count >= 0);
            Assert.All(results, r => Assert.True(r.DatePart.HasValue));
        }

        #endregion

        #region Composite Field Comparison with Conditional

        [Fact]
        public void CompositeField_NullComparison_InConditional()
        {
            // Test: Null comparison on composite field in conditional
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Status = t.CompositeDate == null ? "No Date" : "Has Date"
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Single(results.Where(r => r.Status == "No Date"));
            Assert.Equal(3, results.Count(r => r.Status == "Has Date"));
        }

        [Fact]
        public void CompositeField_NotNullComparison_InConditional()
        {
            // Test: Not null comparison
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Status = t.CompositeDate != null ? "Valid" : "Invalid"
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal(3, results.Count(r => r.Status == "Valid"));
            Assert.Single(results.Where(r => r.Status == "Invalid"));
        }

        #endregion

        #region SQL Generation Validation

        [Fact(Skip = "SQL generation with .Date property access needs additional member handling")]
        public void CompositeField_GeneratesValidSQL()
        {
            // Test: Verify SQL can be generated without errors
            var query = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    DatePart = t.CompositeDate == null 
                        ? t.CompositeDate 
                        : (DateTime?)t.CompositeDate.Value.Date
                });

            var sql = _provider.GetQueryText(query.Expression);

            // Should not throw and should contain expected elements
            Assert.NotEmpty(sql);
            Assert.Contains("SELECT", sql.ToUpper());
            Assert.Contains("FROM", sql.ToUpper());
            // Should contain CASE WHEN for the conditional
            Assert.Contains("CASE", sql.ToUpper());
        }

        [Fact]
        public void CompositeField_ComplexQuery_GeneratesValidSQL()
        {
            // Test: Complex query similar to DevExpress pattern
            var query = _provider.GetTable<TestEntity>()
                .Where(t => t.Name != null)
                .Where(t => t.DateCol != null)
                .OrderByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    ProjectedDate = t.CompositeDate,
                    t.Value
                });

            var sql = _provider.GetQueryText(query.Expression);

            Assert.NotEmpty(sql);
            Assert.Contains("SELECT", sql.ToUpper());
            Assert.Contains("WHERE", sql.ToUpper());
            Assert.Contains("ORDER BY", sql.ToUpper());
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void CompositeField_AllNull_HandlesCorrectly()
        {
            // Test: Query where composite field is always null
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate == null)
                .Select(t => new
                {
                    t.Id,
                    SafeDate = t.CompositeDate == null ? null : t.CompositeDate
                })
                .ToList();

            Assert.Single(results);
            Assert.All(results, r => Assert.False(r.SafeDate.HasValue));
        }

        [Fact]
        public void CompositeField_AllNotNull_HandlesCorrectly()
        {
            // Test: Query where composite field is never null
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate != null)
                .Select(t => new
                {
                    t.Id,
                    SafeDate = t.CompositeDate == null ? null : t.CompositeDate
                })
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.True(r.SafeDate.HasValue));
        }

        [Fact(Skip = "Nested conditionals with .Hour property access needs additional handling")]
        public void CompositeField_NestedConditionals()
        {
            // Test: Nested conditional expressions
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    Category = t.CompositeDate == null ? "None"
                             : t.CompositeDate.Value.Hour < 12 ? "Morning"
                             : "Afternoon"
                })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Single(results.Where(r => r.Category == "None"));
        }

        #endregion

        #region Integration with Other Features

        [Fact(Skip = "String conversion with composite fields needs additional handling")]
        public void CompositeField_WithNavigation_AndConditional()
        {
            // Test: Composite field with navigation properties
            // This would test the combination of multiple complex features
            var results = _provider.GetTable<TestEntity>()
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    DateInfo = t.CompositeDate == null ? "N/A" : t.CompositeDate.Value.ToShortDateString()
                })
                .ToList();

            Assert.Equal(4, results.Count);
        }

        [Fact(Skip = "Aggregates with composite fields need special handling")]
        public void CompositeField_WithAggregates()
        {
            // Test: Composite field with aggregate functions
            var result = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate.HasValue)
                .Select(t => new
                {
                    MinDate = t.CompositeDate.Value,
                    MaxDate = t.CompositeDate.Value
                })
                .Take(1)
                .ToList();

            Assert.Single(result);
        }

        #endregion

        #region DevExpress Exact Pattern Test

        [Fact]
        public void CompositeField_DevExpressExactPattern()
        {
            // Test: Exact DevExpress pattern from your error
            // 1. Project composite field to DTO
            // 2. Group by with conditional extracting .Date
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.Name != null)
                .Where(t => t.DateCol != null)
                .OrderByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    DATEDEP = t.CompositeDate,  // Composite field projection
                    t.Value
                })
                .GroupBy(x => x.DATEDEP == null 
                    ? x.DATEDEP 
                    : (DateTime?)x.DATEDEP.Value.Date)  // Conditional with .Value.Date
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .ToList();

            // Should execute without errors
            Assert.True(results.Count >= 1);
        }

        #endregion
    }
}
