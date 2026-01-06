using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using IQToolkit.Data.Common;

namespace IQToolkit.Data.Advantage
{
    /// <summary>
    /// Optimizes DISTINCT queries that select from navigation properties.
    /// 
    /// When you have: .Select(x => x.Secteur.Libelle).Distinct()
    /// The standard IQToolkit rewriters refuse to convert the relationship to a server-side join
    /// because they see IsDistinct = true. This results in selecting all columns from the parent table.
    /// 
    /// This optimizer ensures that when we have a DISTINCT query with a nested projection,
    /// we only select columns that are actually used in the final projection.
    /// </summary>
    public class DistinctColumnOptimizer : DbExpressionVisitor
    {
        private readonly QueryLanguage language;

        private DistinctColumnOptimizer(QueryLanguage language)
        {
            this.language = language;
        }

        public static Expression Optimize(QueryLanguage language, Expression expression)
        {
            return new DistinctColumnOptimizer(language).Visit(expression);
        }

        protected override Expression VisitProjection(ProjectionExpression proj)
        {
            // Check if this is a DISTINCT or GROUP BY select
            if (proj.Select.IsDistinct || (proj.Select.GroupBy != null && proj.Select.GroupBy.Count > 0))
            {
                // Check if the projector accesses a nested projection (relationship)
                var nestedProjection = FindNestedProjection(proj.Projector);
                if (nestedProjection != null)
                {
                    // We have a DISTINCT/GROUP BY query with a nested projection
                    // The nested projection wasn't converted to a server-side join
                    // because SingletonProjectionRewriter refuses when IsDistinct=true
                    
                    // Extract what column we actually need from the nested projection
                    var neededColumn = ExtractNeededColumn(proj.Projector, nestedProjection);
                    if (neededColumn != null)
                    {
                        // Replace the nested projection with a direct column reference
                        // This will cause ColumnProjector to only select that column
                        var optimized = OptimizeProjector(proj.Projector, nestedProjection, neededColumn);
                        if (optimized != proj.Projector)
                        {
                            var visitedSelect = this.Visit(proj.Select);
                            var select = visitedSelect as SelectExpression;
                            if (select != null)
                            {
                                // Re-project with the optimized projector
                                // Use the original alias to avoid breaking references from outer queries
                                var newAlias = select.Alias;
                                var existingAliases = GatherAliases(select.From);
                                var pc = ColumnProjector.ProjectColumns(
                                    this.language,
                                    ProjectionAffinity.Server,
                                    optimized,
                                    null,
                                    newAlias,
                                    existingAliases);

                                var newSelect = new SelectExpression(
                                    newAlias,
                                    pc.Columns,
                                    select.From,
                                    select.Where,
                                    select.OrderBy,
                                    select.GroupBy,
                                    select.IsDistinct,
                                    select.Skip,
                                    select.Take,
                                    select.IsReverse
                                );

                                return new ProjectionExpression(newSelect, pc.Projector, proj.Aggregator);
                            }
                        }
                    }
                }
            }

            var projector = this.Visit(proj.Projector);
            var visitedSelect2 = this.Visit(proj.Select);
            var select2 = visitedSelect2 as SelectExpression;

            if (projector != proj.Projector || select2 != proj.Select)
            {
                return new ProjectionExpression(select2 ?? proj.Select, projector, proj.Aggregator);
            }
            return proj;
        }

        private ProjectionExpression FindNestedProjection(Expression expression)
        {
            if (expression == null)
                return null;

            if (expression is OuterJoinedExpression oj)
            {
                return FindNestedProjection(oj.Expression);
            }

            if (expression is ProjectionExpression proj)
            {
                return proj;
            }

            if (expression is MemberExpression m)
            {
                return FindNestedProjection(m.Expression);
            }

            if (expression is EntityExpression entity)
            {
                return FindNestedProjection(entity.Expression);
            }

            if (expression is MemberInitExpression minit)
            {
                foreach (var binding in minit.Bindings.OfType<MemberAssignment>())
                {
                    var found = FindNestedProjection(binding.Expression);
                    if (found != null)
                        return found;
                }
            }

            if (expression is NewExpression nex)
            {
                foreach (var arg in nex.Arguments)
                {
                    var found = FindNestedProjection(arg);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private ColumnExpression ExtractNeededColumn(Expression projector, ProjectionExpression nestedProj)
        {
            // The projector might be accessing a property on the nested projection's projector
            // e.g., x.Secteur.Libelle where nestedProj.Projector is the Secteur entity
            // and we're accessing .Libelle on it

            if (projector is OuterJoinedExpression oj)
            {
                return ExtractNeededColumn(oj.Expression, nestedProj);
            }

            if (projector is MemberExpression m)
            {
                if (m.Expression == nestedProj)
                {
                    // We're accessing a member directly on the nested projection
                    return FindColumnForMember(nestedProj.Projector, m.Member.Name);
                }
                
                // Recursive check for nested member access
                var inner = ExtractNeededColumn(m.Expression, nestedProj);
                if (inner != null)
                {
                    // This is more complex than a single column, but we might still be able to find it
                    // if the inner part resolved to an entity projector
                    return FindColumnForMember(inner, m.Member.Name);
                }
            }

            return null;
        }

        private ColumnExpression FindColumnForMember(Expression entityProjector, string memberName)
        {
            if (entityProjector == null)
                return null;

            if (entityProjector is EntityExpression entity)
            {
                return FindColumnForMember(entity.Expression, memberName);
            }

            if (entityProjector is MemberInitExpression minit)
            {
                foreach (var binding in minit.Bindings.OfType<MemberAssignment>())
                {
                    if (binding.Member.Name == memberName && binding.Expression is ColumnExpression col)
                    {
                        return col;
                    }
                }
            }

            if (entityProjector is NewExpression nex && nex.Members != null)
            {
                for (int i = 0; i < nex.Members.Count; i++)
                {
                    if (nex.Members[i].Name == memberName && nex.Arguments[i] is ColumnExpression col)
                    {
                        return col;
                    }
                }
            }

            if (entityProjector is OuterJoinedExpression oj)
            {
                return FindColumnForMember(oj.Expression, memberName);
            }

            return null;
        }

        private Expression OptimizeProjector(Expression projector, ProjectionExpression nestedProj, ColumnExpression neededColumn)
        {
            if (projector == null)
                return null;

            // Replace the nested projection access with a direct column reference
            if (projector is OuterJoinedExpression oj)
            {
                var optimized = OptimizeProjector(oj.Expression, nestedProj, neededColumn);
                if (optimized != oj.Expression)
                {
                    return new OuterJoinedExpression(neededColumn, optimized);
                }
            }

            if (projector is MemberExpression m)
            {
                if (m.Expression == nestedProj || (m.Expression is MemberExpression inner && inner.Expression == nestedProj))
                {
                    // Replace with the column directly
                    return neededColumn;
                }
                
                var optimizedInner = OptimizeProjector(m.Expression, nestedProj, neededColumn);
                if (optimizedInner != m.Expression)
                {
                    // If the inner part was optimized to a column, we might just return the column
                    // if this member access was part of the path to the column.
                    // But for now, just returning the column is what we want for simple paths.
                    return neededColumn;
                }
            }

            return projector;
        }

        private static List<TableAlias> GatherAliases(Expression source)
        {
            var aliases = new List<TableAlias>();
            GatherAliases(source, aliases);
            return aliases;
        }

        private static void GatherAliases(Expression source, List<TableAlias> aliases)
        {
            if (source is AliasedExpression ax)
            {
                aliases.Add(ax.Alias);
            }
            else if (source is JoinExpression jx)
            {
                GatherAliases(jx.Left, aliases);
                GatherAliases(jx.Right, aliases);
            }
        }
    }
}

