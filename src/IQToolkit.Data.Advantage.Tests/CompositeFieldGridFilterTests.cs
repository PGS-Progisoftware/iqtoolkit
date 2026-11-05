using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace IQToolkit.Data.Advantage.Tests
{
    /// <summary>
    /// A composite date filtered, sorted and grouped the way a DevExpress grid does it: after the projection
    /// (XLocBrowser: <c>select new LocationSummary { DATEDEP = l.DTDepartMateriel }</c>, then <c>Where(r => r.DATEDEP ...)</c>).
    /// </summary>
    public class CompositeFieldGridFilterTests : IDisposable
    {
        private readonly AdvantageQueryProvider _provider;

        public CompositeFieldGridFilterTests(ITestOutputHelper output)
        {
            TestSetup.EnsureDatabase();
            _provider = AdvantageQueryProvider.Create(
                $"Data Source={TestSetup.DataDir};ServerType=Local;TableType=CDX;ShowDeleted=False;");
            _provider.Log = new OutputWriter(output);
        }

        public void Dispose() { }

        private IQueryable<Dto> Projected() =>
            _provider.GetTable<TestEntity>().Select(t => new Dto { Id = t.Id, Name = t.Name, DATEDEP = t.CompositeDate });

        // The grid's instant feedback source swallows a failed query and spins: fail the test instead of hanging it
        private static T WithTimeout<T>(Func<T> query)
        {
            var task = Task.Run(query);
            if (!task.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Query did not finish in 15 s");
            return task.Result;
        }

        [Fact]
        public void DayRange_OnProjectedComposite()
        {
            var from = new DateTime(2023, 1, 1);
            var to = new DateTime(2023, 1, 2);
            var ids = WithTimeout(() => Projected().Where(d => d.DATEDEP >= from && d.DATEDEP < to).Select(d => d.Id).OrderBy(i => i).ToList());
            Assert.Equal(new[] { 1, 2 }, ids);
        }

        [Fact]
        public void DayRange_OnProjectedComposite_Count()
        {
            var from = new DateTime(2023, 1, 1);
            var to = new DateTime(2023, 1, 2);
            var count = WithTimeout(() => Projected().Where(d => d.DATEDEP >= from && d.DATEDEP < to).Count());
            Assert.Equal(2, count);
        }

        [Fact]
        public void DatePart_OnProjectedComposite()
        {
            var day = new DateTime(2023, 1, 1);
            var ids = WithTimeout(() => Projected().Where(d => d.DATEDEP.Value.Date == day).Select(d => d.Id).OrderBy(i => i).ToList());
            Assert.Equal(new[] { 1, 2 }, ids);
        }

        [Fact]
        public void Sort_OnProjectedComposite_OrdersByDateThenTime()
        {
            var ids = WithTimeout(() => Projected().Where(d => d.DATEDEP != null).OrderByDescending(d => d.DATEDEP).Select(d => d.Id).ToList());
            Assert.Equal(new[] { 3, 2, 1 }, ids);
        }

        // The Excel filter popup's values: GroupBy(col).OrderBy(g => g.Key).Select(g => g.Key).Take(n)
        [Fact]
        public void FilterPopupValues_OnProjectedComposite_KeepTheTime()
        {
            var keys = WithTimeout(() => Projected().GroupBy(d => d.DATEDEP).OrderBy(g => g.Key).Select(g => g.Key).Take(100).ToList());
            Assert.Equal(new DateTime?[]
            {
                null,
                new DateTime(2023, 1, 1, 10, 0, 0),
                new DateTime(2023, 1, 1, 14, 30, 0),
                new DateTime(2023, 1, 2, 9, 15, 0),
            }, keys);
        }

        [Fact]
        public void FilterPopupValues_OnProjectedComposite_WithOtherColumnsFilter()
        {
            var keys = WithTimeout(() => Projected().Where(d => d.Name == "Beta").GroupBy(d => d.DATEDEP).OrderBy(g => g.Key).Select(g => g.Key).Take(100).ToList());
            Assert.Equal(new DateTime?[] { new DateTime(2023, 1, 1, 14, 30, 0) }, keys);
        }

        // An hour the getter cannot parse (blank, "  :  ") reads as midnight, while SQL sorts it below '00:00'.
        // Each comparison must select the rows the getter's value selects, on the entity and after a projection.
        [Theory]
        [InlineData("==", "2023-01-01 00:00")]
        [InlineData("!=", "2023-01-01 00:00")]
        [InlineData("<", "2023-01-01 00:00")]
        [InlineData("<=", "2023-01-01 00:00")]
        [InlineData(">", "2023-01-01 00:00")]
        [InlineData(">=", "2023-01-01 00:00")]
        [InlineData("==", "2023-01-02 00:00")]
        [InlineData("!=", "2023-01-02 00:00")]
        [InlineData("<", "2023-01-02 00:00")]
        [InlineData("<=", "2023-01-02 00:00")]
        [InlineData(">", "2023-01-02 00:00")]
        [InlineData(">=", "2023-01-02 00:00")]
        [InlineData("==", "2023-01-01 10:00")]
        [InlineData("!=", "2023-01-01 10:00")]
        [InlineData("<", "2023-01-01 10:00")]
        [InlineData("<=", "2023-01-01 10:00")]
        [InlineData(">", "2023-01-01 10:00")]
        [InlineData(">=", "2023-01-01 10:00")]
        public void BlankHour_ComparesAsMidnight(string op, string value)
        {
            var v = DateTime.ParseExact(value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var hours = _provider.GetTable<CompositeHourEntity>();
            var expected = hours.ToList()
                .Where(h => h.CompositeDate != null)
                .Where(Comparison<CompositeHourEntity>(h => h.CompositeDate, op, v).Compile())
                .Select(h => h.Id).OrderBy(i => i).ToList();

            var onEntity = hours
                .Where(h => h.CompositeDate != null)
                .Where(Comparison<CompositeHourEntity>(h => h.CompositeDate, op, v))
                .Select(h => h.Id).OrderBy(i => i).ToList();
            Assert.Equal(expected, onEntity);

            var projected = hours
                .Select(h => new Dto { Id = h.Id, DATEDEP = h.CompositeDate })
                .Where(d => d.DATEDEP != null)
                .Where(Comparison<Dto>(d => d.DATEDEP, op, v))
                .Select(d => d.Id).OrderBy(i => i).ToList();
            Assert.Equal(expected, projected);
        }

        [Fact]
        public void BlankHour_DayRange_KeepsTheDay()
        {
            var from = new DateTime(2023, 1, 1);
            var to = new DateTime(2023, 1, 2);
            var ids = _provider.GetTable<CompositeHourEntity>()
                .Select(h => new Dto { Id = h.Id, DATEDEP = h.CompositeDate })
                .Where(d => d.DATEDEP >= from && d.DATEDEP < to)
                .Select(d => d.Id).OrderBy(i => i).ToList();
            Assert.Equal(new[] { 1, 2, 3 }, ids);
        }

        // DevExpress filter criteria as CriteriaToExpressionConverter (DevExpress.Data 26.1) writes them in LINQ.
        // [Col] Is Null: (object)row.Col == null
        [Fact]
        public void DevExpress_IsNull_OnProjectedComposite()
        {
            var ids = WithTimeout(() => Projected().Where(d => (object)d.DATEDEP == null).Select(d => d.Id).ToList());
            Assert.Equal(new[] { 4 }, ids);
        }

        [Fact]
        public void DevExpress_IsNotNull_OnProjectedComposite()
        {
            var ids = WithTimeout(() => Projected().Where(d => !((object)d.DATEDEP == null)).Select(d => d.Id).OrderBy(i => i).ToList());
            Assert.Equal(new[] { 1, 2, 3 }, ids);
        }

        // GetDate([Col]) = #day#: IIF(row.Col == null, row.Col, (DateTime?)row.Col.Value.Date) == day
        [Fact]
        public void DevExpress_GetDate_OnProjectedComposite()
        {
            DateTime? day = new DateTime(2023, 1, 1);
            var ids = WithTimeout(() => Projected()
                .Where(d => (d.DATEDEP == null ? d.DATEDEP : (DateTime?)d.DATEDEP.Value.Date) == day)
                .Select(d => d.Id).OrderBy(i => i).ToList());
            Assert.Equal(new[] { 1, 2 }, ids);
        }

        // GetDate([Col]) In (#day1#, #day2#), the date tree of the Excel filter popup
        [Fact]
        public void DevExpress_GetDateIn_OnProjectedComposite()
        {
            DateTime? day1 = new DateTime(2023, 1, 1);
            DateTime? day2 = new DateTime(2023, 1, 2);
            var ids = WithTimeout(() => Projected()
                .Where(d => (d.DATEDEP == null ? d.DATEDEP : (DateTime?)d.DATEDEP.Value.Date) == day1
                    || (d.DATEDEP == null ? d.DATEDEP : (DateTime?)d.DATEDEP.Value.Date) == day2)
                .Select(d => d.Id).OrderBy(i => i).ToList());
            Assert.Equal(new[] { 1, 2, 3 }, ids);
        }

        [Fact]
        public void DevExpress_GetDateAfter_OnProjectedComposite()
        {
            DateTime? day = new DateTime(2023, 1, 2);
            var ids = WithTimeout(() => Projected()
                .Where(d => (d.DATEDEP == null ? d.DATEDEP : (DateTime?)d.DATEDEP.Value.Date) >= day)
                .Select(d => d.Id).ToList());
            Assert.Equal(new[] { 3 }, ids);
        }

        [Fact]
        public void DevExpress_GetDate_BlankHour_KeepsTheDay()
        {
            DateTime? day = new DateTime(2023, 1, 1);
            var ids = _provider.GetTable<CompositeHourEntity>()
                .Select(h => new Dto { Id = h.Id, DATEDEP = h.CompositeDate })
                .Where(d => (d.DATEDEP == null ? d.DATEDEP : (DateTime?)d.DATEDEP.Value.Date) == day)
                .Select(d => d.Id).OrderBy(i => i).ToList();
            Assert.Equal(new[] { 1, 2, 3 }, ids);
        }

        // A CHAR date-time ("yyyyMMddHHmm") goes through the same minimal entity after a projection
        private IQueryable<Dto> ProjectedChar() =>
            _provider.GetTable<CharDateTimeEntity>().Select(e => new Dto { Id = e.Id, Name = e.Label, DATEDEP = e.DTMAJ });

        [Fact]
        public void CharDateTime_DayRange_OnProjected()
        {
            var from = new DateTime(2023, 6, 15);
            var to = new DateTime(2023, 6, 16);
            var ids = WithTimeout(() => ProjectedChar().Where(d => d.DATEDEP >= from && d.DATEDEP < to).Select(d => d.Id).OrderBy(i => i).ToList());
            Assert.Equal(new[] { 1, 2 }, ids);
        }

        [Fact]
        public void CharDateTime_Sort_OnProjected()
        {
            var ids = WithTimeout(() => ProjectedChar().Where(d => d.DATEDEP != null).OrderByDescending(d => d.DATEDEP).Select(d => d.Id).ToList());
            Assert.Equal(new[] { 3, 2, 1 }, ids);
        }

        [Fact]
        public void CharDateTime_FilterPopupValues_OnProjected()
        {
            var keys = WithTimeout(() => ProjectedChar().GroupBy(d => d.DATEDEP).OrderBy(g => g.Key).Select(g => g.Key).Take(100).ToList());
            Assert.Equal(new DateTime?[]
            {
                null,
                new DateTime(2023, 6, 15, 10, 30, 0),
                new DateTime(2023, 6, 15, 14, 45, 0),
                new DateTime(2023, 6, 16, 9, 0, 0),
            }, keys);
        }

        // row => row.Member op value, with the value captured as a closure field the way a query holds it
        private static Expression<Func<T, bool>> Comparison<T>(Expression<Func<T, DateTime?>> member, string op, DateTime value)
        {
            DateTime? captured = value;
            Expression<Func<DateTime?>> closure = () => captured;
            var left = member.Body;
            var right = closure.Body;
            Expression body;
            switch (op)
            {
                case "==": body = Expression.Equal(left, right); break;
                case "!=": body = Expression.NotEqual(left, right); break;
                case "<": body = Expression.LessThan(left, right); break;
                case "<=": body = Expression.LessThanOrEqual(left, right); break;
                case ">": body = Expression.GreaterThan(left, right); break;
                case ">=": body = Expression.GreaterThanOrEqual(left, right); break;
                default: throw new ArgumentOutOfRangeException(nameof(op), op, null);
            }
            return Expression.Lambda<Func<T, bool>>(body, member.Parameters);
        }

        public sealed class Dto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime? DATEDEP { get; set; }
        }

        private sealed class OutputWriter : System.IO.StringWriter
        {
            private readonly ITestOutputHelper _output;
            public OutputWriter(ITestOutputHelper output) { _output = output; }
            public override void WriteLine(string value) { try { _output.WriteLine(value ?? ""); } catch (InvalidOperationException) { } }
        }
    }
}
