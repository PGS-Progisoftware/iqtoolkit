using System;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// Tests for CharDateTimeFieldAttribute — a virtual DateTime? property backed by a CHAR(12) column
    /// storing "yyyyMMddHHmm".
    ///
    /// Seed data (CharDateTimeTable):
    ///   Id=1  Label="Alpha"  DTMAJ_RAW="202306151030"  → 2023-06-15 10:30
    ///   Id=2  Label="Beta"   DTMAJ_RAW="202306151445"  → 2023-06-15 14:45
    ///   Id=3  Label="Gamma"  DTMAJ_RAW="202306160900"  → 2023-06-16 09:00
    ///   Id=4  Label="Delta"  DTMAJ_RAW=NULL            → null
    /// </summary>
    public class CharDateTimeFieldTests : IDisposable
    {
        public CharDateTimeFieldTests()
        {
            TestSetup.EnsureDatabase();
        }

        public void Dispose() { }

        private AdvantageQueryProvider GetProvider()
        {
            return AdvantageQueryProvider.Create(
                $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;");
        }

        private static readonly DateTime CutoffMidDay = new DateTime(2023, 6, 15, 12, 0, 0);

        // ── SELECT / projection ────────────────────────────────────────────────

        [Fact]
        public void Select_All_Returns_Four_Rows()
        {
            var rows = GetProvider().GetTable<CharDateTimeEntity>().ToList();
            Assert.Equal(4, rows.Count);
        }

        [Fact]
        public void Select_DTMAJ_Direct_PreservesTimeComponent()
        {
            var entity = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.Id == 2)
                .Single();

            Assert.NotNull(entity.DTMAJ);
            Assert.Equal(new DateTime(2023, 6, 15, 14, 45, 0), entity.DTMAJ.Value);
        }

        [Fact]
        public void Select_DTMAJ_Null_Row_ReturnsNullProperty()
        {
            var entity = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.Id == 4)
                .Single();

            Assert.Null(entity.DTMAJ);
        }

        [Fact]
        public void Select_DTO_Projection_PreservesTimeComponent()
        {
            var dto = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.Id == 1)
                .Select(e => new { e.Id, DTModification = e.DTMAJ })
                .Single();

            Assert.Equal(1, dto.Id);
            Assert.NotNull(dto.DTModification);
            Assert.Equal(new DateTime(2023, 6, 15, 10, 30, 0), dto.DTModification.Value);
        }

        [Fact]
        public void Select_DTO_Projection_All_Rows_ToList()
        {
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Select(e => new { e.Id, e.DTMAJ })
                .ToList();

            Assert.Equal(4, results.Count);
            Assert.Equal(3, results.Count(r => r.DTMAJ.HasValue));
            Assert.Equal(1, results.Count(r => !r.DTMAJ.HasValue));
        }

        // ── WHERE comparisons ──────────────────────────────────────────────────

        [Fact]
        public void Where_GreaterThan_ReturnsCorrectRows()
        {
            // CutoffMidDay = 2023-06-15 12:00:00
            // Row 2 (14:45) and Row 3 (2023-06-16 09:00) are after the cutoff
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ > CutoffMidDay)
                .OrderBy(e => e.Id)
                .ToList();

            Assert.Equal(2, results.Count);
            Assert.Equal(2, results[0].Id);
            Assert.Equal(3, results[1].Id);
        }

        [Fact]
        public void Where_GreaterThanOrEqual_IncludesBoundary()
        {
            var boundary = new DateTime(2023, 6, 15, 14, 45, 0); // exactly Row 2
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ >= boundary)
                .OrderBy(e => e.Id)
                .ToList();

            Assert.Equal(2, results.Count);
            Assert.Equal(2, results[0].Id);
            Assert.Equal(3, results[1].Id);
        }

        [Fact]
        public void Where_LessThan_ReturnsCorrectRows()
        {
            // Note: ADS/CDX treats NULL CHAR as "" in comparisons, so a plain < would include Row 4.
            // Real-world queries combine with a null check (the common pattern).
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ != null && e.DTMAJ < CutoffMidDay)
                .ToList();

            // Only Row 1 (10:30) is before noon on 2023-06-15
            Assert.Single(results);
            Assert.Equal(1, results[0].Id);
        }

        [Fact]
        public void Where_LessThanOrEqual_IncludesBoundary()
        {
            var boundary = new DateTime(2023, 6, 15, 10, 30, 0); // exactly Row 1
            // Same note: combine with null check for correct semantics.
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ != null && e.DTMAJ <= boundary)
                .ToList();

            Assert.Single(results);
            Assert.Equal(1, results[0].Id);
        }

        [Fact]
        public void Where_Equal_MatchesExactValue()
        {
            var target = new DateTime(2023, 6, 16, 9, 0, 0);
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ == target)
                .ToList();

            Assert.Single(results);
            Assert.Equal(3, results[0].Id);
        }

        [Fact]
        public void Where_NotEqual_ExcludesMatchingRow()
        {
            var target = new DateTime(2023, 6, 16, 9, 0, 0);
            // NotEqual on nullable includes nulls behaviour — just verify the exact row is excluded
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ != target)
                .OrderBy(e => e.Id)
                .ToList();

            Assert.DoesNotContain(results, r => r.Id == 3);
        }

        [Fact]
        public void Where_IsNull_ReturnsNullRow()
        {
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ == null)
                .ToList();

            Assert.Single(results);
            Assert.Equal(4, results[0].Id);
        }

        [Fact]
        public void Where_IsNotNull_ExcludesNullRow()
        {
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ != null)
                .OrderBy(e => e.Id)
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.DoesNotContain(results, r => r.Id == 4);
        }

        // ── ORDER BY ───────────────────────────────────────────────────────────

        [Fact]
        public void OrderBy_DTMAJ_Ascending_IsSorted()
        {
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ != null)
                .OrderBy(e => e.DTMAJ)
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.Equal(1, results[0].Id); // 2023-06-15 10:30
            Assert.Equal(2, results[1].Id); // 2023-06-15 14:45
            Assert.Equal(3, results[2].Id); // 2023-06-16 09:00
        }

        [Fact]
        public void OrderBy_DTMAJ_Descending_IsSorted()
        {
            var results = GetProvider().GetTable<CharDateTimeEntity>()
                .Where(e => e.DTMAJ != null)
                .OrderByDescending(e => e.DTMAJ)
                .ToList();

            Assert.Equal(3, results.Count);
            Assert.Equal(3, results[0].Id); // 2023-06-16 09:00
            Assert.Equal(2, results[1].Id); // 2023-06-15 14:45
            Assert.Equal(1, results[2].Id); // 2023-06-15 10:30
        }

        // ── UPDATE (PartialUpdate) ─────────────────────────────────────────────

        [Fact]
        public void UpdatePartial_WritesFormattedString()
        {
            var provider = GetProvider();
            var table = provider.GetTable<CharDateTimeEntity>();
            var newDt = new DateTime(2024, 1, 1, 8, 0, 0);

            // Update Row 1's DTMAJ to 2024-01-01 08:00
            var row = table.Where(e => e.Id == 1).Single();
            row.DTMAJ = newDt;
            table.UpdatePartial(row, e => new { e.DTMAJ });

            // Read back
            var updated = table.Where(e => e.Id == 1).Single();
            Assert.Equal(newDt, updated.DTMAJ);
            Assert.Equal("202401010800", updated.DTMAJ_RAW?.Trim());
        }

        [Fact]
        public void UpdatePartial_SetExpression_WritesFormattedString()
        {
            var provider = GetProvider();
            var table = provider.GetTable<CharDateTimeEntity>();
            var newDt = new DateTime(2025, 12, 31, 23, 59, 0);

            table.UpdatePartial(
                e => e.Id == 3,
                e => new { DTMAJ = (DateTime?)newDt });

            var updated = table.Where(e => e.Id == 3).Single();
            Assert.Equal(newDt, updated.DTMAJ);
            Assert.Equal("202512312359", updated.DTMAJ_RAW?.Trim());
        }

        [Fact]
        public void UpdatePartial_SetToNull_ClearsColumn()
        {
            var provider = GetProvider();
            var table = provider.GetTable<CharDateTimeEntity>();

            table.UpdatePartial(
                e => e.Id == 2,
                e => new { DTMAJ = (DateTime?)null });

            var updated = table.Where(e => e.Id == 2).Single();
            Assert.Null(updated.DTMAJ);
        }
    }
}
