using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using IQToolkit.Data;
using IQToolkit.Data.Common;

namespace IQToolkit.Data.Advantage
{
    /// <summary>
    /// Extended EntityPolicy for Advantage provider with association filter support.
    /// Adds the ability to filter navigation properties at the database level using JOIN conditions.
    /// </summary>
    public class AdvantageEntityPolicy : EntityPolicy
    {
        private readonly Dictionary<MemberInfo, LambdaExpression> associationFilters = new Dictionary<MemberInfo, LambdaExpression>();

        /// <summary>
        /// Add a filter condition to a singleton association member that is applied as part of the JOIN condition.
        /// This allows filtering related entities at the database level.
        /// </summary>
        /// <typeparam name="TEntity">The entity type containing the association member.</typeparam>
        /// <typeparam name="TRelated">The type of the related entity.</typeparam>
        /// <param name="memberSelector">Lambda selecting the association member (e.g., lc => lc.DelaiReglement).</param>
        /// <param name="filterPredicate">Lambda filtering the related entity (e.g., code => code.TYPE == "REGLTDELAI").</param>
        /// <remarks>
        /// The filter is applied as part of the SQL JOIN ON condition, not as a separate WHERE clause.
        /// This means the navigation property will only be populated if the filter condition is met.
        /// Works with IncludeWith for eager loading.
        /// </remarks>
        public void AssociateWith<TEntity, TRelated>(
          Expression<Func<TEntity, TRelated>> memberSelector,
                  Expression<Func<TRelated, bool>> filterPredicate)
        {
            if (memberSelector == null)
                throw new ArgumentNullException(nameof(memberSelector));
            if (filterPredicate == null)
                throw new ArgumentNullException(nameof(filterPredicate));

            var memberExpr = memberSelector.Body as MemberExpression;
            if (memberExpr == null && memberSelector.Body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                memberExpr = unary.Operand as MemberExpression;
            }

            if (memberExpr == null || memberExpr.Member == null)
                throw new InvalidOperationException("memberSelector must be a simple member access expression");

            this.associationFilters[memberExpr.Member] = filterPredicate;
        }

        /// <summary>
        /// Add a filter condition to an association collection member that is applied as part of the JOIN condition.
        /// This allows filtering related entities at the database level.
        /// </summary>
        /// <typeparam name="TEntity">The entity type containing the association member.</typeparam>
        /// <typeparam name="TRelated">The type of the related entity.</typeparam>
        /// <param name="memberSelector">Lambda selecting the association collection member.</param>
        /// <param name="filterPredicate">Lambda filtering the related entities.</param>
        /// <remarks>
        /// The filter is applied as part of the SQL JOIN ON condition, not as a separate WHERE clause.
        /// Works with IncludeWith for eager loading.
        /// </remarks>
        public void AssociateWith<TEntity, TRelated>(
     Expression<Func<TEntity, IEnumerable<TRelated>>> memberSelector,
            Expression<Func<TRelated, bool>> filterPredicate)
        {
            if (memberSelector == null)
                throw new ArgumentNullException(nameof(memberSelector));
            if (filterPredicate == null)
                throw new ArgumentNullException(nameof(filterPredicate));

            var memberExpr = memberSelector.Body as MemberExpression;
            if (memberExpr == null || memberExpr.Member == null)
                throw new InvalidOperationException("memberSelector must be a simple member access expression");

            this.associationFilters[memberExpr.Member] = filterPredicate;
        }

        /// <summary>
        /// Gets the filter expression for an association member, if one has been defined.
        /// </summary>
        /// <param name="member">The association member to get the filter for.</param>
        /// <returns>The filter lambda expression, or null if no filter is defined.</returns>
        public LambdaExpression GetAssociationFilter(MemberInfo member)
        {
            LambdaExpression filter;
            this.associationFilters.TryGetValue(member, out filter);
            return filter;
        }
    }
}
