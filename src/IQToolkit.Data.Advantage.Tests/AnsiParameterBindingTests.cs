using System.Data;
using System.IO;
using System.Linq;
using IQToolkit.Data;
using IQToolkit.Data.Advantage;
using Xunit;

namespace IQToolkit.Data.Advantage.Tests
{
    public class AnsiParameterBindingTests
    {
        [Fact]
        public void Char_MapsTo_AnsiStringFixedLength()
        {
            var qt = (SqlQueryType)new AdvantageLanguage().TypeSystem.Parse("CHAR(40)");
            Assert.Equal(SqlType.Char, qt.SqlType);
            Assert.Equal(DbType.AnsiStringFixedLength, qt.SqlType.ToDbType());
            Assert.Equal(40, qt.Length);
        }

        [Fact]
        public void VarChar_MapsTo_AnsiString()
        {
            var qt = (SqlQueryType)new AdvantageLanguage().TypeSystem.Parse("VARCHAR(40)");
            Assert.Equal(SqlType.VarChar, qt.SqlType);
            Assert.Equal(DbType.AnsiString, qt.SqlType.ToDbType());
        }

        [Fact]
        public void ClrString_WithoutMapping_IsNVarChar()
        {
            var qt = (SqlQueryType)new AdvantageLanguage().TypeSystem.GetColumnType(typeof(string));
            Assert.Equal(SqlType.NVarChar, qt.SqlType);
            Assert.Equal(DbType.String, qt.SqlType.ToDbType());
        }

        [Fact]
        public void UpdatePartial_CharColumn_BindsAnsiStringFixedLength()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";

            var provider = new AdvantageQueryProvider(connString)
            {
                EnableOutboundCommandLogging = true
            };

            var sw = new StringWriter();
            provider.Log = sw;

            var customers = provider.GetTable<Customer>("Customers");
            customers.UpdatePartial(
                c => c.CustomerId == 1,
                c => new { Name = "Zed" });

            var log = sw.ToString();
            Assert.Contains("AnsiStringFixedLength", log);
            Assert.DoesNotContain("(String) = ['Zed']", log);
        }

        [Fact]
        public void IQueryable_CharFilter_BindsAnsiStringFixedLength()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";

            var provider = new AdvantageQueryProvider(connString)
            {
                EnableOutboundCommandLogging = true
            };

            var sw = new StringWriter();
            provider.Log = sw;

            _ = provider.GetTable<Customer>("Customers")
                .Where(c => c.Name == "Alice")
                .ToList();

            var log = sw.ToString();
            Assert.Contains("AnsiStringFixedLength", log);
        }
    }
}
