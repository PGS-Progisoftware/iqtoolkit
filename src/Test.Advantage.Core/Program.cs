// See https://aka.ms/new-console-template for more information
using Gridify;
using IQToolkit.Data.Advantage;
using PCSLib.Data.DBF;
using PCSLib.Data.DTO;
using PCSLib.Data.Enums;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Test.Advantage.Core
{
    class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        static void Main(string[] args)
        {
#if NET5_0_OR_GREATER
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            Console.WriteLine($"Is64BitProcess: {Environment.Is64BitProcess}");
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ace64.dll");
            IntPtr handle = LoadLibrary(path);
            Console.WriteLine($"LoadLibrary(ace64.dll): {handle}, Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

            string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\Data\\Lyon;ServerType=remote;TableType=CDX;TrimTrailingSpaces=True;CharType=OEM";

            var provider = new AdvantageQueryProvider(connectionString);
            provider.Log = Console.Out;
            provider.EnableQueryTiming = true;
			provider.EnableInboundQueryLogging = true;

			var result = provider.GetTable<LocArt>()
			.Where(a => a.CodeArticle.ToLower() == "1001")
			.ToList();

        }
    }

	public enum LocStatus : ushort
	{
		Devis = 0,
		Reservation = 1,
		Location = 2,
		Retour = 3,
		RetourControle = 4,
		Annulation = 9 //TODO : question
	}

	internal class LocationSummary
	{
		[Display(Name = "Loc#", Order = 1, Description = "Numéro Location")]
		public string NUMLOC { get; set; }

		[Display(AutoGenerateField = false)]
		public LocStatus STATUT { get; set; }

		//[Display(AutoGenerateField = false)]
		//public LocStatus2? STATUT2 { get; set; }

		[Display(Name = "Départ")]
		public DateTime? DATEDEP { get; set; }

		[Display(Name = "Début")]
		public DateTime? DATELOC { get; set; }

		[Display(Name = "Fin")]
		public DateTime? DATEFIN { get; set; }

		[Display(Name = "Retour")]
		public DateTime? DATERET { get; set; }

		[Display(Name = "Client")]
		public string ClientName { get; set; }

		[Display(Name = "Affaire")]
		public string Affaire { get; set; }

		[Display(Name = "Suivi Par")]
		public string SuiviPar { get; set; }

		[Display(Name = "Suivi Tech")]
		public string SuiviTech { get; set; }

		[Display(Name = "Total HT")]
		[DisplayFormat(DataFormatString = "c2")]
		public double TOTALHT { get; set; }

		[Display(AutoGenerateField = false)]
		public string CODEPER1 { get; set; }

		[Display(AutoGenerateField = false)]
		public string ValidationTechnique { get; set; }

		[Display(AutoGenerateField = false)]
		public string ValidationCommerciale { get; set; }

		[Display(AutoGenerateField = false)]
		public bool SousTraitance { get; set; }

		[Display(Name = "ST")]
		public string St { get => SousTraitance ? "✔" : ""; }

		public string StatusLib => STATUT.ToString();

		//private DateTime? _dtdep;
		//[Display(Name = "Départure")]
		//[DisplayFormat(DataFormatString = "dd/MM/yy HH:mm")]
		//public DateTime DTDEP
		//{
		//	get
		//	{
		//		if (!_dtdep.HasValue)
		//		{
		//			_dtdep = DATEDEP.Date;
		//			if (TryParseTime(HEUREDEP, out int h, out int m))
		//			{
		//				_dtdep = _dtdep.Value.AddHours(h).AddMinutes(m);
		//			}
		//		}
		//		return _dtdep.Value;
		//	}
		//}
	}
}