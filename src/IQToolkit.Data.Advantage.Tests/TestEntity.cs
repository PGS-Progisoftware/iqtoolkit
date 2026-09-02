using System;
using System.Collections.Generic;
using System.Globalization;
using IQToolkit.Data.Mapping;
using IQToolkit.Data.Advantage;

namespace IQToolkit.Data.Advantage.Tests
{
    [Table(Name="TestTable")]
    public class TestEntity
    {
        [Column(IsPrimaryKey=true)]
        public int Id { get; set; }
        public string Name { get; set; }
        public double Value { get; set; }

        // Backing fields for composite date/time
        public DateTime? DateCol { get; set; }
        
        [Column(DbType="Char(5)")]
        public string TimeCol { get; set; }

        // Composite field
        private DateTime? _compositeDate;
        [CompositeField(DateMember = nameof(DateCol), TimeMember = nameof(TimeCol))]
        public DateTime? CompositeDate 
        {
			get
			{
				if (!_compositeDate.HasValue && DateCol.HasValue)
				{
					_compositeDate = DateCol.Value.Date;
					if (Utils.TryParseTime(TimeCol, out int h, out int m))
					{
						_compositeDate = _compositeDate.Value.AddHours(h);
						_compositeDate = _compositeDate.Value.AddMinutes(m);
					}
				}
				return _compositeDate;
			}
			set
			{
				if (value.HasValue)
				{
					DateCol = value.Value.Date;
					TimeCol = value.Value.ToString("HH:mm");
                    _compositeDate = value;
				}
                else
                {
                    DateCol = null;
                    TimeCol = null;
                    _compositeDate = null;
                }
			}
        }
    }

    [Table(Name="Customers")]
    public class Customer
    {
        [Column(IsPrimaryKey=true)]
        public int CustomerId { get; set; }
        
        [Column(DbType="Char(20)")]
        public string Name { get; set; }
        
        public string City { get; set; }

        [Association(KeyMembers = "CustomerId", RelatedKeyMembers = "CustomerId")]
        public IList<Order> Orders { get; set; }
    }

    [Table(Name="Orders")]
    public class Order
    {
        [Column(IsPrimaryKey=true)]
        public int OrderId { get; set; }
        
        public int CustomerId { get; set; }
        
        public DateTime OrderDate { get; set; }
        
        public decimal Total { get; set; }

        [Association(KeyMembers = "CustomerId", RelatedKeyMembers = "CustomerId", IsForeignKey = true)]
        public Customer Customer { get; set; }

        [Association(KeyMembers = "CustomerId", RelatedKeyMembers = "CustomerId", IsForeignKey = true)]
        [AssociationFilter(Column = "City", Value = "London")]
        public Customer CustomerInLondon { get; set; }
    }

    [Table(Name="CompositeParents")]
    public class CompositeParent
    {
        [Column(IsPrimaryKey=true)]
        public int KeyA { get; set; }

        [Column(IsPrimaryKey=true)]
        public int KeyB { get; set; }

        public string Name { get; set; }

        [Association(KeyMembers = "KeyA,KeyB", RelatedKeyMembers = "ParentKeyA,ParentKeyB")]
        public IList<CompositeChild> Children { get; set; }
    }

    [Table(Name="CompositeChildren")]
    public class CompositeChild
    {
        [Column(IsPrimaryKey=true)]
        public int ChildId { get; set; }

        public int ParentKeyA { get; set; }

        public int ParentKeyB { get; set; }

        public string Data { get; set; }

        [Association(KeyMembers = "ParentKeyA,ParentKeyB", RelatedKeyMembers = "KeyA,KeyB", IsForeignKey = true)]
        public CompositeParent Parent { get; set; }
    }

    /// <summary>
    /// Parent with CHAR FK columns that may be blank (ADS pads spaces).
    /// Mirrors LocGen.LIBLANG / DEVISE → LocCode with AssociationFilter on TYPE.
    /// </summary>
    [Table(Name = "AssocParents")]
    public class AssocParent
    {
        [Column(IsPrimaryKey = true)]
        public int ParentId { get; set; }

        [Column(DbType = "Char(10)")]
        public string LibLang { get; set; }

        [Column(DbType = "Char(10)")]
        public string Devise { get; set; }

        [Column(DbType = "Char(20)")]
        public string NumLoc { get; set; }

        [Association(KeyMembers = nameof(LibLang), RelatedKeyMembers = nameof(AssocCode.Code))]
        [AssociationFilter(nameof(AssocCode.Type), "PAYS")]
        public AssocCode Langue { get; set; }

        [Association(KeyMembers = nameof(Devise), RelatedKeyMembers = nameof(AssocCode.Code))]
        [AssociationFilter(nameof(AssocCode.Type), "DEVISE")]
        public AssocCode DeviseCode { get; set; }

        [Association(KeyMembers = nameof(NumLoc), RelatedKeyMembers = nameof(AssocDetail.NumLoc))]
        public IList<AssocDetail> Details { get; set; }
    }

    [Table(Name = "AssocDetails")]
    public class AssocDetail
    {
        [Column(IsPrimaryKey = true)]
        public int DetailId { get; set; }

        [Column(DbType = "Char(20)")]
        public string NumLoc { get; set; }

        [Column(DbType = "Char(30)")]
        public string Label { get; set; }
    }

    [Table(Name = "AssocCodes")]
    public class AssocCode
    {
        [Column(IsPrimaryKey = true)]
        public int Id { get; set; }

        [Column(DbType = "Char(10)")]
        public string Code { get; set; }

        [Column(DbType = "Char(10)")]
        public string Type { get; set; }

        [Column(DbType = "Char(30)")]
        public string Libelle { get; set; }
    }

    /// <summary>
    /// Entity for CharDateTimeField tests.
    /// DTMAJ is stored as CHAR(12) in "yyyyMMddHHmm" format.
    /// </summary>
    [Table(Name = "CharDateTimeTable")]
    public class CharDateTimeEntity
    {
        [Column(IsPrimaryKey = true)]
        public int Id { get; set; }

        public string Label { get; set; }

        /// <summary>Raw CHAR(12) column storing "yyyyMMddHHmm".</summary>
        [Column(DbType = "Char(12)")]
        public string DTMAJ_RAW { get; set; }

        private DateTime? _dtmaj;

        /// <summary>Virtual property backed by <see cref="DTMAJ_RAW"/>.</summary>
        [CharDateTimeField(Member = nameof(DTMAJ_RAW), Format = "yyyyMMddHHmm")]
        public DateTime? DTMAJ
        {
            get
            {
                if (_dtmaj == null && DTMAJ_RAW != null)
                {
                    var raw = DTMAJ_RAW.Trim();
                    if (raw.Length > 0 &&
                        DateTime.TryParseExact(raw, "yyyyMMddHHmm", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var dt))
                    {
                        _dtmaj = dt;
                    }
                }
                return _dtmaj;
            }
            set
            {
                _dtmaj = value;
                DTMAJ_RAW = value?.ToString("yyyyMMddHHmm");
            }
        }
    }

    public static class Utils
	{
		public static bool TryParseTime(string input, out int hours, out int minutes)
		{
			hours = 0;
			minutes = 0;

			// Check length first (fastest rejection)
			if (input == null || input.Length != 5)
				return false;

			// Check colon position
			if (input[2] != ':')
				return false;

			// Parse hours (2 digits)
			if (!char.IsDigit(input[0]) || !char.IsDigit(input[1]))
				return false;
			hours = (input[0] - '0') * 10 + (input[1] - '0');

			// Parse minutes (2 digits)
			if (!char.IsDigit(input[3]) || !char.IsDigit(input[4]))
				return false;
			minutes = (input[3] - '0') * 10 + (input[4] - '0');

			// Optional: validate ranges
			if (hours > 23 || minutes > 59)
				return false;

			return true;
		}
	}
}
