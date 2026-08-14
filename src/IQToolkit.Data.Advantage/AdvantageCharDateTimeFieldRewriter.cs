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
	/// Rewrites WHERE/ORDER-BY expressions involving <see cref="CharDateTimeFieldAttribute"/> virtual fields
	/// into equivalent comparisons on the backing CHAR column.
	/// Because the format (e.g. "yyyyMMddHHmm") is lexicographically sortable, a simple string comparison
	/// is semantically equivalent to a DateTime comparison.
	///
	/// Example rewrites:
	///   entity.DTMAJ > cutoff        →  String.Compare(entity.DTMAJ_RAW, "202306281335") > 0
	///   entity.DTMAJ == null         →  entity.DTMAJ_RAW == null
	///   entity.DTMAJ.HasValue        →  entity.DTMAJ_RAW != null
	/// </summary>
	public class AdvantageCharDateTimeFieldRewriter : DbExpressionVisitor
	{
		public static Expression Rewrite(Expression expression)
		{
			return new AdvantageCharDateTimeFieldRewriter().Visit(expression);
		}

		protected override Expression VisitBinary(BinaryExpression node)
		{
			if (IsComparisonOperator(node.NodeType))
			{
				// Left is a CharDateTime virtual field
				if (node.Left is MemberExpression leftMember && IsCharDateTimeField(leftMember.Member, out var leftAttr))
				{
					if (TryExtractConstantValue(node.Right, out var rightValue))
					{
						if (rightValue == null)
							return BuildNullComparison(node.NodeType, leftMember, leftAttr);
						if (rightValue is DateTime dt)
							return BuildCharComparison(node.NodeType, leftMember, leftAttr, dt);
					}
				}

				// Right is a CharDateTime virtual field (reversed comparison)
				if (node.Right is MemberExpression rightMember && IsCharDateTimeField(rightMember.Member, out var rightAttr))
				{
					if (TryExtractConstantValue(node.Left, out var leftValue))
					{
						if (leftValue == null)
							return BuildNullComparison(node.NodeType, rightMember, rightAttr);
						if (leftValue is DateTime dt)
							return BuildCharComparison(ReverseOperator(node.NodeType), rightMember, rightAttr, dt);
					}
				}
			}

			return base.VisitBinary(node);
		}

		protected override Expression VisitMemberAccess(MemberExpression m)
		{
			var source = this.Visit(m.Expression);

			// Handle .HasValue on nullable CharDateTimeField
			// e.DTMAJ.HasValue  →  e.DTMAJ_RAW != null
			if (m.Member.Name == "HasValue" &&
				source is MemberExpression innerMember &&
				TypeHelper.IsNullableType(innerMember.Type) &&
				IsCharDateTimeField(innerMember.Member, out var hasValueAttr))
			{
				var charAccess = MakeCharMemberAccess(innerMember.Expression, hasValueAttr);
				if (charAccess != null)
					return Expression.NotEqual(charAccess, Expression.Constant(null, typeof(string)));
			}

			// Pass-through for the virtual property itself (let expanders handle projection)
			if (source != m.Expression)
				return Expression.MakeMemberAccess(source, m.Member);

			return m;
		}

		// ── helpers ──────────────────────────────────────────────────────────────

		private static bool IsComparisonOperator(ExpressionType t) =>
			t == ExpressionType.Equal || t == ExpressionType.NotEqual ||
			t == ExpressionType.GreaterThan || t == ExpressionType.GreaterThanOrEqual ||
			t == ExpressionType.LessThan || t == ExpressionType.LessThanOrEqual;

		private static bool IsCharDateTimeField(MemberInfo member, out CharDateTimeFieldAttribute attr)
		{
			attr = member.GetCustomAttributes(typeof(CharDateTimeFieldAttribute), inherit: false)
				       .OfType<CharDateTimeFieldAttribute>()
				       .FirstOrDefault();
			return attr != null && attr.Member != null;
		}

		private static bool TryExtractConstantValue(Expression expression, out object value)
		{
			value = null;

			while (expression is UnaryExpression uex &&
			       (uex.NodeType == ExpressionType.Convert || uex.NodeType == ExpressionType.ConvertChecked))
			{
				expression = uex.Operand;
			}

			if (expression is ConstantExpression constExpr)
			{
				value = constExpr.Value;
				return true;
			}

			if (expression is MemberExpression memberExpr &&
			    memberExpr.Expression is ConstantExpression closureConst)
			{
				value = memberExpr.Member is FieldInfo fi
					? fi.GetValue(closureConst.Value)
					: ((PropertyInfo)memberExpr.Member).GetValue(closureConst.Value);
				return true;
			}

			return false;
		}

		private static ExpressionType ReverseOperator(ExpressionType op) =>
			op switch
			{
				ExpressionType.GreaterThan        => ExpressionType.LessThan,
				ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
				ExpressionType.LessThan           => ExpressionType.GreaterThan,
				ExpressionType.LessThanOrEqual    => ExpressionType.GreaterThanOrEqual,
				_                                 => op,
			};

		private static MemberExpression MakeCharMemberAccess(Expression entitySource, CharDateTimeFieldAttribute attr)
		{
			var entityType = entitySource.Type;
			var charMember = (MemberInfo)entityType.GetProperty(attr.Member) ?? entityType.GetField(attr.Member);
			if (charMember == null) return null;
			return Expression.MakeMemberAccess(entitySource, charMember);
		}

		private static Expression BuildNullComparison(
			ExpressionType op,
			MemberExpression virtualField,
			CharDateTimeFieldAttribute attr)
		{
			var charAccess = MakeCharMemberAccess(virtualField.Expression, attr);
			if (charAccess == null)
				throw new InvalidOperationException(
					$"CharDateTimeField backing member '{attr.Member}' not found on '{virtualField.Expression.Type.Name}'");

			var nullConst = Expression.Constant(null, typeof(string));

			return op switch
			{
				ExpressionType.Equal    => Expression.Equal(charAccess, nullConst),
				ExpressionType.NotEqual => Expression.NotEqual(charAccess, nullConst),
				_ => throw new NotSupportedException($"Operator '{op}' is not supported for null comparisons with CharDateTimeField"),
			};
		}

		private static Expression BuildCharComparison(
			ExpressionType op,
			MemberExpression virtualField,
			CharDateTimeFieldAttribute attr,
			DateTime value)
		{
			var charAccess = MakeCharMemberAccess(virtualField.Expression, attr);
			if (charAccess == null)
				throw new InvalidOperationException(
					$"CharDateTimeField backing member '{attr.Member}' not found on '{virtualField.Expression.Type.Name}'");

			var format = attr.Format ?? "yyyyMMddHHmm";
			var formatted = value.ToString(format);
			var formattedConst = Expression.Constant(formatted, typeof(string));

			var compareMethod = typeof(string).GetMethod("Compare", new[] { typeof(string), typeof(string) });

			return op switch
			{
				ExpressionType.Equal =>
					Expression.Equal(charAccess, formattedConst),

				ExpressionType.NotEqual =>
					Expression.NotEqual(charAccess, formattedConst),

				ExpressionType.GreaterThan =>
					Expression.GreaterThan(Expression.Call(compareMethod, charAccess, formattedConst), Expression.Constant(0)),

				ExpressionType.GreaterThanOrEqual =>
					Expression.GreaterThanOrEqual(Expression.Call(compareMethod, charAccess, formattedConst), Expression.Constant(0)),

				ExpressionType.LessThan =>
					Expression.LessThan(Expression.Call(compareMethod, charAccess, formattedConst), Expression.Constant(0)),

				ExpressionType.LessThanOrEqual =>
					Expression.LessThanOrEqual(Expression.Call(compareMethod, charAccess, formattedConst), Expression.Constant(0)),

				_ => throw new NotSupportedException($"Operator '{op}' is not supported for CharDateTimeField comparisons"),
			};
		}
	}
}
