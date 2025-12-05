using IQToolkit.Data;
using IQToolkit.Data.Advantage;
using IQToolkit.Data.Mapping;
using PCSLib.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Test.Advantage
{

	//public enum LocGenStatut
	//{
	//	/// <summary>
	//	/// Devis
	//	/// </summary>
	//	Devis = '0',
	//	/// <summary>
	//	/// Reservation
	//	/// </summary>
	//	Reservation = '1',
	//	/// <summary>
	//	/// Location
	//	/// </summary>
	//	Location = '2',
	//	/// <summary>
	//	/// Retour
	//	/// </summary>
	//	Retour = '3',
	//	/// <summary>
	//	/// Retour controle
	//	/// </summary>
	//	RetourControle = '4',
	//	/// <summary>
	//	/// Annulation
	//	/// </summary>
	//	Annulation = '9',
	//	Annulation2 = 'A'
	//}

	//[Table(Name = "LocDet")]
	//public class LocDet
	//{
	//	[MaxLength(9)]
	//	[Column(DbType = "Char(9)")]
	//	public string NUMLOC { get; set; }

	//	[MaxLength(15)]
	//	[Column(DbType = "Char(15)")]
	//	public string CODEART { get; set; }
	//}

	///// <summary>
	///// Test entity mapping for LocGen table.
	///// </summary>
	//[Table(Name = "LocGen")]
	//public class LocationGenerale
	//{
	//	[Column(DbType = "CHAR(9)", IsPrimaryKey = true)]
	//	public string NUMLOC { get; set; }

	//	[MaxLength(10)]
	//	[Column(DbType = "Char(10)")]
	//	public string CODECLT { get; set; }

	//	[Range(-9999999.99, 99999999.99)]
	//	[Column(DbType = "Numeric(11,2)")]
	//	public decimal TOTALHT { get; set; }

	//	[Column(DbType = "Date")]
	//	public DateTime DATEMAJ { get; set; }

	//	[IQToolkit.Data.Mapping.Association(KeyMembers = nameof(CODECLT))]
	//	public LocClient Client { get; set; }
	//}

	//[Table(Name = "LocClt")]
	//public partial class LocClient
	//{

	//	[MaxLength(10)]
	//	[Column(DbType = "Char(10)")]
	//	public string CODECLT { get; set; }

	//	[MaxLength(32)]
	//	[Column(DbType = "Char(32)")]
	//	public string NOM { get; set; }
	//}

		//	[IQToolkit.Data.Mapping.Association(KeyMembers = "NUMLOC")]
		//	public List<LocDet> Articles { get; set; }

		//	[Column(DbType = "CHAR(1)")]
		//	public LocGenStatut? STATUT { get; set; }

		//	[Column(DbType = "CHAR(1)")]
		//	public string STATUT2 { get; set; }


		//	[Column(DbType = "CHAR(10)")]
		//	public string CODECLT { get; set; }

		//	public string CODEPER1 { get; set; }
		//	public DateTime DATEDEP { get; set; }
		//	public DateTime DATEFIN { get; set; }
		//	public DateTime DATERET { get; set; }
		//	public DateTime DATELOC { get; set; }
		//	//public char? STATUT2 { get; set; }

		//	public string AFFAIRE { get; set; }
		//	public DateTime DATECREAT { get; set; }
		//	public string INITCREAT { get; set; }
		//	public DateTime DATEMAJ { get; set; }
		//	public string INITMAJ { get; set; }
		//	public string OBS { get; set; }

		//	[Column(DbType = "CHAR(5)")]
		//	public string HEUREDEP { get; set; }

		//	[Column(DbType = "CHAR(5)")]
		//	public string HEUREMAJ { get; set; }

		//	public string VALIDTECH { get; set; }
		//	public string VALIDCOMM { get; set; }

		//	// Composite DateTime fields - combine date + time columns into single DateTime for queries
		//	private DateTime? _dtdep;
		//	[System.ComponentModel.DataAnnotations.Schema.NotMapped]
		//	[CompositeField(DateMember = nameof(DATEDEP), TimeMember = nameof(HEUREDEP))]
		//	public DateTime DTDEP
		//	{
		//		get
		//		{
		//			if (!_dtdep.HasValue)
		//			{
		//				_dtdep = DATEDEP.Date;
		//				if (Utils.TryParseTime(HEUREDEP, out int h, out int m))
		//				{
		//					_dtdep.Value.AddHours(h);
		//					_dtdep.Value.AddMinutes(m);
		//				}
		//			}
		//			return _dtdep.Value;
		//		}
		//		set
		//		{
		//			DATEDEP = value.Date;
		//			HEUREDEP = value.ToString("HH:mm");
		//		}
		//	}

		//	//[CompositeField(DateMember = nameof(DATEMAJ), TimeMember = nameof(HEUREMAJ))]
		//	//public DateTime DTMAJ { get; set; }

		//	//// Relations
		//	//[Association(KeyMembers = "CODECLT")]
		//	//public LocClt Client { get; set; }

		//	//[Association(KeyMembers = "CODECLT,CODEPER1", RelatedKeyMembers = "CODECLT,CODEPER")]
		//	//public LocPer Per1 { get; set; }
		//}

		//[Table(Name = "LocClt")]
		//public class LocClt
		//{
		//	[Column(IsPrimaryKey = true)]
		//	public bool CLIENT { get; set; }

		//	[MaxLength(10)]
		//	[Column(DbType = "Char(10)")]
		//	public string REGLTDELAI { get; set; }

		//	// Advantage provider-specific filter attribute
		//	[IQToolkit.Data.Mapping.Association(
		//		KeyMembers = nameof(REGLTDELAI), 
		//		RelatedKeyMembers = nameof(LocCode.CODIF))]
		//	[AssociationFilter(Column = nameof(LocCode.TYPE), Value = PCSLib.Data.CodificationTypes.DelaiReglement)]
		//	public LocCode DelaiReglementClient { get; set; }
		//}

		class Program
	{
		static void Main(string[] args)
		{
			string connectionString = "Data Source=C:\\PGS\\GONESS;ServerType=local;TableType=CDX;TrimTrailingSpaces=True;CharType=OEM";

			var provider = new AdvantageQueryProvider(connectionString);
			provider.Log = Console.Out;
			provider.EnableQueryTiming = true;

			var resultsDevis = provider.GetTable<LocArt>()
				.Where(la => la.MODE == LocArtMode.Devis)
				.Take(10)
				.ToList();

			Console.WriteLine($"Found {resultsDevis.Count} records with MODE = Devis ('D' character)");
			foreach (var item in resultsDevis)
			{
				Console.WriteLine($"MODE: {item.MODE} | Enum Name: {Enum.GetName(typeof(LocArtMode), item.MODE)} | Value: {(ushort)item.MODE}");
			}

			var resultsNormal = provider.GetTable<LocArt>()
				.Where(la => la.MODE == LocArtMode.Normal)
				.Take(10)
				.ToList();

			Console.WriteLine($"\nFound {resultsNormal.Count} records with MODE = Normal (space character)");
			foreach (var item in resultsNormal)
			{
				Console.WriteLine($"MODE: {item.MODE} | Enum Name: {Enum.GetName(typeof(LocArtMode), item.MODE)} | Value: {(ushort)item.MODE} | LIBELLE2: {(item.LIBELLE2 == null ? "NULL" : item.LIBELLE2 == "" ? "''" : $"'{item.LIBELLE2}' (len={item.LIBELLE2.Length})")}");
			}

			//Console.ReadKey();


			//var results = provider.GetTable<LocationGenerale>()
			//	.Where(lg => lg.DATEMAJ >= new DateTime(2024, 1, 1))
			//	.Where(lg => lg.TOTALHT > 200)
			//	.OrderBy(lg => lg.TOTALHT)
			//	.Take(10)
			//	.Select(lg => new
			//	{
			//		lg.NUMLOC,
			//		lg.DATEMAJ,
			//		lg.TOTALHT,
			//		PersonName = lg.Client.NOM
			//	});


			//foreach (var item in results)
			//{
			//	Console.WriteLine($"{item.NUMLOC} | {item.DATEMAJ:d} | {item.TOTALHT} | {item.PersonName}");
			//}

			//Console.ReadKey();
		}
	}

	public class LocArt
	{
		/// <summary>
		///Visibilité Devis/Prépa
		/// </summary>
		[Column(DbType = "Char(1)")]
		public LocArtMode MODE { get; set; }

		/// <summary>
		///Description article ligne 1
		/// </summary>
		[MaxLength(35)]
		[Column(DbType = "Char(35)")]
		public string LIBELLE2 { get; set; }
	}

	[CharBacked]
	public enum LocArtMode : ushort
	{
		[Description("Normal")]
		Normal = ' ',
		[Description("Devis")]
		Devis = 'D',
		[Description("Preparation")]
		Prepa = 'P'
	}

	public readonly struct GrilleCoeff
	{
		public readonly double Coef1, Coef2, Coef3, Coef4, Coef5, Coef6;

		public GrilleCoeff(double coef1, double coef2, double coef3, double coef4, double coef5, double coef6)
		{
			Coef1 = coef1;
			Coef2 = coef2;
			Coef3 = coef3;
			Coef4 = coef4;
			Coef5 = coef5;
			Coef6 = coef6;
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
