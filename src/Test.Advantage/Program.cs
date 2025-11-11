using IQToolkit.Data;
using IQToolkit.Data.Advantage;
using IQToolkit.Data.Mapping;
//using PCSLib.Data.DBF;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Test.Advantage
{

	public enum LocGenStatut
	{
		/// <summary>
		/// Devis
		/// </summary>
		Devis = '0',
		/// <summary>
		/// Reservation
		/// </summary>
		Reservation = '1',
		/// <summary>
		/// Location
		/// </summary>
		Location = '2',
		/// <summary>
		/// Retour
		/// </summary>
		Retour = '3',
		/// <summary>
		/// Retour controle
		/// </summary>
		RetourControle = '4',
		/// <summary>
		/// Annulation
		/// </summary>
		Annulation = '9',
		Annulation2 = 'A'
	}

	[Table(Name = "LocDet")]
	public class LocDet
	{
		[MaxLength(9)]
		[Column(DbType = "Char(9)")]
		public string NUMLOC { get; set; }

		[MaxLength(15)]
		[Column(DbType = "Char(15)")]
		public string CODEART { get; set; }
	}

	/// <summary>
	/// Test entity mapping for LocGen table.
	/// </summary>
	[Table(Name = "LocGen")]
	public class LocGen
	{
		[IQToolkit.Data.Mapping.Association(KeyMembers = "NUMLOC")]
		public List<LocDet> Articles { get; set; }

		[Column(DbType = "CHAR(9)", IsPrimaryKey = true)]
		public string NUMLOC { get; set; }

		[Column(DbType = "CHAR(1)")]
		public LocGenStatut? STATUT { get; set; }

		[Column(DbType = "CHAR(1)")]
		public string STATUT2 { get; set; }


		[Column(DbType = "CHAR(10)")]
		public string CODECLT { get; set; }

		public string CODEPER1 { get; set; }
		public DateTime DATEDEP { get; set; }
		public DateTime DATEFIN { get; set; }
		public DateTime DATERET { get; set; }
		public DateTime DATELOC { get; set; }
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
		[System.ComponentModel.DataAnnotations.Schema.NotMapped]
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

		//[CompositeField(DateMember = nameof(DATEMAJ), TimeMember = nameof(HEUREMAJ))]
		//public DateTime DTMAJ { get; set; }

		//// Relations
		//[Association(KeyMembers = "CODECLT")]
		//public LocClt Client { get; set; }

		//[Association(KeyMembers = "CODECLT,CODEPER1", RelatedKeyMembers = "CODECLT,CODEPER")]
		//public LocPer Per1 { get; set; }
	}

	class Program
	{
		static void Main(string[] args)
		{
			string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\DATA\\LYON;ServerType=remote;TableType=CDX;TrimTrailingSpaces=True;CharType=OEM";
			//string connectionString = "Data Source=C:\\PGS\\Mini\\Data;ServerType=remote;TableType=CDX;TrimTrailingSpaces=True;CharType=OEM";

			var policy = new EntityPolicy();
			var provider = AdvantageQueryProvider.Create(connectionString, policy);
			provider.Log = Console.Out;
			provider.EnableQueryTiming = false;

			policy.IncludeWith<LocGen>(l => l.Articles);

			var query = provider.GetTable<LocGen>()
				//.Where(lg=>lg.Articles.Count > 5)
				.Where(lg => lg.DTDEP != null)
				.Take(1)
				.ToList();



			//provider.GetTable<LocGen>().InsertOrUpdate(new LocGen
			//{
			//	NUMLOC = "TEST00001",
			//	STATUT = null
			//});

			//var res = provider.GetTable<LocGen>()
			//	//.Where(lg => lg.STATUT == LocGenStatut.Retour)
			//	.ToList();

			//foreach (var lg in res)
			//{
			//	if (lg.STATUT.HasValue)
			//	{
			//		Console.WriteLine($"NUMLOC: {lg.NUMLOC}, STATUT: [{((char)lg.STATUT.Value)}], Enum: {lg.STATUT.Value}, Int: {(int)lg.STATUT.Value}");
			//	}
			//	else
			//	{
			//		Console.WriteLine($"NUMLOC: {lg.NUMLOC}, STATUT: [NULL]");
			//	}
			//}


			////provider.GetTable<LocGen>().InsertOrUpdate(new LocGen
			////{
			////	NUMLOC = "TEST00002",
			////	STATUT = LocGenStatut.RetourControle
			////});

			Console.WriteLine("\nAll tests completed. Press any key to exit...");
			Console.ReadKey();
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
