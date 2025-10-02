using System;
using System.Collections.Generic;
using IQToolkit.Data.Mapping;

namespace Locasyst.Models
{
    [Table(Name = "Locdet")]
    public class Locdet
    {
        [Column(DbType = "CHAR(9)")]
        public string NUMLOC;      // Char(9)
        public string CODEART;     // Char(15)
        public decimal QUANTITE;   // Numeric(9,3)
        public decimal PRIX;       // Numeric(11,3)
        public decimal TOTAL;      // Numeric(11,2)
        public string OBS;         // Memo

        public Locgen Locgen;      // back-ref
    }
}
