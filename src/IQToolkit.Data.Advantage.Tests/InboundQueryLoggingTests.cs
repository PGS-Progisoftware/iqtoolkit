using System;
using System.IO;
using System.Linq;
using Xunit;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    public class InboundQueryLoggingTests
    {
        [Fact]
        public void InboundQueryLogging_Writes_Expression_Before_Translation()
        {
            TestSetup.EnsureDatabase();
            string connString = $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;Pooling=False;";

            var provider = new AdvantageQueryProvider(connString)
            {
                EnableInboundQueryLogging = true
            };

            var sw = new StringWriter();
            provider.Log = sw;

            _ = provider.GetTable<TestEntity>()
                .Where(t => t.DateCol.HasValue)
                .Select(t => new { t.Id, Year = t.DateCol.Value.Year })
                .Take(1)
                .ToList();

            var log = sw.ToString();
            Assert.Contains("-- LINQ (inbound)", log);
            Assert.Contains("Where", log);
            Assert.Contains("Select", log);
            Assert.Contains("Take", log);
        }
    }
}
