using IQToolkit.Data.Advantage;
using IQToolkit.Data.Mapping;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;

namespace Test.Advantage
{
    public class LocGen
    {
		[Column(DbType = "CHAR(9)", IsPrimaryKey = true)]
		public string NUMLOC;          // Char(9)
		[Column(DbType = "CHAR(10)")]
		public string CODECLT;         // Char(10)
		public string CODEPER1;       // Char(10)
		public DateTime DATEDEP;      // Date
		public DateTime DATEFIN;      // Date
		public DateTime DATERET;
		public DateTime DATELOC;
		public ushort STATUT;          // Char(1)
		public char STATUT2;          // Char(1)
		public decimal TOTALHT;        // Numeric(11,2)
		public string AFFAIRE;         // Char(40)
		public DateTime DATECREAT;    // Date
		public string INITCREAT;       // Char(10)
		public DateTime DATEMAJ;      // Date
		public string INITMAJ;         // Char(10)
		public string OBS;             // Memo

		[Column(DbType = "CHAR(5)")]
		public string HEUREDEP;

		public string VALIDTECH { get; set; }
		public string VALIDCOMM { get; set; }

		// Relations
		[IQToolkit.Data.Mapping.Association(KeyMembers = "CODECLT")]
		public LocClt Client;          // CODECLT -> LocClt.CODECLT

		[IQToolkit.Data.Mapping.Association(KeyMembers = "CODECLT,CODEPER1", RelatedKeyMembers = "CODECLT,CODEPER")]
		public LocPer Per1;



		private DateTime? _dtdep;
		public DateTime DTDEP
		{
			get
			{
				if (!_dtdep.HasValue)
				{
					_dtdep = DATEDEP.Date;
					if (TryParseTime(HEUREDEP, out int h, out int m))
					{
						_dtdep.Value.AddHours(h);
						_dtdep.Value.AddMinutes(m);
					}
				}
				return _dtdep.Value;
			}
			set { }
		}

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

	class Program
    {
        static void Main(string[] args)
        {
            // Use local server connection for Advantage with TableType=CDX
            string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\DATA\\LYON;ServerType=remote;TableType=CDX";
            
            var provider = new AdvantageQueryProvider(connectionString);
            provider.Log = Console.Out; // Enable SQL logging
            
            TestAssociationRelationship(provider);
        }
        
        static void TestAssociationRelationship(AdvantageQueryProvider provider)
        {
            Console.WriteLine("Testing Association relationship between LocGen and LocClt");
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var locGenTable = provider.GetTable<LocGen>();
                
                // Test the association by accessing the Client property
                var locGenWithClient = locGenTable
                    .Where(lg => lg.Client.Nom != null)
                    .Select(lg => new { 
                        LocationNumber = lg.NUMLOC,
                        ClientName = lg.Client.Nom 
                    })
                    .Take(10)
                    .ToList();
                
                stopwatch.Stop();
                
                Console.WriteLine($"Association test completed in {stopwatch.ElapsedMilliseconds} ms");
                Console.WriteLine($"Found {locGenWithClient.Count} LocGen records with associated clients");
                
                foreach (var item in locGenWithClient)
                {
                    Console.WriteLine($"  Location: {item.LocationNumber} - Client: {item.ClientName}");
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"Association test failed: {ex.Message}");
            }
        }
    }
}
