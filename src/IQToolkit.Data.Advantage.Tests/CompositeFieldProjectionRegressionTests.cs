using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Regression tests for composite datetime projections to DTOs.
    /// Guards against rewriting composite fields to the date column only (time dropped).
    /// </summary>
    public class CompositeFieldProjectionRegressionTests : IDisposable
    {
        private static readonly IReadOnlyDictionary<int, DateTime?> ExpectedById = new Dictionary<int, DateTime?>
        {
            [1] = new DateTime(2023, 1, 1, 10, 0, 0),
            [2] = new DateTime(2023, 1, 1, 14, 30, 0),
            [3] = new DateTime(2023, 1, 2, 9, 15, 0),
            [4] = null,
        };

        private readonly AdvantageQueryProvider _provider;

        public CompositeFieldProjectionRegressionTests()
        {
            TestSetup.EnsureDatabase();
            _provider = AdvantageQueryProvider.Create(
                $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;");
        }

        public void Dispose()
        {
        }

        [Fact]
        public void AnonymousDtoProjection_AllRows_PreserveDateAndTime()
        {
            var results = _provider.GetTable<TestEntity>()
                .OrderBy(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    DTModification = t.CompositeDate
                })
                .ToList();

            Assert.Equal(ExpectedById.Count, results.Count);

            foreach (var row in results)
            {
                Assert.Equal(ExpectedById[row.Id], row.DTModification);
            }
        }

        [Fact]
        public void NamedDtoProjection_PreserveDateAndTime()
        {
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.Id == 2)
                .Select(t => new ContactLikeDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    DTModification = t.CompositeDate
                })
                .ToList();

            Assert.Single(results);
            Assert.Equal(new DateTime(2023, 1, 1, 14, 30, 0), results[0].DTModification);
        }

        [Fact]
        public void DtoProjection_AfterFilterOrderAndPaging_PreserveDateAndTime()
        {
            var results = _provider.GetTable<TestEntity>()
                .Where(t => t.CompositeDate != null)
                .OrderByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    DTModification = t.CompositeDate
                })
                .Take(2)
                .ToList();

            Assert.Equal(2, results.Count);
            Assert.Equal(new DateTime(2023, 1, 2, 9, 15, 0), results[0].DTModification);
            Assert.Equal(new DateTime(2023, 1, 1, 14, 30, 0), results[1].DTModification);
        }

        [Fact]
        public void DtoProjection_Regression_MustNotCollapseTimeToMidnight()
        {
            // Date-only rewrite returns 2023-01-01 00:00:00 for row 2 — this must fail if that regresses.
            var projected = _provider.GetTable<TestEntity>()
                .Where(t => t.Id == 2)
                .Select(t => new { DTModification = t.CompositeDate })
                .Single()
                .DTModification;

            Assert.NotNull(projected);
            Assert.NotEqual(TimeSpan.Zero, projected.Value.TimeOfDay);
            Assert.Equal(14, projected.Value.Hour);
            Assert.Equal(30, projected.Value.Minute);
        }

        [Fact]
        public void DirectEntityRead_Matches_AnonymousDtoProjection()
        {
            foreach (var id in new[] { 1, 2, 3 })
            {
                var direct = _provider.GetTable<TestEntity>()
                    .Where(t => t.Id == id)
                    .Select(t => t.CompositeDate)
                    .Single();

                var projected = _provider.GetTable<TestEntity>()
                    .Where(t => t.Id == id)
                    .Select(t => new { DTModification = t.CompositeDate })
                    .Single()
                    .DTModification;

                Assert.Equal(direct, projected);
            }
        }

        [Fact]
        public void DevExpressPattern_ProjectionBeforeGroupBy_PreservesTimeComponent()
        {
            var projected = _provider.GetTable<TestEntity>()
                .Where(t => t.Id == 2)
                .Select(t => new
                {
                    t.Id,
                    DATEDEP = t.CompositeDate
                })
                .Single();

            Assert.Equal(new DateTime(2023, 1, 1, 14, 30, 0), projected.DATEDEP);
        }

        private sealed class ContactLikeDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime? DTModification { get; set; }
        }
    }
}
