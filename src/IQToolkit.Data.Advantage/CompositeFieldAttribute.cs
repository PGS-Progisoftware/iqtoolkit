// Copyright (c) Microsoft Corporation.  All rights reserved.
// This source code is made available under the terms of the Microsoft Public License (MS-PL)

using System;

namespace IQToolkit.Data.Advantage
{
	/// <summary>
	/// Marks a property as a composite field that combines a date and time column into a single DateTime property.
	/// This property is NOT mapped to a database column - it's a virtual calculated field used only in WHERE clauses.
	/// The CompositeFieldRewriter will automatically transform expressions like "DTDEP > cutoff" into proper
	/// date/time comparison logic: "(DATEDEP > cutoffDate) OR (DATEDEP = cutoffDate AND HEUREDEP > cutoffTime)".
	/// </summary>
	/// <example>
	/// <code>
	/// public class MyEntity
	/// {
	///     public DateTime DATEDEP { get; set; }      // Database column: Date
	///     public string HEUREDEP { get; set; }       // Database column: Char(5) time as "HH:mm"
	///     
	///     [CompositeField(DateMember = nameof(DATEDEP), TimeMember = nameof(HEUREDEP))]
	///     public DateTime DTDEP { get; set; }        // Virtual field - NOT in database
	/// }
	/// 
	/// // Usage in LINQ:
	/// query.Where(e => e.DTDEP > DateTime.Now);  // Works! Automatically rewritten
	/// </code>
	/// </example>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class CompositeFieldAttribute : Attribute
	{
		/// <summary>
		/// The name of the DateTime property that holds the date portion.
		/// </summary>
		public string DateMember { get; set; }

		/// <summary>
		/// The name of the string property that holds the time portion (e.g., "HH:mm" format).
		/// </summary>
		public string TimeMember { get; set; }
	}
}
