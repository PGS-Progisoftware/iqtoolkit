using System;
using IQToolkit.Data.Mapping;

namespace Locasyst.Models
{
    [Table(Name = "LocPer")]
    public class LocPer
    {
        [Column(DbType = "CHAR(10)")]
        public string CODECLT;         // Char(10)
        [Column(DbType = "CHAR(10)")]
        public string CODEPER;         // Char(10)
        [Column(DbType = "CHAR(10)")]
        public string CODESITE;        // Char(10)
        [Column(DbType = "CHAR(10)")]
        public string CIVILITE;        // Char(10)
        [Column(DbType = "CHAR(32)")]
        public string NOM;             // Char(32)
        [Column(DbType = "CHAR(25)")]
        public string PRENOM;          // Char(25)
        [Column(DbType = "CHAR(22)")]
        public string TEL;             // Char(22)
        [Column(DbType = "CHAR(22)")]
        public string FAX;             // Char(22)
        [Column(DbType = "CHAR(22)")]
        public string MOBILE;          // Char(22)
        [Column(DbType = "CHAR(60)")]
        public string EMAIL;           // Char(60)
        [Column(DbType = "CHAR(60)")]
        public string EMAIL2;          // Char(60)
        [Column(DbType = "CHAR(60)")]
        public string EMAIL3;          // Char(60)
        [Column(DbType = "CHAR(60)")]
        public string EMAIL4;          // Char(60)
        [Column(DbType = "CHAR(10)")]
        public string FONCTION;        // Char(10)
        [Column(DbType = "CHAR(30)")]
        public string TITRE;           // Char(30)
        public bool? NEWSLETTER;       // Logical
        public bool? INACTIF;          // Logical
        [Column(DbType = "CHAR(20)")]
        public string CODEALIAS;       // Char(20)
        public DateTime? DATECREAT;    // Date
        [Column(DbType = "CHAR(5)")]
        public string HEURECREAT;      // Char(5)
        [Column(DbType = "CHAR(10)")]
        public string INITCREAT;       // Char(10)
        public DateTime? DATEMAJ;      // Date
        [Column(DbType = "CHAR(5)")]
        public string HEUREMAJ;        // Char(5)
        [Column(DbType = "CHAR(10)")]
        public string INITMAJ;         // Char(10)
        public string OBS;             // Memo
    }
}
