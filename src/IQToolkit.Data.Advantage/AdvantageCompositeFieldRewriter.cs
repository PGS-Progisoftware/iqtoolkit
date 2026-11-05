// Copyright (c) Microsoft Corporation.  All rights reserved.
// This source code is made available under the terms of the Microsoft Public License (MS-PL)

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using IQToolkit.Data.Common;

namespace IQToolkit.Data.Advantage
{
	/// <summary>
	/// Rewrites expressions involving composite DateTime fields into expressions 
	/// that compare the separate date and time columns.
	/// 
	/// For example: entity.DTDEP > someDate
	/// Becomes: (entity.DATEDEP > someDate.Date) OR (entity.DATEDEP = someDate.Date AND entity.HEUREDEP > someTime)
	/// </summary>
	public class AdvantageCompositeFieldRewriter : DbExpressionVisitor
	{
		public static Expression Rewrite(Expression expression)
		{
			var rewriter = new AdvantageCompositeFieldRewriter();
			return rewriter.Visit(expression);
		}

		protected override Expression VisitBinary(BinaryExpression node)
		{
			// Check if this is a comparison operation
			if (IsComparisonOperator(node.NodeType))
			{
				// DevExpress writes [Col] Is Null as (object)row.Col == null
				var left = StripConvert(node.Left);
				var right = StripConvert(node.Right);

				// Check left side for composite field
				if (left is MemberExpression leftMember && IsCompositeField(leftMember.Member, out var leftDate, out var leftTime))
				{
					// Case 1: Right is also Composite
					if (right is MemberExpression rightMember && IsCompositeField(rightMember.Member, out var rightDate, out var rightTime))
					{
						return BuildCompositeToCompositeComparison(node.NodeType, leftMember, leftDate, leftTime, rightMember, rightDate, rightTime);
					}

					// Case 2: Right is Constant (or evaluates to one)
					if (TryExtractConstantValue(node.Right, out var constantValue))
					{
						// Special handling for null comparisons
						if (constantValue == null)
						{
							return BuildNullComparison(node.NodeType, leftMember, leftDate);
						}

						if (constantValue is DateTime dt)
						{
							return BuildCompositeComparison(node.NodeType, leftMember, leftDate, leftTime, dt);
						}
					}
				}

				// Check right side for composite field (reversed comparison)
				if (right is MemberExpression rightComposite && IsCompositeField(rightComposite.Member, out var dateMember, out var timeMember))
				{
					if (TryExtractConstantValue(node.Left, out var constantValue))
					{
						// Special handling for null comparisons (reversed)
						if (constantValue == null)
						{
							return BuildNullComparison(node.NodeType, rightComposite, dateMember);
						}

						if (constantValue is DateTime dt)
						{
							var reversedOp = ReverseOperator(node.NodeType);
							return BuildCompositeComparison(reversedOp, rightComposite, dateMember, timeMember, dt);
						}
					}
				}
			}

			// Not a composite field comparison, continue normal visitation
			return base.VisitBinary(node);
		}

		// DevExpress GetDate([Col]) on a nullable column: IIF(col == null, col, (DateTime?)col.Value.Date).
		// A composite has no single SQL value, and in that branch it is null: write null. The date part
		// (col.Value.Date) reads the date column (VisitMemberAccess).
		protected override Expression VisitConditional(ConditionalExpression c)
		{
			if (c.Test is BinaryExpression test
				&& (test.NodeType == ExpressionType.Equal || test.NodeType == ExpressionType.NotEqual)
				&& StripConvert(test.Left) is MemberExpression tested
				&& IsCompositeField(tested.Member, out _, out _)
				&& TryExtractConstantValue(test.Right, out var compared) && compared == null)
			{
				var whenNull = test.NodeType == ExpressionType.Equal ? c.IfTrue : c.IfFalse;
				if (StripConvert(whenNull) is MemberExpression returned
					&& returned.Member == tested.Member
					&& DbExpressionComparer.AreEqual(returned.Expression, tested.Expression))
				{
					var nullValue = Expression.Constant(null, whenNull.Type);
					c = test.NodeType == ExpressionType.Equal
						? Expression.Condition(c.Test, nullValue, c.IfFalse, c.Type)
						: Expression.Condition(c.Test, c.IfTrue, nullValue, c.Type);
				}
			}
			return base.VisitConditional(c);
		}

		private static Expression StripConvert(Expression expression)
		{
			while (expression is UnaryExpression u
				&& (u.NodeType == ExpressionType.Convert || u.NodeType == ExpressionType.ConvertChecked))
			{
				expression = u.Operand;
			}
			return expression;
		}

		protected override Expression VisitMemberAccess(MemberExpression m)
		{
			// First visit the expression part
			var source = this.Visit(m.Expression);
			
			// Handle property access on .Value of nullable composite fields
			// Example: locgen.DTDepartMateriel.Value.Date
			// This is the DevExpress pattern that's causing the error
			if (source is MemberExpression valueAccess &&
			    valueAccess.Member.Name == "Value" &&
			    valueAccess.Expression is MemberExpression compositeMember &&
			    TypeHelper.IsNullableType(compositeMember.Type) &&
			    IsCompositeField(compositeMember.Member, out var dateMemberName, out var timeMemberName))
			{
				// Pattern: composite.Value.Date
				// We need to rewrite this to: underlyingDateColumn.Value.Date
				
				// The property being accessed on .Value (e.g., "Date", "Hour", etc.)
				var property = m.Member;
				
				// Get the underlying date member from the entity
				var entityType = compositeMember.Expression.Type;
				var dateMember = (MemberInfo)entityType.GetProperty(dateMemberName) ?? 
				                 entityType.GetField(dateMemberName);
				
				if (dateMember != null)
				{
					// Access the date column directly
					var dateAccess = Expression.MakeMemberAccess(compositeMember.Expression, dateMember);
					
					// If the date column is nullable and we're accessing a property, 
					// we need to add .Value first
					if (TypeHelper.IsNullableType(dateAccess.Type))
					{
						// dateAccess is DateTime?, need to get .Value then apply property
						var valueProperty = dateAccess.Type.GetProperty("Value");
						var dateValue = Expression.MakeMemberAccess(dateAccess, valueProperty);
						// Now apply the property access (e.g., .Date)
						return Expression.MakeMemberAccess(dateValue, property);
					}
					else
					{
						// dateAccess is DateTime, apply property directly
						return Expression.MakeMemberAccess(dateAccess, property);
					}
				}
			}
			
			// Handle .Value access on nullable composite fields
			// Example: locgen.DTDepartMateriel.Value
			if (m.Member.Name == "Value" && 
			    source is MemberExpression innerMember &&
			    TypeHelper.IsNullableType(innerMember.Type) &&
			    IsCompositeField(innerMember.Member, out var dateMemberName2, out var timeMemberName2))
			{
				// Rewrite composite.Value to underlying date column value
				// Get the date member from the entity
				var entityType = innerMember.Expression.Type;
				var dateMember = (MemberInfo)entityType.GetProperty(dateMemberName2) ?? 
				                 entityType.GetField(dateMemberName2);
				
				if (dateMember != null)
				{
					// Return access to the date column's .Value
					var dateAccess = Expression.MakeMemberAccess(innerMember.Expression, dateMember);
					// If nullable, return .Value, otherwise just the date
					if (TypeHelper.IsNullableType(dateAccess.Type))
					{
						return Expression.MakeMemberAccess(dateAccess, m.Member); // m.Member is "Value"
					}
					else
					{
						return dateAccess; // Already non-nullable
					}
				}
			}
			
			// Handle composite field access in projections  
			// DON'T rewrite here - let CompositeFieldExpander handle it
			// The issue is that CompositeFieldExpander can only expand when the member HAS the attribute
			if (IsCompositeField(m.Member, out var dateField, out var timeField))
			{
				// In projections, we can't do the full expansion here
				// Just pass through and let CompositeFieldExpander handle it
				if (source != m.Expression)
					return Expression.MakeMemberAccess(source, m.Member);
			}
			
			// Reconstruct with visited source
			if (source != m.Expression)
				return Expression.MakeMemberAccess(source, m.Member);
			
			return m;
		}

		// VisitUnary removed to prevent incorrect rewriting of composite fields in projections.
		// The CompositeFieldExpander handles projections correctly by expanding to the underlying columns


		private static bool IsComparisonOperator(ExpressionType nodeType)
		{
			return nodeType == ExpressionType.Equal ||
			   nodeType == ExpressionType.NotEqual ||
				 nodeType == ExpressionType.GreaterThan ||
				   nodeType == ExpressionType.GreaterThanOrEqual ||
		nodeType == ExpressionType.LessThan ||
			nodeType == ExpressionType.LessThanOrEqual;
		}

		private static bool TryExtractConstantValue(Expression expression, out object value)
		{
			value = null;

			while (expression is UnaryExpression uex &&
			       (uex.NodeType == ExpressionType.Convert || uex.NodeType == ExpressionType.ConvertChecked))
			{
				expression = uex.Operand;
			}

			// Handle direct constant
			if (expression is ConstantExpression constExpr)
			{
				value = constExpr.Value;
				return true;
			}

			// Handle member access to a constant (e.g., captured variables in closures)
			if (expression is MemberExpression memberExpr &&
						 memberExpr.Expression is ConstantExpression closureConst)
			{
				var member = memberExpr.Member;
				if (member is FieldInfo field)
				{
					value = field.GetValue(closureConst.Value);
					return true;
				}
				else if (member is PropertyInfo prop)
				{
					value = prop.GetValue(closureConst.Value);
					return true;
				}
			}

			return false;
		}

		private static ExpressionType ReverseOperator(ExpressionType op)
		{
			switch (op)
			{
				case ExpressionType.GreaterThan:
					return ExpressionType.LessThan;
				case ExpressionType.GreaterThanOrEqual:
					return ExpressionType.LessThanOrEqual;
				case ExpressionType.LessThan:
					return ExpressionType.GreaterThan;
				case ExpressionType.LessThanOrEqual:
					return ExpressionType.GreaterThanOrEqual;
				default:
					return op; // Equal and NotEqual are symmetric
			}
		}

		private static bool IsCompositeField(MemberInfo member, out string dateField, out string timeField)
		{
			dateField = null;
			timeField = null;

			// Look for CompositeFieldAttribute
			var attrs = member.GetCustomAttributes(typeof(CompositeFieldAttribute), inherit: false);
			var attr = attrs.FirstOrDefault() as CompositeFieldAttribute;

			if (attr != null)
			{
				dateField = attr.DateMember;
				timeField = attr.TimeMember;

				return dateField != null && timeField != null;
			}

			return false;
		}

		/// <summary>
		/// Builds a null comparison for a composite field by checking only the date component.
		/// Since you can't have a time without a date, checking the date is sufficient.
		/// </summary>
		private Expression BuildNullComparison(
			ExpressionType op,
			MemberExpression compositeMember,
			string dateMemberName)
		{
			var entityType = compositeMember.Expression.Type;
			var parameter = compositeMember.Expression;

			// Get the date backing field member - try property first, then field
			var dateMember = (MemberInfo)entityType.GetProperty(dateMemberName)
				?? entityType.GetField(dateMemberName);

			if (dateMember == null)
			{
				throw new InvalidOperationException(
					$"Composite field date member '{dateMemberName}' not found on type '{entityType.Name}'");
			}

			var dateAccess = Expression.MakeMemberAccess(parameter, dateMember);
			var nullConstant = Expression.Constant(null, typeof(DateTime?));

			// Build the null comparison based on the operator
			switch (op)
			{
				case ExpressionType.Equal:
					// DTDEP == null becomes DATEDEP IS NULL
					return Expression.Equal(dateAccess, nullConstant);

				case ExpressionType.NotEqual:
					// DTDEP != null becomes DATEDEP IS NOT NULL
					return Expression.NotEqual(dateAccess, nullConstant);

				default:
					throw new NotSupportedException($"Operator '{op}' is not supported for null comparisons with composite fields");
			}
		}

		private Expression BuildCompositeToCompositeComparison(
			ExpressionType op,
			MemberExpression leftComposite, string leftDateCol, string leftTimeCol,
			MemberExpression rightComposite, string rightDateCol, string rightTimeCol)
		{
			// Left side access
			var leftEntityType = leftComposite.Expression.Type;
			var leftParam = leftComposite.Expression;
			var leftDateMember = (MemberInfo)leftEntityType.GetProperty(leftDateCol) ?? leftEntityType.GetField(leftDateCol);
			var leftTimeMember = (MemberInfo)leftEntityType.GetProperty(leftTimeCol) ?? leftEntityType.GetField(leftTimeCol);
			var leftDateAccess = Expression.MakeMemberAccess(leftParam, leftDateMember);
			var leftTimeAccess = Expression.MakeMemberAccess(leftParam, leftTimeMember);

			// Right side access
			var rightEntityType = rightComposite.Expression.Type;
			var rightParam = rightComposite.Expression;
			var rightDateMember = (MemberInfo)rightEntityType.GetProperty(rightDateCol) ?? rightEntityType.GetField(rightDateCol);
			var rightTimeMember = (MemberInfo)rightEntityType.GetProperty(rightTimeCol) ?? rightEntityType.GetField(rightTimeCol);
			var rightDateAccess = Expression.MakeMemberAccess(rightParam, rightDateMember);
			var rightTimeAccess = Expression.MakeMemberAccess(rightParam, rightTimeMember);

			// Ensure types match for date comparison (handle nullable)
			Expression rightDateExpr = rightDateAccess;
			if (leftDateAccess.Type != rightDateExpr.Type)
			{
				if (TypeHelper.IsNullableType(leftDateAccess.Type) && !TypeHelper.IsNullableType(rightDateExpr.Type))
				{
					rightDateExpr = Expression.Convert(rightDateExpr, leftDateAccess.Type);
				}
			}

			var stringCompareMethod = typeof(string).GetMethod("Compare", new[] { typeof(string), typeof(string) });

			Expression result;

			switch (op)
			{
				case ExpressionType.Equal:
					result = Expression.AndAlso(
						Expression.Equal(leftDateAccess, rightDateExpr),
						Expression.Equal(leftTimeAccess, rightTimeAccess));
					break;

				case ExpressionType.NotEqual:
					result = Expression.OrElse(
						Expression.NotEqual(leftDateAccess, rightDateExpr),
						Expression.NotEqual(leftTimeAccess, rightTimeAccess));
					break;

				case ExpressionType.GreaterThan:
					result = Expression.OrElse(
						Expression.GreaterThan(leftDateAccess, rightDateExpr),
						Expression.AndAlso(
							Expression.Equal(leftDateAccess, rightDateExpr),
							Expression.GreaterThan(
								Expression.Call(stringCompareMethod, leftTimeAccess, rightTimeAccess),
								Expression.Constant(0))));
					break;

				case ExpressionType.GreaterThanOrEqual:
					result = Expression.OrElse(
						Expression.GreaterThan(leftDateAccess, rightDateExpr),
						Expression.AndAlso(
							Expression.Equal(leftDateAccess, rightDateExpr),
							Expression.GreaterThanOrEqual(
								Expression.Call(stringCompareMethod, leftTimeAccess, rightTimeAccess),
								Expression.Constant(0))));
					break;

				case ExpressionType.LessThan:
					result = Expression.OrElse(
						Expression.LessThan(leftDateAccess, rightDateExpr),
						Expression.AndAlso(
							Expression.Equal(leftDateAccess, rightDateExpr),
							Expression.LessThan(
								Expression.Call(stringCompareMethod, leftTimeAccess, rightTimeAccess),
								Expression.Constant(0))));
					break;

				case ExpressionType.LessThanOrEqual:
					result = Expression.OrElse(
						Expression.LessThan(leftDateAccess, rightDateExpr),
						Expression.AndAlso(
							Expression.Equal(leftDateAccess, rightDateExpr),
							Expression.LessThanOrEqual(
								Expression.Call(stringCompareMethod, leftTimeAccess, rightTimeAccess),
								Expression.Constant(0))));
					break;

				default:
					throw new NotSupportedException($"Operator '{op}' is not supported for composite field comparisons");
			}

			return result;
		}

		private Expression BuildCompositeComparison(
			ExpressionType op,
			MemberExpression compositeMember,
			string dateMemberName,
			string timeMemberName,
			DateTime value)
		{
			var entityType = compositeMember.Expression.Type;
			var parameter = compositeMember.Expression;

			// Get the backing field members - try property first, then field
			var dateMember = (MemberInfo)entityType.GetProperty(dateMemberName)
		?? entityType.GetField(dateMemberName);
			var timeMember = (MemberInfo)entityType.GetProperty(timeMemberName)
			?? entityType.GetField(timeMemberName);

			if (dateMember == null || timeMember == null)
			{
				throw new InvalidOperationException(
			   $"Composite field members '{dateMemberName}' or '{timeMemberName}' not found on type '{entityType.Name}'");
			}

			var dateAccess = Expression.MakeMemberAccess(parameter, dateMember);
			var timeAccess = Expression.MakeMemberAccess(parameter, timeMember);

			Expression dateConst = Expression.Constant(value.Date, typeof(DateTime));
			if (TypeHelper.IsNullableType(dateAccess.Type) && !TypeHelper.IsNullableType(dateConst.Type))
			{
				dateConst = Expression.Convert(dateConst, dateAccess.Type);
			}

			var timeConst = Expression.Constant(value.ToString("HH:mm"), typeof(string));

			// For string comparisons, we need to use String.Compare instead of binary operators
			// String.Compare returns: < 0 if left < right, 0 if equal, > 0 if left > right
			var stringCompareMethod = typeof(string).GetMethod("Compare",
				new[] { typeof(string), typeof(string) });

			// The getter reads an hour it cannot parse (blank, VO's empty "  :  ") as midnight, but SQL sorts it
			// below '00:00'. Against midnight, every hour is at or after it: >= and < compare the date alone, and
			// = takes the hours sorting up to '00:00'. A grid's day filter (>= day AND < next day) is this case.
			if (value.Hour == 0 && value.Minute == 0)
			{
				var timeCompared = Expression.Call(stringCompareMethod, timeAccess, timeConst);
				switch (op)
				{
					case ExpressionType.Equal:
						return Expression.AndAlso(Expression.Equal(dateAccess, dateConst),
							Expression.LessThanOrEqual(timeCompared, Expression.Constant(0)));
					case ExpressionType.NotEqual:
						return Expression.OrElse(Expression.NotEqual(dateAccess, dateConst),
							Expression.GreaterThan(timeCompared, Expression.Constant(0)));
					case ExpressionType.GreaterThanOrEqual:
						return Expression.GreaterThanOrEqual(dateAccess, dateConst);
					case ExpressionType.LessThan:
						return Expression.LessThan(dateAccess, dateConst);
				}
			}

			Expression result;

			// Build comparison based on operator
			switch (op)
			{
				case ExpressionType.Equal:
					// (DATEDEP = date AND HEUREDEP = time)
					result = Expression.AndAlso(
						Expression.Equal(dateAccess, dateConst),
						Expression.Equal(timeAccess, timeConst));
					break;

				case ExpressionType.NotEqual:
					// (DATEDEP != date OR HEUREDEP != time)
					result = Expression.OrElse(
						Expression.NotEqual(dateAccess, dateConst),
						Expression.NotEqual(timeAccess, timeConst));
					break;

				case ExpressionType.GreaterThan:
					// (DATEDEP > date) OR (DATEDEP = date AND HEUREDEP > time)
					// For string: String.Compare(HEUREDEP, time) > 0
					result = Expression.OrElse(
						Expression.GreaterThan(dateAccess, dateConst),
						Expression.AndAlso(
							Expression.Equal(dateAccess, dateConst),
							Expression.GreaterThan(
								Expression.Call(stringCompareMethod, timeAccess, timeConst),
								Expression.Constant(0))));
					break;

				case ExpressionType.GreaterThanOrEqual:
					// (DATEDEP > date) OR (DATEDEP = date AND HEUREDEP >= time)
					// For string: String.Compare(HEUREDEP, time) >= 0
					result = Expression.OrElse(
						Expression.GreaterThan(dateAccess, dateConst),
						Expression.AndAlso(
							Expression.Equal(dateAccess, dateConst),
							Expression.GreaterThanOrEqual(
								Expression.Call(stringCompareMethod, timeAccess, timeConst),
								Expression.Constant(0))));
					break;

				case ExpressionType.LessThan:
					// (DATEDEP < date) OR (DATEDEP = date AND HEUREDEP < time)
					// For string: String.Compare(HEUREDEP, time) < 0
					result = Expression.OrElse(
						Expression.LessThan(dateAccess, dateConst),
						Expression.AndAlso(
							Expression.Equal(dateAccess, dateConst),
							Expression.LessThan(
								Expression.Call(stringCompareMethod, timeAccess, timeConst),
								Expression.Constant(0))));
					break;

				case ExpressionType.LessThanOrEqual:
					// (DATEDEP < date) OR (DATEDEP = date AND HEUREDEP <= time)
					// For string: String.Compare(HEUREDEP, time) <= 0
					result = Expression.OrElse(
						Expression.LessThan(dateAccess, dateConst),
						Expression.AndAlso(
							Expression.Equal(dateAccess, dateConst),
							Expression.LessThanOrEqual(
								Expression.Call(stringCompareMethod, timeAccess, timeConst),
								Expression.Constant(0))));
					break;

				default:
					throw new NotSupportedException($"Operator '{op}' is not supported for composite field comparisons");
			}

			return result;
		}
	}
}