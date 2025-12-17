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
				// Check left side for composite field
				if (node.Left is MemberExpression leftMember && IsCompositeField(leftMember.Member, out var leftDate, out var leftTime))
				{
					// Case 1: Right is also Composite
					if (node.Right is MemberExpression rightMember && IsCompositeField(rightMember.Member, out var rightDate, out var rightTime))
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
				if (node.Right is MemberExpression rightComposite && IsCompositeField(rightComposite.Member, out var dateMember, out var timeMember))
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

		// VisitUnary and VisitMemberAccess removed to prevent incorrect rewriting of composite fields in projections.
		// The CompositeFieldExpander handles projections correctly by expanding to the underlying columns.


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