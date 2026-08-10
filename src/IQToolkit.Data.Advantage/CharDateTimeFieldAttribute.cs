// Copyright (c) Microsoft Corporation.  All rights reserved.
// This source code is made available under the terms of the Microsoft Public License (MS-PL)

using System;

namespace IQToolkit.Data.Advantage
{
	/// <summary>
	/// Marks a virtual DateTime? property that is physically stored in a single CHAR column
	/// using a specific date/time format string (e.g. "yyyyMMddHHmm").
	/// The property is NOT mapped to a database column directly — the backing column is named
	/// by <see cref="Member"/> and must be a separate string-typed property on the entity.
	/// The rewriter automatically translates LINQ comparisons on this virtual property into
	/// lexicographic string comparisons on the backing column.
	/// </summary>
	/// <example>
	/// <code>
	/// public class MyEntity
	/// {
	///     [Column(DbType = "Char(12)")]
	///     public string DTMAJ_RAW { get; set; }   // DB column — stores "202306281335"
	///
	///     [CharDateTimeField(Member = nameof(DTMAJ_RAW), Format = "yyyyMMddHHmm")]
	///     public DateTime? DTMAJ { get; set; }    // Virtual — NOT in database
	/// }
	///
	/// // Usage in LINQ:
	/// query.Where(e => e.DTMAJ > DateTime.Now);  // Automatically rewritten
	/// </code>
	/// </example>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class CharDateTimeFieldAttribute : Attribute
	{
		/// <summary>
		/// Name of the string property on the entity that holds the raw CHAR value.
		/// </summary>
		public string Member { get; set; }

		/// <summary>
		/// Format string used to parse/format the DateTime value.
		/// Must produce a lexicographically sortable string. Default: "yyyyMMddHHmm".
		/// </summary>
		public string Format { get; set; } = "yyyyMMddHHmm";
	}
}
