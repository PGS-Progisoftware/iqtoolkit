using System;
using System.IO;
using System.Linq.Expressions;
using IQToolkit;

namespace IQToolkit.Data.Advantage
{
	/// <summary>
	/// Null-safe expression pretty-printer for Advantage inbound query logging.
	/// Keeps defensive handling out of core IQToolkit <see cref="ExpressionWriter"/>.
	/// </summary>
	public class AdvantageExpressionWriter : ExpressionWriter
	{
		protected AdvantageExpressionWriter(TextWriter writer)
			: base(writer)
		{
		}

		public new static string WriteToString(Expression expression)
		{
			var sw = new StringWriter();
			new AdvantageExpressionWriter(sw).Visit(expression);
			return sw.ToString();
		}

		protected override string GetTypeName(Type type)
		{
			if (type == null)
			{
				return "(null)";
			}

			return base.GetTypeName(type);
		}

		protected override Expression VisitMethodCall(MethodCallExpression m)
		{
			if (m.Object != null)
			{
				this.Visit(m.Object);
			}
			else
			{
				var declaringType = m.Method?.DeclaringType;
				this.Write(declaringType != null ? this.GetTypeName(declaringType) : "(unknown)");
			}

			this.Write(".");
			this.Write(m.Method?.Name ?? "(unknown)");
			this.Write("(");
			if (m.Arguments.Count > 1)
				this.WriteLine(Indentation.Inner);
			this.VisitExpressionList(m.Arguments);
			if (m.Arguments.Count > 1)
				this.WriteLine(Indentation.Outer);
			this.Write(")");
			return m;
		}

		protected override NewExpression VisitNew(NewExpression nex)
		{
			this.Write("new ");
			var type = nex.Constructor != null ? nex.Constructor.DeclaringType : nex.Type;
			this.Write(this.GetTypeName(type));
			this.Write("(");
			if (nex.Arguments.Count > 1)
				this.WriteLine(Indentation.Inner);
			this.VisitExpressionList(nex.Arguments);
			if (nex.Arguments.Count > 1)
				this.WriteLine(Indentation.Outer);
			this.Write(")");
			return nex;
		}
	}
}
