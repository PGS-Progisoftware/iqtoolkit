using IQToolkit.Data.Advantage;
using IQToolkit.Data.Mapping;
using System;
using System.Linq;

namespace Test.Advantage
{
	/// <summary>
	/// Test entity mapping for LocGen table.
	/// </summary>
	public class LocGen
	{
		[Column(DbType = "CHAR(9)", IsPrimaryKey = true)]
		public string NUMLOC { get; set; }

		[Column(DbType = "CHAR(10)")]
		public string CODECLT { get; set; }

		public string CODEPER1 { get; set; }
		public DateTime DATEDEP { get; set; }
		public DateTime DATEFIN { get; set; }
		public DateTime DATERET { get; set; }
		public DateTime DATELOC { get; set; }
		//public char? STATUT { get; set; }
		//public char? STATUT2 { get; set; }
		public decimal TOTALHT { get; set; }
		public string AFFAIRE { get; set; }
		public DateTime DATECREAT { get; set; }
		public string INITCREAT { get; set; }
		public DateTime DATEMAJ { get; set; }
		public string INITMAJ { get; set; }
		public string OBS { get; set; }

		[Column(DbType = "CHAR(5)")]
		public string HEUREDEP { get; set; }

		[Column(DbType = "CHAR(5)")]
		public string HEUREMAJ { get; set; }

		public string VALIDTECH { get; set; }
		public string VALIDCOMM { get; set; }

		// Composite DateTime fields - combine date + time columns into single DateTime for queries
		private DateTime? _dtdep;
		[CompositeField(DateMember = nameof(DATEDEP), TimeMember = nameof(HEUREDEP))]
		public DateTime DTDEP
		{
			get
			{
				if (!_dtdep.HasValue)
				{
					_dtdep = DATEDEP.Date;
					if (Utils.TryParseTime(HEUREDEP, out int h, out int m))
					{
						_dtdep.Value.AddHours(h);
						_dtdep.Value.AddMinutes(m);
					}
				}
				return _dtdep.Value;
			}
			set
			{
				DATEDEP = value.Date;
				HEUREDEP = value.ToString("HH:mm");
			}
		}

		[CompositeField(DateMember = nameof(DATEMAJ), TimeMember = nameof(HEUREMAJ))]
		public DateTime DTMAJ { get; set; }

		//// Relations
		//[Association(KeyMembers = "CODECLT")]
		//public LocClt Client { get; set; }

		//[Association(KeyMembers = "CODECLT,CODEPER1", RelatedKeyMembers = "CODECLT,CODEPER")]
		//public LocPer Per1 { get; set; }
	}

	public class LocClt
	{
		[Column(DbType = "CHAR(10)", IsPrimaryKey = true)]
		public string CODECLT { get; set; }

		public string Nom { get; set; }
	}

	public class LocPer
	{
		public string CODECLT { get; set; }
		public string CODEPER { get; set; }
		public string Nom { get; set; }
		public string Prenom { get; set; }
	}

	[Table(Name = "LocStgen")]
	public class LocStGen
	{
		public string NUMLOC { get; set; }
	}

	class Program
	{
		static void Main(string[] args)
		{
			string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\DATA\\LYON;ServerType=remote;TableType=CDX;TrimTrailingSpaces=True;CharType=OEM";

			var provider = AdvantageQueryProvider.Create(connectionString);
			provider.Log = Console.Out;
			provider.EnableQueryTiming = false;

			Test1_SimpleQuery(provider);
			//Test2_StringCompare(provider);
			//Test3_CompositeField(provider);

			Console.WriteLine("\nAll tests completed. Press any key to exit...");
			Console.ReadKey();
		}

		static void Test1_SimpleQuery(AdvantageQueryProvider provider)
		{
			Console.WriteLine("=== TEST 1: Simple Query with Composite Field ===");
			try
			{
				var query = provider.GetTable<LocGen>()
					.Where(lg => lg.DTDEP > DateTime.Now.AddYears(-3))
					.Select(lg => new { dt = lg.DTDEP, id = lg.NUMLOC });
					
					

				//Console.WriteLine("SQL: " + provider.GetQueryText(query.Expression));
				var result = query.ToList();
				Console.WriteLine($"✅ SUCCESS: {result} result(s)\n");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ FAILED: {ex.Message}\n");
			}
		}

		static void Test2_StringCompare(AdvantageQueryProvider provider)
		{
			Console.WriteLine("=== TEST 2: String.Compare ===");
			try
			{
				var query = provider.GetTable<LocGen>()
					.Where(lg => string.Compare(lg.HEUREDEP, "12:00") < 0)
					.Select(lg => lg.NUMLOC);

				Console.WriteLine("SQL: " + provider.GetQueryText(query.Expression));
				var result = query.ToList();
				Console.WriteLine($"✅ SUCCESS: {result.Count} result(s)\n");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ FAILED: {ex.Message}\n");
			}
		}

		static void Test3_CompositeField(AdvantageQueryProvider provider)
		{
			Console.WriteLine("=== TEST 3: Composite Field with Specific DateTime ===");
			try
			{
				var cutoffDate = new DateTime(2024, 6, 28, 13, 33, 0);

				var query = provider.GetTable<LocGen>()
					.Where(lg => lg.DTDEP > cutoffDate)
					.Select(lg => lg.NUMLOC)
					.Take(1);

				Console.WriteLine("SQL: " + provider.GetQueryText(query.Expression));
				var result = query.ToList();
				Console.WriteLine($"✅ SUCCESS: {result.Count} result(s)\n");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"❌ FAILED: {ex.Message}\n");
			}
		}
	}

	public class Utils
	{
		public static bool TryParseTime(string input, out int hours, out int minutes)
		{
			hours = 0;
			minutes = 0;

			// Check length first (fastest rejection)
			if (input.Length != 5)
				return false;

			ReadOnlySpan<char> span = input.AsSpan();

			// Check colon position
			if (span[2] != ':')
				return false;

			// Parse hours (2 digits)
			if (!char.IsDigit(span[0]) || !char.IsDigit(span[1]))
				return false;
			hours = (span[0] - '0') * 10 + (span[1] - '0');

			// Parse minutes (2 digits)
			if (!char.IsDigit(span[3]) || !char.IsDigit(span[4]))
				return false;
			minutes = (span[3] - '0') * 10 + (span[4] - '0');

			// Optional: validate ranges
			if (hours > 23 || minutes > 59)
				return false;

			return true;
		}
	}
}
