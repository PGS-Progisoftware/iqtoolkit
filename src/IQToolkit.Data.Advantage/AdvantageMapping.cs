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
