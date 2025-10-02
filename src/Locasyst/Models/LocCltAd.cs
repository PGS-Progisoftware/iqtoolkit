using System;
using System.Collections.Generic;
using IQToolkit.Data.Mapping;

namespace Locasyst.Models
{
    [Table(Name = "LocCltAd")]
    public class LocCltAd
    {
        [Column(DbType = "CHAR(10)")]
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
}
