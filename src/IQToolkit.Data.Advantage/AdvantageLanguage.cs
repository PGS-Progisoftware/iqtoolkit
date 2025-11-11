using System;
using System.Linq.Expressions;
using System.Reflection;
using IQToolkit.Data.Common;
using IQToolkit.Data;

namespace IQToolkit.Data.Advantage
{
    public class AdvantageLanguage : QueryLanguage
    {
        private static readonly SqlTypeSystem typeSystem = new SqlTypeSystem();

        public AdvantageLanguage() { }

        public override QueryTypeSystem TypeSystem => typeSystem;

        public override string Quote(string name)
        {
            // Advantage uses [name] quoting like SQL Server
            if (name.StartsWith("[") && name.EndsWith("]"))
                return name;
            return "[" + name + "]";
        }

        public override Expression GetGeneratedIdExpression(MemberInfo member)
        {
            // Advantage SQL: SELECT @@IDENTITY
            return new FunctionExpression(TypeHelper.GetMemberType(member), "@@IDENTITY", null);
        }

        public override QueryLinguist CreateLinguist(QueryTranslator translator)
        {
            return new AdvantageLinguist(this, translator);
        }

        class AdvantageLinguist : QueryLinguist
        {
            public AdvantageLinguist(AdvantageLanguage language, QueryTranslator translator)
                : base(language, translator)
            {
            }

            public override Expression Translate(Expression expression)
            {
                // First, rewrite composite fields BEFORE any other translation
                // This ensures that by the time QueryBinder runs, all composite field 
                // references have been replaced with their underlying date/time field references
                expression = AdvantageCompositeFieldRewriter.Rewrite(expression);
                
                // Then proceed with normal translation (binding, optimization, etc.)
                return base.Translate(expression);
            }

            public override string Format(Expression expression)
            {
                // Use the custom AdvantageFormatter to ensure positional parameters
                return AdvantageFormatter.Format(expression, this.Language);
            }
        }
    }
}