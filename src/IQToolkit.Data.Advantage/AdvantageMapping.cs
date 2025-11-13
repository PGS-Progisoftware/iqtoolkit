using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using IQToolkit.Data.Common;
using IQToolkit.Data.Mapping;

namespace IQToolkit.Data.Advantage
{
	/// <summary>
	/// Advantage-specific mapping that handles CompositeField properties.
	/// Composite fields combine date and time columns into a single DateTime property.
	/// </summary>
	public class AdvantageMapping : AttributeMapping
	{
		public AdvantageMapping(Type contextType = null) 
			: base(contextType)
		{
		}

		/// <summary>
		/// Composite field properties are NOT database columns.
		/// </summary>
		public override bool IsColumn(MappingEntity entity, MemberInfo member)
		{
			if (HasCompositeFieldAttribute(member))
				return false;

			return base.IsColumn(entity, member);
		}

		public override QueryMapper CreateMapper(QueryTranslator translator)
		{
			return new AdvantageMapper(this, translator);
		}

		private static bool HasCompositeFieldAttribute(MemberInfo member)
		{
			return member.GetCustomAttributes(typeof(CompositeFieldAttribute), true).Length > 0;
		}

		private static CompositeFieldAttribute GetCompositeFieldAttribute(MemberInfo member)
		{
			var attrs = member.GetCustomAttributes(typeof(CompositeFieldAttribute), true);
			return attrs.Length > 0 ? (CompositeFieldAttribute)attrs[0] : null;
		}

		/// <summary>
		/// Custom mapper that handles composite fields in WHERE clauses and SELECT projections.
		/// Also supports association filters from AdvantageEntityPolicy.
		/// </summary>
		private class AdvantageMapper : AdvancedMapper
		{
			private readonly AdvantageMapping _mapping;

			public AdvantageMapper(AdvantageMapping mapping, QueryTranslator translator)
				: base(mapping, translator)
			{
				_mapping = mapping;
			}

			public override Expression Translate(Expression expression)
			{
				// Step 1: Rewrite composite field comparisons (e.g., DTDEP > x) into date/time logic
				expression = AdvantageCompositeFieldRewriter.Rewrite(expression);

				// Step 2: Normal translation (binding, optimization, etc.)
				expression = base.Translate(expression);
				
				// Step 3: Expand composite field accesses in SELECT to underlying columns
				expression = CompositeFieldExpander.Expand(expression);

				return expression;
			}

			public override Expression GetMemberExpression(Expression root, MappingEntity entity, MemberInfo member)
			{
				// Check if this is an association relationship with a filter
				if (_mapping.IsAssociationRelationship(entity, member))
				{
					MappingEntity relatedEntity = _mapping.GetRelatedEntity(entity, member);
					ProjectionExpression projection = this.GetQueryExpression(relatedEntity);

					// Build WHERE clause for joining back to 'root'
					var declaredTypeMembers = _mapping.GetAssociationKeyMembers(entity, member).ToList();
					var associatedMembers = _mapping.GetAssociationRelatedKeyMembers(entity, member).ToList();

					Expression where = null;
					for (int i = 0, n = associatedMembers.Count; i < n; i++)
					{
						Expression equal =
							this.GetMemberExpression(projection.Projector, relatedEntity, associatedMembers[i]).Equal(
								this.GetMemberExpression(root, entity, declaredTypeMembers[i])
							);
						where = (where != null) ? where.And(equal) : equal;
					}

					// Apply association filter from AdvantageEntityPolicy if present
					var policy = this.Translator.Police.Policy as AdvantageEntityPolicy;
					if (policy != null)
					{
						var filter = policy.GetAssociationFilter(member);
						if (filter != null)
						{
							// Convert filter lambda to column expressions with proper table alias
							var filterParam = filter.Parameters[0];
							var filterCondition = MemberToColumnRewriter.Rewrite(
								filter.Body,
								filterParam,
								projection.Select.Alias,
								relatedEntity,
								this);

							// Add filter to WHERE clause (becomes part of JOIN ON condition)
							where = (where != null) ? where.And(filterCondition) : filterCondition;
						}
					}

					TableAlias newAlias = new TableAlias();
					var pc = ColumnProjector.ProjectColumns(
						this.Translator.Linguist.Language,
						projection.Projector,
						null,
						newAlias,
						projection.Select.Alias);

					LambdaExpression aggregator = Aggregator.GetAggregator(
						TypeHelper.GetMemberType(member),
						typeof(IEnumerable<>).MakeGenericType(pc.Projector.Type));

					var result = new ProjectionExpression(
						new SelectExpression(newAlias, pc.Columns, projection.Select, where),
						pc.Projector,
						aggregator
					);

					return this.Translator.Police.ApplyPolicy(result, member);
				}

				// Fall back to base implementation for non-associations or unfiltered associations
				return base.GetMemberExpression(root, entity, member);
			}

			/// <summary>
			/// Rewrites member access expressions in a filter lambda to column expressions with the correct table alias.
			/// </summary>
			private class MemberToColumnRewriter : DbExpressionVisitor
			{
				private readonly ParameterExpression parameter;
				private readonly TableAlias alias;
				private readonly MappingEntity entity;
				private readonly AdvantageMapper mapper;

				private MemberToColumnRewriter(
					ParameterExpression parameter,
					TableAlias alias,
					MappingEntity entity,
					AdvantageMapper mapper)
				{
					this.parameter = parameter;
					this.alias = alias;
					this.entity = entity;
					this.mapper = mapper;
				}

				public static Expression Rewrite(
					Expression expression,
					ParameterExpression parameter,
					TableAlias alias,
					MappingEntity entity,
					AdvantageMapper mapper)
				{
					return new MemberToColumnRewriter(parameter, alias, entity, mapper).Visit(expression);
				}

				protected override Expression VisitMemberAccess(MemberExpression m)
				{
					// Convert member access on filter parameter to ColumnExpression
					if (m.Expression == this.parameter)
					{
						if (this.mapper._mapping.IsColumn(this.entity, m.Member))
						{
							var columnName = this.mapper._mapping.GetColumnName(this.entity, m.Member);
							var columnType = this.mapper.GetColumnType(this.entity, m.Member);
							return new ColumnExpression(
								TypeHelper.GetMemberType(m.Member),
								columnType,
								this.alias,
								columnName);
						}
					}
					return base.VisitMemberAccess(m);
				}
			}

			/// <summary>
			/// Build entity expression with underlying date/time columns (not composite fields).
			/// </summary>
			public override EntityExpression GetEntityExpression(Expression root, MappingEntity entity)
			{
				var assignments = new List<EntityAssignment>();

				foreach (MemberInfo mi in _mapping.GetMappedMembers(entity))
				{
					if (_mapping.IsAssociationRelationship(entity, mi))
						continue;

					// For composite fields, include their underlying columns instead
					if (HasCompositeFieldAttribute(mi))
					{
						var attr = GetCompositeFieldAttribute(mi);
						var dateMember = entity.StaticType.GetProperty(attr.DateMember) ?? (MemberInfo)entity.StaticType.GetField(attr.DateMember);
						var timeMember = entity.StaticType.GetProperty(attr.TimeMember) ?? (MemberInfo)entity.StaticType.GetField(attr.TimeMember);
						
						if (!assignments.Any(a => a.Member == dateMember))
						{
							var dateExpr = base.GetMemberExpression(root, entity, dateMember);
							if (dateExpr != null)
								assignments.Add(new EntityAssignment(dateMember, dateExpr));
						}
						
						if (!assignments.Any(a => a.Member == timeMember))
						{
							var timeExpr = base.GetMemberExpression(root, entity, timeMember);
							if (timeExpr != null)
								assignments.Add(new EntityAssignment(timeMember, timeExpr));
						}
						
						continue;
					}

					var me = base.GetMemberExpression(root, entity, mi);
					if (me != null)
						assignments.Add(new EntityAssignment(mi, me));
				}

				return new EntityExpression(entity, this.BuildEntityExpression(entity, assignments));
			}
		}

		/// <summary>
		/// Expands composite field member accesses in SELECT projections.
		/// Replaces lg.DTDEP with new LocGen { DATEDEP = ..., HEUREDEP = ... }.DTDEP
		/// so both underlying columns are selected and the getter is called client-side.
		/// </summary>
		private class CompositeFieldExpander : DbExpressionVisitor
		{
			public static Expression Expand(Expression expression)
			{
				return new CompositeFieldExpander().Visit(expression);
			}

			protected override Expression VisitMemberAccess(MemberExpression m)
			{
				var source = this.Visit(m.Expression);
				
				if (source != null && 
				    source.NodeType == (ExpressionType)DbExpressionType.Entity && 
				  HasCompositeFieldAttribute(m.Member))
				{
					var entityExpr = (EntityExpression)source;
					var attr = GetCompositeFieldAttribute(m.Member);
					
					var dateMember = entityExpr.Entity.StaticType.GetProperty(attr.DateMember) ?? 
						(MemberInfo)entityExpr.Entity.StaticType.GetField(attr.DateMember);
					var timeMember = entityExpr.Entity.StaticType.GetProperty(attr.TimeMember) ?? 
						(MemberInfo)entityExpr.Entity.StaticType.GetField(attr.TimeMember);
					
					var dateExpr = FindMemberInEntity(entityExpr.Expression, dateMember);
					var timeExpr = FindMemberInEntity(entityExpr.Expression, timeMember);
					
					if (dateExpr != null && timeExpr != null)
					{
						// Create a minimal entity with just the two columns, then access the composite field
						var minimalEntity = Expression.MemberInit(
							Expression.New(entityExpr.Entity.RuntimeType),
							Expression.Bind(dateMember, dateExpr),
							Expression.Bind(timeMember, timeExpr)
						);
						
						return Expression.MakeMemberAccess(minimalEntity, m.Member);
					}
				}
				
				if (source != m.Expression)
					return Expression.MakeMemberAccess(source, m.Member);
					
				return m;
			}
			
			private Expression FindMemberInEntity(Expression entityExpression, MemberInfo member)
			{
				if (entityExpression is MemberInitExpression minit)
				{
					foreach (var binding in minit.Bindings.OfType<MemberAssignment>())
					{
						if (binding.Member.Name == member.Name)
							return binding.Expression;
					}
				}
				else if (entityExpression is NewExpression nex && nex.Members != null)
				{
					for (int i = 0; i < nex.Members.Count; i++)
					{
						if (nex.Members[i].Name == member.Name)
							return nex.Arguments[i];
					}
				}
				
				return null;
			}
		}
	}
}
