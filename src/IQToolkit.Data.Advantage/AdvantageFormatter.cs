using IQToolkit.Data.Common;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace IQToolkit.Data.Advantage
{
	public class AdvantageFormatter : SqlFormatter
	{
		public AdvantageFormatter(QueryLanguage language) : base(language) { }

		protected override void WriteParameterName(string name)
		{
			// Always use positional parameter marker for Advantage
			this.Write(":" + name);
		}

		public static string Format(Expression expression, QueryLanguage language)
		{
			var formatter = new AdvantageFormatter(language);
			formatter.Visit(expression);
			return formatter.ToString();
		}

		protected override Expression VisitBinary(BinaryExpression b)
		{
			// For equality/inequality comparisons, check if we're comparing with an enum column
			if (b.NodeType == ExpressionType.Equal || b.NodeType == ExpressionType.NotEqual)
			{
				// Unwrap any Convert expressions to get to the actual column or constant
				Expression left = b.Left;
				Expression right = b.Right;
				
				while (left.NodeType == ExpressionType.Convert || left.NodeType == ExpressionType.ConvertChecked)
				{
					left = ((UnaryExpression)left).Operand;
				}
				
				while (right.NodeType == ExpressionType.Convert || right.NodeType == ExpressionType.ConvertChecked)
				{
					right = ((UnaryExpression)right).Operand;
				}

				var leftColumn = left as ColumnExpression;
				var rightColumn = right as ColumnExpression;
				var leftConst = left as ConstantExpression;
				var rightConst = right as ConstantExpression;

				// Determine which side is the column and which is the constant
				ColumnExpression enumColumn = null;
				ConstantExpression constValue = null;

				if (leftColumn != null && IsCharColumn(leftColumn) && rightConst != null)
				{
					enumColumn = leftColumn;
					constValue = rightConst;
				}
				else if (rightColumn != null && IsCharColumn(rightColumn) && leftConst != null)
				{
					enumColumn = rightColumn;
					constValue = leftConst;
				}

				if (enumColumn != null && constValue != null && constValue.Value != null)
				{
					// We have a CHAR(1) column being compared to a constant
					// Check if the constant is an integer (likely a converted enum)
					if (constValue.Value is int intValue)
					{
						// Convert to char and write the comparison directly
						char charValue = (char)intValue;
						
						this.Visit(b.Left);  // Use original left side to preserve any Convert
						this.Write(b.NodeType == ExpressionType.Equal ? " = " : " <> ");
						this.Write("'");
						this.Write(charValue.ToString());
						this.Write("'");
						
						return b;
					}
				}
			}

			return base.VisitBinary(b);
		}

		private bool IsCharColumn(ColumnExpression column)
		{
			// Check if this column is mapped as CHAR(1)
			if (column.QueryType is SqlQueryType sqlType)
			{
				// Check if it's a CHAR type with length 1 (indicating an enum)
				if (sqlType.SqlType == SqlType.Char && sqlType.Length == 1)
				{
					return true;
				}
			}
			return false;
		}

		protected override Expression VisitConstant(ConstantExpression c)
		{
			if (c.Value != null && c.Type.IsEnum)
			{
				// Convert enum to its ASCII character representation
				int enumIntValue = (int)c.Value;
				char enumChar = (char)enumIntValue;

				this.Write("'");
				this.Write(enumChar.ToString());
				this.Write("'");
				return c;
			}

			return base.VisitConstant(c);
		}

		protected override void WriteColumnName(string name)
		{
			// Always quote column names for Advantage
			this.Write(this.Language.Quote(name));
		}

		protected override void WriteTableName(string name)
		{
			// Always quote table names for Advantage
			this.Write(this.Language.Quote(name));
		}

		protected override Expression VisitSelect(SelectExpression select)
		{
			this.AddAliases(select.From);
			this.Write("SELECT ");
			if (select.IsDistinct)
			{
				this.Write("DISTINCT ");
			}

			// Advantage pagination: TOP x [START AT y]. START AT is only valid with TOP.
			// If Skip specified without Take, emit TOP maxint.
			if (select.Take != null || select.Skip != null)
			{
				this.Write("TOP ");
				if (select.Take != null)
				{
					this.Visit(select.Take);
				}
				else
				{
					this.Write(int.MaxValue.ToString());
				}
				if (select.Skip != null)
				{
					this.Write(" START AT ");
					// Advantage START AT is 1-based
					if (select.Skip is ConstantExpression ce && ce.Value is int skipVal)
					{
						this.Write((skipVal + 1).ToString());
					}
					else
					{
						this.Write("(");
						this.Visit(select.Skip);
						this.Write(" + 1)");
					}
				}
				this.Write(" ");
			}

			this.WriteColumns(select.Columns);
			if (select.From != null)
			{
				this.WriteLine(Indentation.Same);
				this.Write("FROM ");
				this.VisitSource(select.From);
			}
			if (select.Where != null)
			{
				this.WriteLine(Indentation.Same);
				this.Write("WHERE ");
				this.VisitPredicate(select.Where);
			}
			if (select.GroupBy != null && select.GroupBy.Count > 0)
			{
				this.WriteLine(Indentation.Same);
				this.Write("GROUP BY ");
				for (int i = 0, n = select.GroupBy.Count; i < n; i++)
				{
					if (i > 0)
					{
						this.Write(", ");
					}
					this.VisitValue(select.GroupBy[i]);
				}
			}
			if (select.OrderBy != null && select.OrderBy.Count > 0)
			{
				this.WriteLine(Indentation.Same);
				this.Write("ORDER BY ");
				for (int i = 0, n = select.OrderBy.Count; i < n; i++)
				{
					OrderExpression exp = select.OrderBy[i];
					if (i > 0)
					{
						this.Write(", ");
					}
					this.VisitValue(exp.Expression);
					if (exp.OrderType != OrderType.Ascending)
					{
						this.Write(" DESC");
					}
				}
			}

			return select;
		}

		protected override Expression VisitMemberAccess(MemberExpression m)
		{
			if (m.Member.DeclaringType == typeof(string))
			{
				switch (m.Member.Name)
				{
					case "Length":
						this.Write("LEN(");
						this.Visit(m.Expression);
						this.Write(")");
						return m;
				}
			}
			else if (m.Member.DeclaringType == typeof(DateTime) || m.Member.DeclaringType == typeof(DateTimeOffset))
			{
				switch (m.Member.Name)
				{
					case "Day":
						this.Write("DAY("); this.Visit(m.Expression); this.Write(")"); return m;
					case "Month":
						this.Write("MONTH("); this.Visit(m.Expression); this.Write(")"); return m;
					case "Year":
						this.Write("YEAR("); this.Visit(m.Expression); this.Write(")"); return m;
					case "Hour":
						this.Write("HOUR("); this.Visit(m.Expression); this.Write(")"); return m;
					case "Minute":
						this.Write("MINUTE("); this.Visit(m.Expression); this.Write(")"); return m;
					case "Second":
						this.Write("SECOND("); this.Visit(m.Expression); this.Write(")"); return m;
				}
			}
			return base.VisitMemberAccess(m);
		}

		protected override Expression VisitMethodCall(MethodCallExpression m)
		{
			// Handle Convert.ToXXX(string) methods - translate to VAL()
			if (m.Method.DeclaringType == typeof(Convert) && m.Arguments.Count == 1)
			{
				var arg = m.Arguments[0];
				
				// Only translate if the argument is a string (column or expression)
				if (arg.Type == typeof(string))
				{
					switch (m.Method.Name)
					{
						case "ToInt16":
						case "ToInt32":
						case "ToInt64":
						case "ToDecimal":
						case "ToDouble":
						case "ToSingle":
						case "ToByte":
						case "ToSByte":
						case "ToUInt16":
						case "ToUInt32":
						case "ToUInt64":
							this.Write("VAL(");
							this.Visit(arg);
							this.Write(")");
							return m;
					}
				}
			}

			if (m.Method.DeclaringType == typeof(string))
			{
				switch (m.Method.Name)
				{
					case "StartsWith":
						this.Write("(");
						this.Visit(m.Object);
						this.Write(" LIKE ");
						this.Visit(m.Arguments[0]);
						this.Write(" + '%')");
						return m;
					case "EndsWith":
						this.Write("(");
						this.Visit(m.Object);
						this.Write(" LIKE '%' + ");
						this.Visit(m.Arguments[0]);
						this.Write(")");
						return m;
					case "Contains":
						this.Write("(");
						this.Visit(m.Object);
						// For FoxPro CDX behavior, LIKE supports % wildcard and + concatenation is fine
						this.Write(" LIKE '%' + ");
						this.Visit(m.Arguments[0]);
						this.Write(" + '%')");
						return m;
					case "Concat":
						IList<Expression> args = m.Arguments;
						if (args.Count == 1 && args[0].NodeType == ExpressionType.NewArrayInit)
						{
							args = ((NewArrayExpression)args[0]).Expressions;
						}
						for (int i = 0, n = args.Count; i < n; i++)
						{
							if (i > 0) this.Write(" + ");
							this.Visit(args[i]);
						}
						return m;
					case "IsNullOrEmpty":
						this.Write("(");
						this.Visit(m.Arguments[0]);
						this.Write(" IS NULL OR ");
						this.Visit(m.Arguments[0]);
						this.Write(" = '')");
						return m;
					case "ToUpper":
						this.Write("UPPER(");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "ToLower":
						this.Write("LOWER(");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "Replace":
						this.Write("REPLACE(");
						this.Visit(m.Object);
						this.Write(", ");
						this.Visit(m.Arguments[0]);
						this.Write(", ");
						this.Visit(m.Arguments[1]);
						this.Write(")");
						return m;
					case "Substring":
						// Advantage SUBSTRING(string, start, count) with 1-based start
						this.Write("SUBSTRING(");
						this.Visit(m.Object);
						this.Write(", ");
						this.Visit(m.Arguments[0]);
						if (m.Arguments.Count == 2)
						{
							this.Write(", ");
							this.Visit(m.Arguments[1]);
						}
						this.Write(")");
						return m;
					case "Remove":
						// Remove(start) => LEFT(str, start)
						// Remove(start, length) => CONCAT(LEFT(str, start), SUBSTRING(str, start + length + 1))
						if (m.Arguments.Count == 1)
						{
							this.Write("LEFT(");
							this.Visit(m.Object);
							this.Write(", ");
							this.Visit(m.Arguments[0]);
							this.Write(")");
						}
						else
						{
							this.Write("(");
							this.Write("("); // CONCAT emulation with +
							this.Write("LEFT(");
							this.Visit(m.Object);
							this.Write(", ");
							this.Visit(m.Arguments[0]);
							this.Write(") + ");
							this.Write("SUBSTRING(");
							this.Visit(m.Object);
							this.Write(", ");
							// start + length + 1 (1-based)
							this.Visit(m.Arguments[0]);
							this.Write(" + ");
							this.Visit(m.Arguments[1]);
							this.Write(" + 1)");
							this.Write(")");
							this.Write(")");
						}
						return m;
					case "IndexOf":
						if (m.Arguments.Count == 1)
						{
							// Zero-based index: POSITION returns 1-based (0 if not found)
							this.Write("(POSITION(");
							this.Visit(m.Arguments[0]);
							this.Write(" IN ");
							this.Visit(m.Object);
							this.Write(") - 1)");
						}
						else
						{
							// With start index: search in substring and adjust
							this.Write("(CASE WHEN POSITION(");
							this.Visit(m.Arguments[0]);
							this.Write(" IN SUBSTRING(");
							this.Visit(m.Object);
							this.Write(", ");
							this.Visit(m.Arguments[1]);
							this.Write(" + 1)) = 0 THEN -1 ELSE (POSITION(");
							this.Visit(m.Arguments[0]);
							this.Write(" IN SUBSTRING(");
							this.Visit(m.Object);
							this.Write(", ");
							this.Visit(m.Arguments[1]);
							this.Write(" + 1)) + ");
							this.Visit(m.Arguments[1]);
							this.Write(" - 1) END)");
						}
						return m;
					case "Trim":
						this.Write("TRIM(");
						this.Visit(m.Object);
						this.Write(")");
						return m;
				}
			}
			else if (m.Method.DeclaringType == typeof(DateTime))
			{
				switch (m.Method.Name)
				{
					case "op_Subtract":
						if (m.Arguments[1].Type == typeof(DateTime))
						{
							// Difference in milliseconds
							this.Write("TIMESTAMPDIFF(SQL_TSI_FRAC_SECOND, ");
							this.Visit(m.Arguments[1]);
							this.Write(", ");
							this.Visit(m.Arguments[0]);
							this.Write(")");
							return m;
						}
						break;
					case "AddYears":
						this.Write("TIMESTAMPADD(SQL_TSI_YEAR,");
						this.Visit(m.Arguments[0]);
						this.Write(",");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "AddMonths":
						this.Write("TIMESTAMPADD(SQL_TSI_MONTH,");
						this.Visit(m.Arguments[0]);
						this.Write(",");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "AddDays":
						this.Write("TIMESTAMPADD(SQL_TSI_DAY,");
						this.Visit(m.Arguments[0]);
						this.Write(",");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "AddHours":
						this.Write("TIMESTAMPADD(SQL_TSI_HOUR,");
						this.Visit(m.Arguments[0]);
						this.Write(",");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "AddMinutes":
						this.Write("TIMESTAMPADD(SQL_TSI_MINUTE,");
						this.Visit(m.Arguments[0]);
						this.Write(",");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "AddSeconds":
						this.Write("TIMESTAMPADD(SQL_TSI_SECOND,");
						this.Visit(m.Arguments[0]);
						this.Write(",");
						this.Visit(m.Object);
						this.Write(")");
						return m;
					case "AddMilliseconds":
						this.Write("TIMESTAMPADD(SQL_TSI_FRAC_SECOND,");
						this.Visit(m.Arguments[0]);
						this.Write(",");
						this.Visit(m.Object);
						this.Write(")");
						return m;
				}
			}
			else if (m.Method.DeclaringType == typeof(Decimal))
			{
				switch (m.Method.Name)
				{
					case "Add":
					case "Subtract":
					case "Multiply":
					case "Divide":
					case "Remainder":
						this.Write("(");
						this.VisitValue(m.Arguments[0]);
						this.Write(" ");
						this.Write(GetOperator(m.Method.Name));
						this.Write(" ");
						this.VisitValue(m.Arguments[1]);
						this.Write(")");
						return m;
					case "Negate":
						this.Write("-");
						this.Visit(m.Arguments[0]);
						this.Write("");
						return m;
					case "Ceiling":
					case "Floor":
						this.Write(m.Method.Name.ToUpper());
						this.Write("(");
						this.Visit(m.Arguments[0]);
						this.Write(")");
						return m;
					case "Round":
						if (m.Arguments.Count == 1)
						{
							this.Write("ROUND(");
							this.Visit(m.Arguments[0]);
							this.Write(", 0)");
							return m;
						}
						else if (m.Arguments.Count == 2 && m.Arguments[1].Type == typeof(int))
						{
							this.Write("ROUND(");
							this.Visit(m.Arguments[0]);
							this.Write(", ");
							this.Visit(m.Arguments[1]);
							this.Write(")");
							return m;
						}
						break;
					case "Truncate":
						this.Write("ROUND(");
						this.Visit(m.Arguments[0]);
						this.Write(", 0, 1)");
						return m;
				}
			}
			else if (m.Method.DeclaringType == typeof(Math))
			{
				switch (m.Method.Name)
				{
					case "Abs":
					case "Acos":
					case "Asin":
					case "Atan":
					case "Cos":
					case "Exp":
					case "Log10":
					case "Sin":
					case "Tan":
					case "Sqrt":
					case "Sign":
					case "Ceiling":
					case "Floor":
						this.Write(m.Method.Name.ToUpper());
						this.Write("(");
						this.Visit(m.Arguments[0]);
						this.Write(")");
						return m;
					case "Atan2":
						this.Write("ATN2(");
						this.Visit(m.Arguments[0]);
						this.Write(", ");
						this.Visit(m.Arguments[1]);
						this.Write(")");
						return m;
					case "Log":
						if (m.Arguments.Count == 1)
						{
							goto case "Log10";
						}
						break;
					case "Pow":
						this.Write("POWER(");
						this.Visit(m.Arguments[0]);
						this.Write(", ");
						this.Visit(m.Arguments[1]);
						this.Write(")");
						return m;
					case "Round":
						if (m.Arguments.Count == 1)
						{
							this.Write("ROUND(");
							this.Visit(m.Arguments[0]);
							this.Write(", 0)");
							return m;
						}
						else if (m.Arguments.Count == 2 && m.Arguments[1].Type == typeof(int))
						{
							this.Write("ROUND(");
							this.Visit(m.Arguments[0]);
							this.Write(", ");
							this.Visit(m.Arguments[1]);
							this.Write(")");
							return m;
						}
						break;
					case "Truncate":
						this.Write("ROUND(");
						this.Visit(m.Arguments[0]);
						this.Write(", 0, 1)");
						return m;
				}
			}
			if (m.Method.Name == "ToString")
			{
				if (m.Object.Type != typeof(string))
				{
					this.Write("CAST(");
					this.Visit(m.Object);
					this.Write(" AS SQL_VARCHAR)");
				}
				else
				{
					this.Visit(m.Object);
				}
				return m;
			}
			else if (!m.Method.IsStatic && m.Method.Name == "CompareTo" && m.Method.ReturnType == typeof(int) && m.Arguments.Count == 1)
			{
				this.Write("(CASE WHEN ");
				this.Visit(m.Object);
				this.Write(" = ");
				this.Visit(m.Arguments[0]);
				this.Write(" THEN 0 WHEN ");
				this.Visit(m.Object);
				this.Write(" < ");
				this.Visit(m.Arguments[0]);
				this.Write(" THEN -1 ELSE 1 END)");
				return m;
			}
			else if (m.Method.IsStatic && m.Method.Name == "Compare" && m.Method.ReturnType == typeof(int) && m.Arguments.Count == 2)
			{
				this.Write("(CASE WHEN ");
				this.Visit(m.Arguments[0]);
				this.Write(" = ");
				this.Visit(m.Arguments[1]);
				this.Write(" THEN 0 WHEN ");
				this.Visit(m.Arguments[0]);
				this.Write(" < ");
				this.Visit(m.Arguments[1]);
				this.Write(" THEN -1 ELSE 1 END)");
				return m;
			}
			return base.VisitMethodCall(m);
		}
	}
}
