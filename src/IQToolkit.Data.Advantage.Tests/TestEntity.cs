using System;
using System.Collections.Generic;
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
