// Copyright (c) PGS-Progisoftware
// Custom attribute for Advantage provider to filter associations

using System;

namespace IQToolkit.Data.Advantage
{
	/// <summary>
	/// Specifies a simple equality filter to apply to an association as part of the JOIN ON clause.
	/// This is an Advantage provider-specific attribute that works alongside [Association].
	/// </summary>
	/// <example>
	/// [Association(KeyMembers = "REGLTDELAI")]
	/// [AssociationFilter(Column = "TYPE", Value = "REGLTDELAI")]
	/// public LocCode DelaiReglementClient { get; set; }
	/// 
	/// Generates SQL: LEFT OUTER JOIN LocCode ON (CODIF = REGLTDELAI) AND (TYPE = 'REGLTDELAI')
	/// </example>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
	public class AssociationFilterAttribute : Attribute
	{
		/// <summary>
		/// Column name in the related table to filter on (e.g., "TYPE").
		/// </summary>
		public string Column { get; set; }

		/// <summary>
		/// Value to compare the column against using equality (=).
		/// The value is treated as a string constant and properly quoted in SQL.
		/// </summary>
		public string Value { get; set; }

		public AssociationFilterAttribute()
		{
			
		}

		public AssociationFilterAttribute(string column, string value)
		{
			this.Column = column;
			this.Value = value;
		}
	}
}
