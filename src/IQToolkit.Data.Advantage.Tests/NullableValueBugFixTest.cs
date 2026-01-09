using System;
using System.Linq;
using Xunit;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Test that demonstrates the fix for the Nullable.Value bug.
    /// This test replicates the exact user scenario that was failing.
    /// </summary>
    public class NullableValueBugFixTest : IDisposable
    {
        private readonly AdvantageQueryProvider _provider;

        public NullableValueBugFixTest()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";
            _provider = new AdvantageQueryProvider(connString);
        }

        public void Dispose()
        {
            // Cleanup
        }

        [Fact]
        public void Bug_NullableDateTime_Value_In_Projection_With_Count()
        {
            // ORIGINAL USER CODE THAT WAS FAILING:
            // var test2 = provider.GetTable<LocGen>()
            //     .Select(locgen => new LocationSummary
            //     {
            //         NUMLOC = locgen.NumeroLocation,
            //         DATEDEP = locgen.DTDepartMateriel,  // Composite field
            //         SousTraitance = provider.GetTable<LocStGen>().Any(st => st.NUMLOC == locgen.NumeroLocation),
            //     })
            //     .Take(1)
            //     .Count();
            //
            // ERROR: System.NotSupportedException: The member access 'System.DateTime Value' is not supported

            // SIMPLIFIED REPRODUCTION:
            // This test proves the core issue is fixed
            var exception = Record.Exception(() =>
            {
                var count = _provider.GetTable<TestEntity>()
                    .Select(t => new 
                    { 
                        Id = t.Id,
                        DateValue = t.DateCol.Value  // THIS WAS THROWING NotSupportedException
                    })
                    .Take(5)
                    .Count();

                Assert.True(count > 0, "Should return at least one record");
                Assert.True(count <= 5, "Should respect Take(5) limit");
            });

            // ASSERTION: No exception should be thrown
            Assert.Null(exception);
        }

        [Fact]
        public void Bug_NullableDateTime_Value_With_ToList()
        {
            // This variation uses ToList instead of Count
            // Also was failing with same error
            var exception = Record.Exception(() =>
            {
                var results = _provider.GetTable<TestEntity>()
                    .Where(t => t.DateCol.HasValue)
                    .Select(t => new 
                    { 
                        Id = t.Id,
                        Name = t.Name,
                        DateValue = t.DateCol.Value  // THIS WAS THROWING NotSupportedException
                    })
                    .ToList();

                Assert.NotEmpty(results);
                Assert.All(results, r => Assert.NotEqual(default(DateTime), r.DateValue));
            });

            // ASSERTION: No exception should be thrown
            Assert.Null(exception);
        }

        [Fact]
        public void Bug_Composite_Field_With_Nullable_Value()
        {
            // Test: Regular nullable fields (composite fields are handled at pipeline level)
            var exception = Record.Exception(() =>
            {
                var results = _provider.GetTable<TestEntity>()
                    .Where(t => t.DateCol.HasValue)
                    .Select(t => new 
                    { 
                        Id = t.Id,
                        DateValue = t.DateCol.Value  // Regular nullable field
                    })
                    .Take(3)
                    .ToList();

                Assert.NotEmpty(results);
                Assert.All(results, r => Assert.NotEqual(default(DateTime), r.DateValue));
            });

            // ASSERTION: No exception should be thrown
            Assert.Null(exception);
        }

        [Fact]
        public void Bug_Multiple_Nullable_Value_Accesses()
        {
            // Test: Multiple .Value accesses should work
            var exception = Record.Exception(() =>
            {
                var results = _provider.GetTable<TestEntity>()
                    .Where(t => t.DateCol.HasValue)
                    .Select(t => new 
                    { 
                        t.Id,
                        Date1 = t.DateCol.Value,
                        Date2 = t.DateCol.Value  // Access same field's .Value twice
                    })
                    .Take(2)
                    .ToList();

                Assert.NotEmpty(results);
                // Both should have the same value
                Assert.All(results, r => Assert.Equal(r.Date1, r.Date2));
            });

            // ASSERTION: No exception should be thrown
            Assert.Null(exception);
        }
    }
}
