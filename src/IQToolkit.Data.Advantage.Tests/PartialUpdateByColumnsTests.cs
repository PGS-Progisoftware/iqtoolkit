using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// UpdatePartial on a row found by several columns: a table without a primary key (DetailLines, shaped like
    /// LocDet) and a table with a two-column key (CompositeParents).
    /// </summary>
    public class PartialUpdateByColumnsTests
    {
        private const string Location = "226060778";
        private const string OtherLocation = "226060779";
        private const string Recycled = "zzzzzzzzz";

        public PartialUpdateByColumnsTests()
        {
            TestSetup.EnsureDatabase();
        }

        private static AdvantageQueryProvider GetProvider()
        {
            return AdvantageQueryProvider.Create($"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;");
        }

        // A blank CHAR column of a DBF comes back as null.
        private static string Text(string value) => (value ?? "").Trim();

        private static List<string> ReadLines(IQueryable<DetailLine> lines)
        {
            return lines.ToList()
                .Select(l => FormattableString.Invariant($"{Text(l.NumLoc)}|{Text(l.Ligne)}|{Text(l.Label)}|{l.Qty}"))
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> ReadParents(IQueryable<CompositeParent> parents)
        {
            return parents.ToList()
                .Select(p => $"{p.KeyA}|{p.KeyB}|{Text(p.Name)}")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> Replace(List<string> rows, string before, string after)
        {
            Assert.Contains(before, rows);
            return rows.Select(r => r == before ? after : r).OrderBy(r => r, StringComparer.Ordinal).ToList();
        }

        [Fact]
        public void NoKey_TwoColumns_UpdatesThatRowOnly()
        {
            var lines = GetProvider().GetTable<DetailLine>();
            var before = ReadLines(lines);
            string numLoc = Location;
            string ligne = "  2";

            var affected = lines.UpdatePartial(
                l => l.NumLoc == numLoc && l.Ligne == ligne,
                l => new { Label = "Changed", Qty = 7.5 });

            Assert.Equal(1, affected);
            Assert.Equal(Replace(before, "226060778|2|Two|2", "226060778|2|Changed|7.5"), ReadLines(lines));
        }

        [Fact]
        public void NoKey_TwoColumns_NoSuchRow_UpdatesNothing()
        {
            var lines = GetProvider().GetTable<DetailLine>();
            var before = ReadLines(lines);

            var affected = lines.UpdatePartial(
                l => l.NumLoc == Location && l.Ligne == "  9",
                l => new { Label = "Nope" });

            Assert.Equal(0, affected);
            Assert.Equal(before, ReadLines(lines));
        }

        [Fact]
        public void NoKey_SetCanWriteTheColumnsTheRowWasFoundBy()
        {
            var lines = GetProvider().GetTable<DetailLine>();
            var before = ReadLines(lines);

            // Delete line 2 the way Locasyst does (the row is recycled), then move line 3 up.
            var recycled = lines.UpdatePartial(
                l => l.NumLoc == Location && l.Ligne == "  2",
                l => new { NumLoc = Recycled, Ligne = "", Label = "", Qty = 0d });
            var moved = lines.UpdatePartial(
                l => l.NumLoc == Location && l.Ligne == "  3",
                l => new { Ligne = "  2" });

            Assert.Equal(1, recycled);
            Assert.Equal(1, moved);

            var expected = Replace(before, "226060778|2|Two|2", "zzzzzzzzz|||0");
            expected = Replace(expected, "226060778|3|Three|3", "226060778|2|Three|3");
            Assert.Equal(expected, ReadLines(lines));
        }

        [Fact]
        public void NoKey_OneColumn_UpdatesEveryMatchingRow()
        {
            var lines = GetProvider().GetTable<DetailLine>();

            var affected = lines.UpdatePartial(
                l => l.NumLoc == Location,
                l => new { Qty = 99d });

            Assert.Equal(3, affected);
            var after = lines.ToList();
            Assert.All(after.Where(l => Text(l.NumLoc) == Location), l => Assert.Equal(99d, l.Qty));
            Assert.Equal(new[] { 10d, 20d }, after.Where(l => Text(l.NumLoc) == OtherLocation).Select(l => l.Qty).OrderBy(q => q));
            Assert.Equal(0d, after.Single(l => Text(l.NumLoc) == Recycled).Qty);
        }

        [Fact]
        public void NoKey_PredicateTestingNoColumn_Throws()
        {
            var lines = GetProvider().GetTable<DetailLine>();
            var before = ReadLines(lines);
            bool always = true;

            var literal = Assert.Throws<NotSupportedException>(() =>
                lines.UpdatePartial(l => true, l => new { Label = "All" }));
            var captured = Assert.Throws<NotSupportedException>(() =>
                lines.UpdatePartial(l => always, l => new { Label = "All" }));

            Assert.Contains("at least one column", literal.Message);
            Assert.Contains("at least one column", captured.Message);
            Assert.Equal(before, ReadLines(lines));
        }

        [Fact]
        public void NoKey_CharColumns_BindAnsiFixedLength()
        {
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";
            var provider = new AdvantageQueryProvider(connString)
            {
                EnableOutboundCommandLogging = true
            };
            var log = new StringWriter();
            provider.Log = log;

            var affected = provider.GetTable<DetailLine>().UpdatePartial(
                l => l.NumLoc == Location && l.Ligne == "  1",
                l => new { Label = "Ansi" });

            Assert.Equal(1, affected);
            Assert.Contains("AnsiStringFixedLength", log.ToString());
            Assert.DoesNotContain("(String) =", log.ToString());
        }

        [Fact]
        public void CompositeKey_BothColumns_UpdatesThatRowOnly()
        {
            var parents = GetProvider().GetTable<CompositeParent>();
            var before = ReadParents(parents);
            int keyA = 1;
            int keyB = 20;

            var affected = parents.UpdatePartial(
                p => p.KeyA == keyA && p.KeyB == keyB,
                p => new { Name = "Renamed" });

            Assert.Equal(1, affected);
            Assert.Equal(Replace(before, "1|20|Parent2", "1|20|Renamed"), ReadParents(parents));
        }

        [Fact]
        public void CompositeKey_ExtraCondition_Mismatch_UpdatesNothing()
        {
            var parents = GetProvider().GetTable<CompositeParent>();
            var before = ReadParents(parents);

            var affected = parents.UpdatePartial(
                p => p.KeyA == 1 && p.KeyB == 20 && p.Name == "Parent1",
                p => new { Name = "Nope" });

            Assert.Equal(0, affected);
            Assert.Equal(before, ReadParents(parents));
        }

        [Fact]
        public void CompositeKey_ExtraCondition_Matching_Updates()
        {
            var parents = GetProvider().GetTable<CompositeParent>();
            var before = ReadParents(parents);

            var affected = parents.UpdatePartial(
                p => p.KeyA == 1 && p.KeyB == 20 && p.Name == "Parent2",
                p => new { Name = "Checked" });

            Assert.Equal(1, affected);
            Assert.Equal(Replace(before, "1|20|Parent2", "1|20|Checked"), ReadParents(parents));
        }

        [Fact]
        public void CompositeKey_OneKeyColumnMissing_Throws()
        {
            var parents = GetProvider().GetTable<CompositeParent>();
            var before = ReadParents(parents);

            var ex = Assert.Throws<NotSupportedException>(() =>
                parents.UpdatePartial(
                    p => p.KeyA == 1,
                    p => new { Name = "Nope" }));

            Assert.Contains("primary key", ex.Message);
            Assert.Contains("KeyB", ex.Message);
            Assert.Equal(before, ReadParents(parents));
        }

        [Fact]
        public void CompositeKey_SameKeyColumnTwice_Throws()
        {
            var parents = GetProvider().GetTable<CompositeParent>();

            var ex = Assert.Throws<NotSupportedException>(() =>
                parents.UpdatePartial(
                    p => p.KeyA == 1 && p.KeyA == 2 && p.KeyB == 10,
                    p => new { Name = "Nope" }));

            Assert.Contains("KeyA", ex.Message);
        }
    }
}
