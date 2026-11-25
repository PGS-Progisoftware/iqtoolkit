using System;
using System.Linq;
using System.Linq.Expressions;
using Advantage.Data.Provider;
using IQToolkit.Data.Mapping;
using Xunit;
using Xunit.Abstractions;

namespace IQToolkit.Data.Advantage.Tests
{
    [Table(Name = "AccentRows")]
    public class AccentRow
    {
        [Column(IsPrimaryKey = true)]
        public int Id { get; set; }

        [Column(DbType = "Char(30)")]
        public string Label { get; set; }
    }

    /// <summary>
    /// The LocasystXSharp XBrowser find panel (PCSLib XBrowserBase.GridView_ParseFindPanelText) searches each text
    /// column with Contains(Upper(field), Upper(text)), which reaches ADS as UPPER(field) LIKE '%TEXT%': case is
    /// ignored, accents are not, so "rate" misses "Raté". The fix folds accents on both sides: the typed text in C#,
    /// the column in SQL with one REPLACE per accented capital. These tests pin what ADS does with both queries on an
    /// ANSI CDX table, opened as PCSLib opens them.
    /// </summary>
    public class AccentSearchTests
    {
        private readonly ITestOutputHelper _output;

        public AccentSearchTests(ITestOutputHelper output)
        {
            _output = output;
            TestSetup.EnsureDatabase();
            CreateAccentRows();
        }

        // As PCSLib.Data Utils connects: ANSI characters on CDX tables, trailing spaces trimmed.
        // ADS_TEST_SERVERTYPE=Remote runs them on the Advantage Database Server of this machine.
        private static string ConnectionString =>
            $"Data Source={TestSetup.DataDir};ServerType={ServerType};TableType=CDX;CharType=ANSI;TrimTrailingSpaces=True;ShowDeleted=False;Pooling=False;";

        private static string ServerType => Environment.GetEnvironmentVariable("ADS_TEST_SERVERTYPE") ?? "Local";

        private static bool IsLocalServer => string.Equals(ServerType, "Local", StringComparison.OrdinalIgnoreCase);

        private static readonly string[] Labels =
        {
            "Raté",        // 1
            "Rate",        // 2
            "RATÉ ROUGE",  // 3
            "Pâte",        // 4
            "Œuvre",       // 5
            "cœur",        // 6
            "Garçon",      // 7
            "Autre",       // 8
        };

        // What the find panel will replace after UPPER(), which raises accented lowercase letters on the server only
        // (Upper_RaisesAccentedLowercaseLetters_OnlyOnTheServer): each accented letter of code page 1252 used in French,
        // in both cases, so the query finds the same rows on both.
        // Not Œ œ Æ æ: ADS sizes a nested REPLACE for the worst case, and four folds from one letter to two make
        // "the length of the result is too long" (7200 / 2159) on a CHAR(30); 'Œ' did not match the stored 0x8C either.
        // Not Ÿ, which is in the same 0x80-0x9F range.
        private static readonly (string Accented, string Plain)[] Folds =
        {
            ("À", "A"), ("Â", "A"), ("Ä", "A"), ("à", "A"), ("â", "A"), ("ä", "A"),
            ("É", "E"), ("È", "E"), ("Ê", "E"), ("Ë", "E"), ("é", "E"), ("è", "E"), ("ê", "E"), ("ë", "E"),
            ("Î", "I"), ("Ï", "I"), ("î", "I"), ("ï", "I"),
            ("Ô", "O"), ("Ö", "O"), ("ô", "O"), ("ö", "O"),
            ("Ù", "U"), ("Û", "U"), ("Ü", "U"), ("ù", "U"), ("û", "U"), ("ü", "U"),
            ("ÿ", "Y"),
            ("Ç", "C"), ("ç", "C"),
        };

        [Fact]
        public void UpperLike_IgnoresCaseButNotAccents()
        {
            var provider = GetProvider();
            var query = Rows(provider).Where(r => r.Label.ToUpper().Contains("RATE"));

            Assert.Equal(new[] { 2 }, Ids(provider, query));
        }

        // UPPER('Raté') is 'RATÉ' on the Advantage Database Server and 'RATé' on the local server (ALS).
        [Theory]
        [InlineData("RATÉ", new[] { 3 }, new[] { 1, 3 })]
        [InlineData("PÂTE", new int[0], new[] { 4 })]
        [InlineData("GARÇON", new int[0], new[] { 7 })]
        public void Upper_RaisesAccentedLowercaseLetters_OnlyOnTheServer(string upperText, int[] localServerIds, int[] databaseServerIds)
        {
            var provider = GetProvider();
            var query = Rows(provider).Where(r => r.Label.ToUpper().Contains(upperText));

            Assert.Equal(IsLocalServer ? localServerIds : databaseServerIds, Ids(provider, query));
        }

        [Theory]
        [InlineData("rate", new[] { 1, 2, 3 })]
        [InlineData("raté", new[] { 1, 2, 3 })]
        [InlineData("RATE", new[] { 1, 2, 3 })]
        [InlineData("pate", new[] { 4 })]
        [InlineData("oeuvre", new int[0])]   // ligatures are not folded (Folds)
        [InlineData("coeur", new int[0])]
        [InlineData("garcon", new[] { 7 })]
        [InlineData("autre", new[] { 8 })]
        public void FoldedUpperLike_FindsRowsWhateverTheAccents(string typed, int[] expectedIds)
        {
            var provider = GetProvider();
            var query = Rows(provider).Where(FoldedContains(typed));

            Assert.Equal(expectedIds, Ids(provider, query));
        }

        /// <summary>The criteria DevExpress turns into LINQ for Contains(Replace(…Replace(Upper([Label]), 'À', 'A')…), text).</summary>
        private static Expression<Func<AccentRow, bool>> FoldedContains(string typed)
        {
            var row = Expression.Parameter(typeof(AccentRow), "r");
            Expression label = Expression.Call(Expression.Property(row, nameof(AccentRow.Label)), StringMethod(nameof(string.ToUpper)));
            foreach (var (accented, plain) in Folds)
                label = Expression.Call(label, StringMethod(nameof(string.Replace), typeof(string), typeof(string)),
                    Expression.Constant(accented), Expression.Constant(plain));
            var contains = Expression.Call(label, StringMethod(nameof(string.Contains), typeof(string)), Expression.Constant(Fold(typed)));
            return Expression.Lambda<Func<AccentRow, bool>>(contains, row);
        }

        private static string Fold(string text)
        {
            var folded = text.ToUpperInvariant();
            foreach (var (accented, plain) in Folds)
                folded = folded.Replace(accented, plain);
            return folded;
        }

        private static System.Reflection.MethodInfo StringMethod(string name, params Type[] parameterTypes)
            => typeof(string).GetMethod(name, parameterTypes);

        private int[] Ids(AdvantageQueryProvider provider, IQueryable<AccentRow> query)
        {
            _output.WriteLine(provider.GetQueryText(query.Expression));
            return query.Select(r => r.Id).ToList().OrderBy(id => id).ToArray();
        }

        private static AdvantageQueryProvider GetProvider() => AdvantageQueryProvider.Create(ConnectionString);

        private static IQueryable<AccentRow> Rows(AdvantageQueryProvider provider) => provider.GetTable<AccentRow>("AccentRows");

        private static void CreateAccentRows()
        {
            using (var conn = new AdsConnection(ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE AccentRows (Id Integer, Label Char(30))";
                    cmd.ExecuteNonQuery();
                    for (int i = 0; i < Labels.Length; i++)
                    {
                        cmd.CommandText = $"INSERT INTO AccentRows (Id, Label) VALUES ({i + 1}, '{Labels[i]}')";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
