using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DevExpressTests
{
	internal static class Program
	{
		/// <summary>
		/// Point d'entrée principal de l'application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new Form1());
		}
	}

	class LocArt 
		{
		public string CodeArt { get; set; }
		public string Designation { get; set; }
		public string PrixVente { get; set; }
		public string PrixAchat { get; set; }
		public string Stock { get; set; }
		public string Seuil { get; set; }
		public string Famille { get; set; }
		public string SousFamille { get; set; }
		public string TauxTVA { get; set; }
	}

	class LocClt
	{
		public string CodeClt { get; set; }
		public string Nom { get; set; }
		public string Adr1 { get; set; }
		public bool Client { get; set; }

		public string CP { get; set; }

		public string Ville { get; set; }
		public string Tel { get; set; }
		public string Fax { get; set; }

		public string SuiviPar { get; set; }
		public string Siret { get; set; }
		public string Secteur { get; set; }
		public string SSecteur { get; set; }
	}
}
