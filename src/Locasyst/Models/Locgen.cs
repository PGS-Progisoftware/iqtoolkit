using System;
using System.Collections.Generic;
using IQToolkit.Data.Mapping;

namespace Locasyst.Models
{
    [Table(Name = "Locgen")]
    public class Locgen
    {
        [Column(DbType = "CHAR(9)", IsPrimaryKey = true)]
        public string NUMLOC;          // Char(9)
        [Column(DbType = "CHAR(10)")]
        public string CODECLT;         // Char(10)
        [Column(DbType = "CHAR(10)")]
        public string CODEPER1;        // Char(10)
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
        public string HEUREDEP;        // Char(5)

        // Relations
        [Association(Member = "Client", KeyMembers = "CODECLT", RelatedKeyMembers = "CODECLT")]
        public LocClt Client;          // CODECLT -> LocClt.CODECLT
        [Association(Member = "Details", KeyMembers = "NUMLOC", RelatedKeyMembers = "NUMLOC")]
        public IList<Locdet> Details;  // NUMLOC -> Locdet.NUMLOC
    }
}
