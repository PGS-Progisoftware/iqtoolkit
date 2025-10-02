using System;
using System.Collections.Generic;
using IQToolkit.Data.Mapping;

namespace Locasyst.Models
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
}
