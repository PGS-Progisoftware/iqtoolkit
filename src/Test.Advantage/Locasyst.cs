using System;
using System.Collections.Generic;
using System.Linq;
using IQToolkit;
using IQToolkit.Data;
using IQToolkit.Data.Mapping;

namespace Test.Locasyst
{
	[Table(Name = "LocClt")]
	public class LocClt
	{
		[Column(DbType = "CHAR(10)", IsPrimaryKey = true)]
		public string CODECLT;            // Char(10)
		public string ENTETE;             // Char(10)
		[Column(DbType = "CHAR(40)")]
		public string NOM;                // Char(40)
		public string NOM2;               // Char(38)
		public string ADR1;               // Char(38)
		public string ADR2;               // Char(38)
		public string CP;                 // Char(8)
		public string VILLE;              // Char(32)
		public string PAYS;               // Char(20)
		public string TEL;                // Char(22)
		public string TELNUM;             // Char(16)
		public string FAX;                // Char(22)
		public string INTERNET;           // Char(60)
		public string CONTACT1;           // Char(10)
		public string ADRFACT;            // Char(10)
		public bool CLIENT;               // Logical
		public bool FOUR;                 // Logical
		public string CODECLTFOU;         // Char(15)
		public string CODECOMPTA;         // Char(20)
		public string CODECOMPTF;         // Char(20)
		public string CODEGROUPE;         // Char(10)
		public bool GROUPE;               // Logical
		public string REGLTDELAI;         // Char(10)
		public string REGLTMODE;          // Char(10)
		public string REGLTDELF;          // Char(10)
		public string REGLTMODF;          // Char(10)
		public int REMISE1;               // Numeric(3,0)
		public int REMISE2;
		public int REMISE3;
		public int REMISE4;
		public int REMISE5;
		public int REMISE6;
		public int REMISEP;
		public int REMISEV;
		public int REMISET;
		public string TARIF;              // Char(1)
		public string BANQUE;             // Char(5)
		public string GUICHET;            // Char(5)
		public string NOCPTE;             // Char(11)
		public string RIB;                // Char(2)
		public string DOMICIL;            // Char(25)
		public string IBAN;               // Char(35)
		public string BIC;                // Char(11)
		public int ENCOURS;               // Numeric(7,0)
		public string RISQUE;             // Char(1)
		public int ACOMPTETX;             // Numeric(3,0)
		public bool SANSASSUR;            // Logical
		public bool SANSTVA;              // Logical
		public string DEVISE;             // Char(3)
		public bool EURO;                 // Logical
		public string SECTEUR;            // Char(10)
		public string SSECTEUR;           // Char(10)
		public string TYPEPART;           // Char(20)
		public bool PARTDEFAUT;           // Logical
		public bool PARTPRESTA;           // Logical
		public bool PARTTRANS;            // Logical
		public string MTPART;             // Char(7)
		public string NUMTVA;             // Char(16)
		public string SIRET;              // Char(20)
		public string CODEAPE;            // Char(6)
		public bool CAUTIONOBL;           // Logical
		public string CAUTIONBNQ;         // Char(25)
		public int CAUTIONMT;             // Numeric(6,0)
		public string CAUTIONCHQ;         // Char(10)
		public DateTime? CAUTIONDAT;      // Date
		public string CODEFACT;           // Char(10)
		public string CODEBNQREM;         // Char(10)
		public bool WEBACCES;             // Logical
		public string WEBPASSE;           // Char(20)
		public bool FACTDEMAT;            // Logical
		public string LONGITUDE;          // Char(11)
		public string LATITUDE;           // Char(11)
		public string SUIVIPAR;           // Char(10)
		public bool INACTIFCLT;           // Logical
		public bool INACTIFOUR;           // Logical
		public DateTime? DATECREAT;       // Date
		public string HEURECREAT;         // Char(5)
		public string INITCREAT;          // Char(10)
		public DateTime? DATEMAJ;         // Date
		public string HEUREMAJ;           // Char(5)
		public string INITMAJ;            // Char(10)
		public bool SUPPORTPGS;           // Logical
		public string OBS;                // Memo
		public string IMPORTARIF;         // Memo

		// Relations
		[Association(Member = "Addresses", KeyMembers = "CODECLT", RelatedKeyMembers = "CODECLT")]
		public IList<LocCltAd> Addresses; // LocCltAd.CODECLT -> LocClt.CODECLT
	}

	public class LocCltAd
	{
		public string CODECLT;    // Char(10)
		public string CODESITE;    // Char(10)
		public string LIBADR;      // Char(38)
		public string ADR1;        // Char(38)
		public string ADR2;        // Char(38)
		public string CP;          // Char(8)
		public string VILLE;       // Char(32)
		public string PAYS;        // Char(20)
		public string TEL;         // Char(22)
		public string FAX;         // Char(22)
		public string CODEPER;     // Char(10)
		public string CONTACT;     // Char(32)
		public string MOBILE;      // Char(22)
		public string EMAIL;       // Char(60)
		public bool ATTENTION;     // Logical
		public string DOCUMENT;    // Memo
		public string OBS;         // Memo

		public LocClt Client;      // back-ref
	}

	public class Locart
	{
		public string CODEART;     // Char(15)
		public string LIBELLE1;    // Char(45)
		public string LIBELLE2;    // Char(45)
		public string GROUPE;      // Char(10)
		public string FAMILLE;     // Char(10)
		public string CATEGORIE;   // Char(10)
		public string GFCORDRE;    // Char(5)
		public string MARQUE;      // Char(15)
		public string LIBELLEUK;   // Char(45)
		public string LIBELLEUK2;  // Char(45)
		public bool REFALIAS;      // Logical
		public bool REFOCCAZ;      // Logical
		public decimal PRXACHAT;   // Numeric(11,3)
		public decimal PRXVENTE;   // Numeric(11,3)
		public decimal PRXLOC;     // Numeric(9,3)
		public decimal PRXLOC2;    // Numeric(9,3)
		public decimal PRXLOC3;    // Numeric(9,3)
		public decimal PRXLOC4;    // Numeric(9,3)
		public decimal PRXMOYJR;   // Numeric(9,3)
		public decimal COUTFIXLOC; // Numeric(9,3)
		public decimal ECOTAXE;    // Numeric(7,2)
		public string CODETVA;     // Char(1)
		public string CODETVAVTE;  // Char(1)
		public int QTELOC;         // Numeric(5,0)
		public int QTEVENTE;       // Numeric(5,0)
		public int QTESTOCK;       // Numeric(5,0)
		public string URL;         // Char(250)
		public string IDUNIQUE;    // Char(8)
		public DateTime? DATEMAJ;  // Date
		public string INITMAJ;     // Char(10)
		public DateTime? DATECREAT;// Date
		public string INITCREAT;   // Char(10)
		public string OBS;         // Memo

		// Relations
		public IList<Locdet> Locdets; // Locdet.CODEART
		public IList<LocImmos> Immos; // LocImmos.CODEART
	}

	public class Locgen
	{
		public string NUMLOC;          // Char(9)
		public string CODECLT;         // Char(10)
		public DateTime? DATEDEP;      // Date
		public DateTime? DATEFIN;      // Date
		public string STATUT;          // Char(1)
		public decimal TOTALHT;        // Numeric(11,2)
		public string AFFAIRE;         // Char(40)
		public bool? AFFINACTIF;       // Logical
		public DateTime? DATECREAT;    // Date
		public string INITCREAT;       // Char(10)
		public DateTime? DATEMAJ;      // Date
		public string INITMAJ;         // Char(10)
		public string OBS;             // Memo

		// Relations
		public LocClt Client;          // CODECLT -> LocClt.CODECLT
		public IList<Locdet> Details;  // NUMLOC -> Locdet.NUMLOC
	}

	public class Locdet
	{
		public string NUMLOC;      // Char(9)
		public string CODEART;     // Char(15)
		public DateTime? DATEDEP;  // Date
		public DateTime? DATERET;  // Date
		public decimal QTELOC;     // Numeric(7,1)
		public decimal QTERET;     // Numeric(7,1)
		public decimal PRXLOC;     // Numeric(11,3)
		public decimal PRXFAC;     // Numeric(11,3)
		public int REMISE;         // Numeric(3,0)
		public string OBS;         // Char(100)

		public Locgen Loc;         // NUMLOC -> Locgen
		public Locart Article;     // CODEART -> Locart
	}

	public class LocImmos
	{
		public string CODEART;     // Char(15)
		public string CODEIMMO;    // Char(10)
		public string NOSERIE;     // Char(20)
		public bool CONTAINER;     // Logical
		public DateTime? DATEACHAT;// Date
		public DateTime? DATEVENTE;// Date
		public decimal PRXACHAT;   // Numeric(11,3)
		public decimal PRXVENTE;   // Numeric(11,3)

		public Locart Article;     // CODEART -> Locart
	}

	public class LocartPx
	{
		public string CODEART;     // Char(15)
		public DateTime? DATETARIF;// Date
		public decimal PRXLOC;     // Numeric(9,3)
		public decimal PRXLOC2;    // Numeric(9,3)
		public decimal PRXLOC3;    // Numeric(9,3)
		public decimal PRXLOC4;    // Numeric(9,3)
		public decimal PRXVENTE;   // Numeric(11,3)
		public decimal PRXACHAT;   // Numeric(11,3)

		public Locart Article;     // CODEART -> Locart
	}

	public class LocArtStat
	{
		public string CODEART;     // Char(15)
		public string PERIODE;     // Char(7)
		public string PERIODE2;    // Char(3)
		public int NBLOC;          // Numeric(5,0)
		public int QTELOC;         // Numeric(6,0)
		public int QTELOCXJR;      // Numeric(6,0)
		public decimal MTLOC;      // Numeric(10,0)
		public int QTESTR;         // Numeric(6,0)
		public int QTESTRXJR;      // Numeric(6,0)
		public decimal MTSTR;      // Numeric(10,0)
		public int QTEDPT;         // Numeric(6,0)
		public int QTEDPTXJR;      // Numeric(6,0)
		public decimal MTDPT;      // Numeric(10,0)
		public decimal QTESTOCK;   // Numeric(8,1)
		public decimal VALSTOCK;   // Numeric(12,0)
		public int QTEPERTE;       // Numeric(6,0)
		public decimal MTPERTE;    // Numeric(10,0)
		public int QTEVENTE;       // Numeric(6,0)
		public decimal MTVENTE;    // Numeric(10,0)
		public int QTECESSION;     // Numeric(6,0)
		public decimal MTCESSION;   // Numeric(10,0)
		public DateTime? DATECALCUL;// Date
		public bool PREVISION;     // Logical
		public DateTime? DATERETMIN;// Date
		public DateTime? DATERETMAX;// Date
		public DateTime? DEBPERIODE;// Date
		public DateTime? FINPERIODE;// Date

		public Locart Article;     // CODEART -> Locart
	}

	public class Locdoc
	{
		public string NUMLOC;      // Char(9)
		public string AFFAIRE;     // Char(15)
		public DateTime? DDOC;     // Date
		public string HDOC;        // Char(5)
		public string SENS;        // Char(1)
		public string TYPEDOC;     // Char(1)
		public string DESCRIPT;    // Char(60)
		public string ORIGINE;     // Char(20)
		public string DESTINAT;    // Char(20)
		public string FICHIER;     // Char(150)

		public Locgen Loc;         // NUMLOC -> Locgen
	}

	public partial class LocasystContext
	{
		private readonly IEntityProvider provider;

		public LocasystContext(IEntityProvider provider)
		{
			this.provider = provider;
		}

		public IEntityProvider Provider => this.provider;

		[Table(Name = "LocClt")]
		[Column(Member = "CODECLT", IsPrimaryKey = true, DbType = "CHAR(10)")]
		[Column(Member = "NOM", DbType = "CHAR(40)")]
		[Association(Member = "Addresses", KeyMembers = "CODECLT", RelatedKeyMembers = "CODECLT")]
		public virtual IEntityTable<LocClt> LocClts => this.provider.GetTable<LocClt>();

		[Table(Name = "LocCltAd")]
		[Column(Member = "CODECLT", DbType = "CHAR(10)")]
		[Column(Member = "CODESITE", DbType = "CHAR(10)")]
		[Association(Member = "Client", KeyMembers = "CODECLT", RelatedKeyMembers = "CODECLT")]
		public virtual IEntityTable<LocCltAd> LocCltAds => this.provider.GetTable<LocCltAd>();

		[Table(Name = "Locart")]
		[Column(Member = "CODEART", IsPrimaryKey = true, DbType = "CHAR(15)")]
		[Column(Member = "LIBELLE1", DbType = "CHAR(45)")]
		[Association(Member = "Locdets", KeyMembers = "CODEART", RelatedKeyMembers = "CODEART")]
		[Association(Member = "Immos", KeyMembers = "CODEART", RelatedKeyMembers = "CODEART")]
		public virtual IEntityTable<Locart> Locarts => this.provider.GetTable<Locart>();

		[Table(Name = "Locgen")]
		[Column(Member = "NUMLOC", IsPrimaryKey = true, DbType = "CHAR(9)")]
		[Column(Member = "CODECLT", DbType = "CHAR(10)")]
		[Association(Member = "Client", KeyMembers = "CODECLT", RelatedKeyMembers = "CODECLT")]
		[Association(Member = "Details", KeyMembers = "NUMLOC", RelatedKeyMembers = "NUMLOC")]
		public virtual IEntityTable<Locgen> Locgens => this.provider.GetTable<Locgen>();

		[Table(Name = "Locdet")]
		[Column(Member = "NUMLOC", DbType = "CHAR(9)")]
		[Column(Member = "CODEART", DbType = "CHAR(15)")]
		[Association(Member = "Loc", KeyMembers = "NUMLOC", RelatedKeyMembers = "NUMLOC")]
		[Association(Member = "Article", KeyMembers = "CODEART", RelatedKeyMembers = "CODEART")]
		public virtual IEntityTable<Locdet> Locdets => this.provider.GetTable<Locdet>();

		[Table(Name = "LocImmos")]
		[Column(Member = "CODEART", DbType = "CHAR(15)")]
		[Column(Member = "CODEIMMO", DbType = "CHAR(10)")]
		[Association(Member = "Article", KeyMembers = "CODEART", RelatedKeyMembers = "CODEART")]
		public virtual IEntityTable<LocImmos> LocImmos => this.provider.GetTable<LocImmos>();

		[Table(Name = "LOCARTPX")]
		[Column(Member = "CODEART", DbType = "CHAR(15)")]
		[Column(Member = "DATETARIF")]
		[Column(Member = "PRXLOC", DbType = "NUMERIC(9,3)")]
		[Column(Member = "PRXLOC2", DbType = "NUMERIC(9,3)")]
		[Column(Member = "PRXLOC3", DbType = "NUMERIC(9,3)")]
		[Column(Member = "PRXLOC4", DbType = "NUMERIC(9,3)")]
		[Column(Member = "PRXVENTE", DbType = "NUMERIC(11,3)")]
		[Column(Member = "PRXACHAT", DbType = "NUMERIC(11,3)")]
		[Association(Member = "Article", KeyMembers = "CODEART", RelatedKeyMembers = "CODEART")]
		public virtual IEntityTable<LocartPx> LocartPxs => this.provider.GetTable<LocartPx>();

		[Table(Name = "LocArtStat")]
		[Column(Member = "CODEART", DbType = "CHAR(15)")]
		[Column(Member = "PERIODE", DbType = "CHAR(7)")]
		[Column(Member = "PERIODE2", DbType = "CHAR(3)")]
		[Association(Member = "Article", KeyMembers = "CODEART", RelatedKeyMembers = "CODEART")]
		public virtual IEntityTable<LocArtStat> LocArtStats => this.provider.GetTable<LocArtStat>();

		[Table(Name = "LOCDOC")]
		[Column(Member = "NUMLOC", DbType = "CHAR(9)")]
		[Column(Member = "AFFAIRE", DbType = "CHAR(15)")]
		[Column(Member = "DDOC")]
		[Column(Member = "HDOC", DbType = "CHAR(5)")]
		[Association(Member = "Loc", KeyMembers = "NUMLOC", RelatedKeyMembers = "NUMLOC")]
		public virtual IEntityTable<Locdoc> Locdocs => this.provider.GetTable<Locdoc>();
	}
}


