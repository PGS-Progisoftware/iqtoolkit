using System;
using System.Collections.Generic;
using System.Linq;
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
                // Composite field rewriting is handled by AdvantageMapper.Translate
                // so we don't need to do it here.
                
                // FIRST: Handle nullable .Value member access before any other transformations
                // This converts patterns like nullable.Value.Year => nullable.Year
                expression = NullableValueRemover.Remove(expression);
                
                // Optimize DISTINCT queries with navigation properties
                // When SingletonProjectionRewriter refuses to convert relationships to server-side joins
                // (because IsDistinct=true), we end up selecting all columns from parent tables.
                // This optimizer ensures only necessary columns are selected.
                expression = DistinctColumnOptimizer.Optimize(this.Language, expression);
                
                // NEW: Remove OrderBy from subqueries (Advantage/SQL Server limitation)
                // ORDER BY is invalid in subqueries unless TOP/SKIP is used.
                // We do this BEFORE RedundantSubqueryRemover so that subqueries with OrderBy can be merged.
                expression = OrderByRemover.Remove(expression);

                // Clean up after our rewriter
                expression = UnusedColumnRemover.Remove(expression);
                expression = RedundantColumnRemover.Remove(expression);
                expression = RedundantSubqueryRemover.Remove(expression);
                expression = RedundantJoinRemover.Remove(expression);

                // Fix for Advantage Error 7200: Unable to perform DISTINCT operation on this column: IMPORTARIF/OBS
                // This error occurs when a MEMO/BLOB column is included in a subquery that is part of a DISTINCT operation,
                // even if the column itself is not in the DISTINCT list.
                // We aggressively remove known problematic columns if they are not explicitly projected in the final result.
                // Note: UnusedColumnRemover should handle this, but sometimes fails to remove columns from the source table expression.
                expression = MemoColumnPruner.Prune(expression);
                
                // Proceed with normal translation (binding, optimization, etc.)
                return base.Translate(expression);
            }

            public override string Format(Expression expression)
            {
                // Use the custom AdvantageFormatter to ensure positional parameters
                return AdvantageFormatter.Format(expression, this.Language);
            }

            /// <summary>
            /// Removes .Value property access on Nullable types to allow SQL generator to handle nullable columns directly.
            /// Transforms: nullable.Value.Year => marker that formatter can understand
            /// This is necessary because SQL doesn't have a concept of .Value - you access properties directly on nullable columns.
            /// </summary>
            class NullableValueRemover : DbExpressionVisitor
            {
                public static Expression Remove(Expression expression)
                {
                    return new NullableValueRemover().Visit(expression);
                }

                protected override Expression VisitMemberAccess(MemberExpression m)
                {
                    // Handle pattern: nullable.Value.SomeMember
                    // For example: lg.DateCreation.Value.Year where DateCreation is DateTime?
                    if (m.Expression is MemberExpression inner &&
                        inner.Member.Name == "Value" &&
                        TypeHelper.IsNullableType(inner.Expression.Type))
                    {
                        // Get the underlying non-nullable type (e.g., DateTime from DateTime?)
                        var underlyingType = TypeHelper.GetNonNullableType(inner.Expression.Type);
                        
                        // Check if the member being accessed (e.g., Year) exists on the underlying type (DateTime)
                        if (m.Member.DeclaringType == underlyingType ||
                            (m.Member.DeclaringType != null && underlyingType.IsAssignableFrom(m.Member.DeclaringType)))
                        {
                            // Visit the nullable expression (e.g., lg.DateCreation)
                            var visitedNullable = this.Visit(inner.Expression);
                            
                            // Create a new MemberExpression with the nullable expression but keep the original member
                            // This will have a type mismatch, but the formatter will handle it specially
                            // We use the internal constructor via reflection to bypass validation
                            return CreateMemberAccess(visitedNullable, m.Member, m.Type);
                        }
                    }

                    return base.VisitMemberAccess(m);
                }

                private static Expression CreateMemberAccess(Expression expression, MemberInfo member, Type type)
                {
					var propertyInfo = (PropertyInfo)member;

					// If the expression type doesn't match the property's declaring type,
					// we need to convert (e.g., DateTime? -> DateTime)
					if (expression.Type != propertyInfo.DeclaringType &&
						TypeHelper.IsNullableType(expression.Type))
					{
						var underlyingType = TypeHelper.GetNonNullableType(expression.Type);
						if (underlyingType == propertyInfo.DeclaringType)
						{
							// Add a Convert node to cast DateTime? to DateTime
							// This is what the C# compiler does for nullable.Value
							expression = Expression.Convert(expression, underlyingType);
						}
					}

					return Expression.Property(expression, propertyInfo);
				}
            }

            class OrderByRemover : DbExpressionVisitor
            {
                public static Expression Remove(Expression expression)
                {
                    return new OrderByRemover().Visit(expression);
                }

                protected override Expression VisitSelect(SelectExpression select)
                {
                    // Visit children first
                    select = (SelectExpression)base.VisitSelect(select);

                    // If the FROM clause is a SelectExpression (subquery), check if we can remove its OrderBy
                    if (select.From is SelectExpression subSelect)
                    {
                        // If subquery has OrderBy but no Take/Skip, the OrderBy is redundant and invalid in many SQL dialects
                        if (subSelect.OrderBy != null && subSelect.OrderBy.Count > 0 && subSelect.Take == null && subSelect.Skip == null)
                        {
                            // Remove OrderBy from subquery
                            return new SelectExpression(
                                select.Alias,
                                select.Columns,
                                new SelectExpression(
                                    subSelect.Alias,
                                    subSelect.Columns,
                                    subSelect.From,
                                    subSelect.Where,
                                    null, // Remove OrderBy
                                    subSelect.GroupBy,
                                    subSelect.IsDistinct,
                                    subSelect.Skip,
                                    subSelect.Take,
                                    subSelect.IsReverse
                                ),
                                select.Where,
                                select.OrderBy,
                                select.GroupBy,
                                select.IsDistinct,
                                select.Skip,
                                select.Take,
                                select.IsReverse
                            );
                        }
                    }
                    
                    return select;
                }
            }

            class MemoColumnPruner : DbExpressionVisitor
            {
                public static Expression Prune(Expression expression)
                {
                    return new MemoColumnPruner().Visit(expression);
                }

                protected override Expression VisitSelect(SelectExpression select)
                {
                    select = (SelectExpression)base.VisitSelect(select);
                    
                    // Prune MEMO columns from DISTINCT projections
                    if (select.IsDistinct)
                    {
                        var newColumns = new List<ColumnDeclaration>();
                        bool changed = false;
                        
                        foreach (var col in select.Columns)
                        {
                            if (IsMemo(col.Expression))
                            {
                                changed = true;
                            }
                            else
                            {
                                newColumns.Add(col);
                            }
                        }
                        
                        if (changed)
                        {
                            select = new SelectExpression(
                                select.Alias, 
                                newColumns, 
                                select.From, 
                                select.Where, 
                                select.OrderBy, 
                                select.GroupBy, 
                                select.IsDistinct, 
                                select.Skip, 
                                select.Take, 
                                select.IsReverse
                            );
                        }
                    }

                    // Prune MEMO columns from GROUP BY clauses
                    // REMOVED: Pruning columns from GROUP BY changes the query semantics and leads to incorrect results (e.g. returning all rows if GroupBy becomes empty).
                    // If a Memo column is in GroupBy, let the database throw an error (e.g. 7200) rather than silently changing the query.
                    /*
                    if (select.GroupBy != null && select.GroupBy.Count > 0)
                    {
                        var newGroupBy = new List<Expression>();
                        bool changed = false;

                        foreach (var exp in select.GroupBy)
                        {
                            if (IsMemo(exp))
                            {
                                changed = true;
                            }
                            else
                            {
                                newGroupBy.Add(exp);
                            }
                        }

                        if (changed)
                        {
                            // If we removed columns from GroupBy, we must also remove them from the Select list
                            // to avoid "Column not found in GROUP BY clause" errors.
                            var newColumns = new List<ColumnDeclaration>();
                            foreach (var col in select.Columns)
                            {
                                if (IsMemo(col.Expression))
                                {
                                    // Skip Memo columns in Select list if we are pruning GroupBy
                                }
                                else
                                {
                                    newColumns.Add(col);
                                }
                            }

                            select = new SelectExpression(
                                select.Alias, 
                                newColumns, 
                                select.From, 
                                select.Where, 
                                select.OrderBy, 
                                newGroupBy, 
                                select.IsDistinct, 
                                select.Skip, 
                                select.Take, 
                                select.IsReverse
                            );
                        }
                    }
                    */
                    
                    return select;
                }

                private bool IsMemo(Expression exp)
                {
                    if (exp is ColumnExpression ce && ce.QueryType is SqlQueryType sqt)
                    {
                        if (sqt.SqlType == SqlType.Text || 
                            sqt.SqlType == SqlType.NText || 
                            sqt.SqlType == SqlType.Image ||
                            sqt.SqlType == SqlType.Binary)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
        }
    }
}