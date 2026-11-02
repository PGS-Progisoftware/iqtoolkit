using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// A code stored more than once in a lookup table: the join keeps the copy VO reads (lowest record number),
    /// so each parent row comes back once.
    /// </summary>
    public class FirstCopyLookupTests : IDisposable
    {
        public FirstCopyLookupTests()
        {
            TestSetup.EnsureDatabase();
        }

        public void Dispose()
        {
        }

        private AdvantageQueryProvider GetProvider()
        {
            return AdvantageQueryProvider.Create($"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;TrimTrailingSpaces=True;");
        }

        [Fact]
        public void CodeStoredTwice_EachParentComesBackOnce_WithTheFirstCopy()
        {
            var provider = GetProvider();
            var list = provider.GetTable<LookupParent>("LookupParents")
                .Select(p => new { p.ParentId, Famille = p.FamilleCode.Libelle, Categorie = p.CategorieCode.Libelle })
                .ToList()
                .OrderBy(r => r.ParentId)
                .ToList();

            Assert.Equal(new[] { 1, 2, 3, 4 }, list.Select(r => r.ParentId).ToArray());
            Assert.Equal(new[] { "Froid", "Chaud", "Froid", "Mobilier" }, list.Select(r => r.Famille).ToArray());
            Assert.Equal(new[] { "Verre", "Inox", null, "Verre" }, list.Select(r => r.Categorie).ToArray());
        }

        [Fact]
        public void CodeStoredTwice_SqlKeepsTheLowestRecordNumber()
        {
            var provider = GetProvider();
            var query = provider.GetTable<LookupParent>("LookupParents")
                .Select(p => new { p.ParentId, Categorie = p.CategorieCode.Libelle });

            var sql = provider.GetQueryText(query.Expression);

            Assert.Matches(@"RECNO\(t\d+\) AS \[RECORDNUMBER\]", sql);
            Assert.Matches(@"t\d+\.\[RECORDNUMBER\] IN \(\s*SELECT MIN\(RECNO\(t\d+\)\)", sql);
            Assert.Contains("GROUP BY", sql);
        }

        [Fact]
        public void CodeStoredTwice_SortedByLabel_PageHasEachParentOnce()
        {
            var provider = GetProvider();
            var list = provider.GetTable<LookupParent>("LookupParents")
                .Select(p => new { p.ParentId, Categorie = p.CategorieCode.Libelle })
                .Where(r => r.Categorie != null)
                .OrderBy(r => r.Categorie).ThenBy(r => r.ParentId)
                .Take(3)
                .ToList();

            Assert.Equal(new[] { 2, 1, 4 }, list.Select(r => r.ParentId).ToArray());
            Assert.Equal(new[] { "Inox", "Verre", "Verre" }, list.Select(r => r.Categorie).ToArray());
        }

        [Fact]
        public void CodeStoredTwice_FilterOnALaterCopysLabel_MatchesNothing()
        {
            var provider = GetProvider();
            var parents = provider.GetTable<LookupParent>("LookupParents");

            var granite = parents.Where(p => p.CategorieCode.Libelle == "Verre granite").Select(p => p.ParentId).ToList();
            var verre = parents.Where(p => p.CategorieCode.Libelle == "Verre").Select(p => p.ParentId).ToList().OrderBy(id => id).ToList();

            Assert.Empty(granite);
            Assert.Equal(new[] { 1, 4 }, verre.ToArray());
        }

        [Fact]
        public void CodesUniqueWithinTheirType_SqlUnchanged()
        {
            // G1 is also a FAMILLE code and GROUPE holds blank copies: neither is a copy the join can meet.
            var provider = GetProvider();
            var query = provider.GetTable<LookupParent>("LookupParents")
                .Select(p => new { p.ParentId, Groupe = p.GroupeCode.Libelle });

            var sql = provider.GetQueryText(query.Expression);
            var list = query.ToList().OrderBy(r => r.ParentId).ToList();

            Assert.DoesNotContain("RECNO", sql);
            Assert.Equal(new[] { "Groupe 1", "Groupe 2", null, "Groupe 1" }, list.Select(r => r.Groupe).ToArray());
        }

        [Fact]
        public void CopiesCheck_RunsOncePerLookupPerProvider()
        {
            var provider = GetProvider();
            var log = new StringWriter();
            provider.Log = log;
            var query = provider.GetTable<LookupParent>("LookupParents")
                .Select(p => new { p.ParentId, Famille = p.FamilleCode.Libelle, Groupe = p.GroupeCode.Libelle });

            query.ToList();
            query.ToList();

            Assert.Equal(2, Regex.Matches(log.ToString(), @"-- SQL \(copies check\)").Count);
            Assert.Contains("-- SQL (copies check): codes stored more than once", log.ToString());
            Assert.Contains("-- SQL (copies check): none", log.ToString());
        }
    }
}
