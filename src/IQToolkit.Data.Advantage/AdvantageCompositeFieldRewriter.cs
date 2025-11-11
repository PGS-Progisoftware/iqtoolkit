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
				if (node.Left is MemberExpression leftMember)
				{
					if (IsCompositeField(leftMember.Member, out var dateMember, out var timeMember))
					{
						// Extract the constant value from the right side
						var constantValue = ExtractConstantValue(node.Right);

						// Special handling for null comparisons
						if (constantValue == null || node.Right.NodeType == ExpressionType.Constant && ((ConstantExpression)node.Right).Value == null)
						{
							return BuildNullComparison(node.NodeType, leftMember, dateMember);
						}

						if (constantValue is DateTime dt)
						{
							return BuildCompositeComparison(node.NodeType, leftMember, dateMember, timeMember, dt);
						}
					}
				}

				// Check right side for composite field (reversed comparison)
				if (node.Right is MemberExpression rightMember)
				{
					if (IsCompositeField(rightMember.Member, out var dateMember, out var timeMember))
					{
						var constantValue = ExtractConstantValue(node.Left);
						
						// Special handling for null comparisons (reversed)
						if (constantValue == null || node.Left.NodeType == ExpressionType.Constant && ((ConstantExpression)node.Left).Value == null)
						{
							return BuildNullComparison(node.NodeType, rightMember, dateMember);
						}

						if (constantValue is DateTime dt)
						{
							var reversedOp = ReverseOperator(node.NodeType);
							return BuildCompositeComparison(reversedOp, rightMember, dateMember, timeMember, dt);
						}
					}
				}
			}

			// Not a composite field comparison, continue normal visitation
			return base.VisitBinary(node);
		}

		private static bool IsComparisonOperator(ExpressionType nodeType)
		{
			return nodeType == ExpressionType.Equal ||
			   nodeType == ExpressionType.NotEqual ||
				 nodeType == ExpressionType.GreaterThan ||
				   nodeType == ExpressionType.GreaterThanOrEqual ||
		nodeType == ExpressionType.LessThan ||
			nodeType == ExpressionType.LessThanOrEqual;
		}

		private static object ExtractConstantValue(Expression expression)
		{
			// Handle direct constant
			if (expression is ConstantExpression constExpr)
			{
				return constExpr.Value;
			}

			// Handle member access to a constant (e.g., captured variables in closures)
			if (expression is MemberExpression memberExpr &&
						 memberExpr.Expression is ConstantExpression closureConst)
			{
				var member = memberExpr.Member;
				if (member is FieldInfo field)
				{
					return field.GetValue(closureConst.Value);
				}
				else if (member is PropertyInfo prop)
				{
					return prop.GetValue(closureConst.Value);
				}
			}

			return null;
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

			var dateConst = Expression.Constant(value.Date, typeof(DateTime));
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
