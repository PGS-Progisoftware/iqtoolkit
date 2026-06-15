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
                // Step 0: Rewrite Select(nav).Distinct() to explicit Join/SelectMany
                // This avoids QueryBinder generating inefficient SQL (subquery with DISTINCT *)
                // which causes issues with Advantage (MEMO columns) and performance.
                expression = NavigationPropertyRewriter.Rewrite(_mapping, expression);

				// Step 1: Rewrite composite field comparisons (e.g., DTDEP > x) into date/time logic
				expression = AdvantageCompositeFieldRewriter.Rewrite(expression);
				
				// Step 1b: Expand composite fields in SELECT projections BEFORE binding
				// This is critical: composite fields must be replaced with their underlying columns
				// BEFORE QueryBinder creates the projection, otherwise the composite field reference
				// gets baked into the query tree and SqlFormatter can't handle it.
				expression = CompositeFieldProjectionExpander.Expand(expression, _mapping);

				// Step 2: Normal translation (binding, optimization, etc.)
				expression = base.Translate(expression);

				// Step 2b: Rewrite composite field comparisons AGAIN (e.g. inside Where clauses after ProjectTo)
				// This handles cases where composite fields were hidden by projections and are now exposed as MemberAccess on TableExpression
				expression = AdvantageCompositeFieldRewriter.Rewrite(expression);

				// Step 2c: Convert MemberAccess on TableExpression/EntityExpression to ColumnExpression
				// This is needed because Step 2b introduces MemberAccess to underlying columns (Date/Time)
				// but SqlFormatter expects ColumnExpressions.
				expression = Columnizer.Columnize(expression, this);

				// Step 2c2: ORDER BY on composite fields must sort by underlying date/time columns
				expression = OrderByCompositeFieldRewriter.Rewrite(this, expression);
				expression = Columnizer.Columnize(expression, this);
				
				// Step 2d: Fix GROUP BY expressions that reference columns from projections with navigation properties
				// This ensures GROUP BY clauses correctly reference columns from joined tables
				expression = GroupByColumnFixer.Fix(expression);
				
				// Step 3: Expand composite field accesses in SELECT to underlying columns
				expression = CompositeFieldExpander.Expand(expression);
				expression = Columnizer.Columnize(expression, this);

				return expression;
			}

            class Columnizer : DbExpressionVisitor
            {
                private readonly AdvantageMapper mapper;

                private Columnizer(AdvantageMapper mapper)
                {
                    this.mapper = mapper;
                }

                public static Expression Columnize(Expression expression, AdvantageMapper mapper)
                {
                    return new Columnizer(mapper).Visit(expression);
                }

                protected override Expression VisitMemberAccess(MemberExpression m)
                {
                    var basicMapping = mapper.Mapping as BasicMapping;
                    if (basicMapping == null)
                        return base.VisitMemberAccess(m);

                // Check if accessing a member on a TableExpression
                if (m.Expression is TableExpression tex)
                {
                    // DON'T columnize composite fields - let CompositeFieldExpander handle them
                    if (HasCompositeFieldAttribute(m.Member))
                    {
                        return base.VisitMemberAccess(m);
                    }
                    
                    if (basicMapping.IsColumn(tex.Entity, m.Member))
                    {
                        return new ColumnExpression(
                            TypeHelper.GetMemberType(m.Member),
                            mapper.GetColumnType(tex.Entity, m.Member),
                            tex.Alias,
                            basicMapping.GetColumnName(tex.Entity, m.Member)
                        );
                    }
                }
                // Check if accessing a member on an EntityExpression
                else if (m.Expression is EntityExpression ex)
                {
                     // DON'T columnize composite fields - let CompositeFieldExpander handle them
                     if (HasCompositeFieldAttribute(m.Member))
                     {
                         return base.VisitMemberAccess(m);
                     }
                     
                     if (ex.Expression is AliasedExpression aex)
                     {
                         if (basicMapping.IsColumn(ex.Entity, m.Member))
                         {
                            return new ColumnExpression(
                                TypeHelper.GetMemberType(m.Member),
                                mapper.GetColumnType(ex.Entity, m.Member),
                                aex.Alias,
                                basicMapping.GetColumnName(ex.Entity, m.Member)
                            );
                         }
                     }
                     else 
                     {
                         var memberExpr = FindMemberInEntity(ex.Expression, m.Member);
                         if (memberExpr != null)
                         {
                             return this.Visit(memberExpr);
                         }
                     }
                }

                return base.VisitMemberAccess(m);
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
                        return FindMemberInEntity(minit.NewExpression, member);
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

            class OrderByCompositeFieldRewriter : DbExpressionVisitor
            {
                private readonly AdvantageMapper mapper;
                private readonly AdvantageMapping mapping;

                private OrderByCompositeFieldRewriter(AdvantageMapper mapper)
                {
                    this.mapper = mapper;
                    this.mapping = mapper._mapping;
                }

                public static Expression Rewrite(AdvantageMapper mapper, Expression expression)
                {
                    return new OrderByCompositeFieldRewriter(mapper).Visit(expression);
                }

                protected override Expression VisitSelect(SelectExpression select)
                {
                    select = (SelectExpression)base.VisitSelect(select);

                    if (select.OrderBy == null || select.OrderBy.Count == 0)
                        return select;

                    var newOrderings = new List<OrderExpression>();
                    bool changed = false;

                    foreach (var order in select.OrderBy)
                    {
                        if (TryExpandCompositeOrderBy(order, out var expanded))
                        {
                            changed = true;
                            newOrderings.AddRange(expanded);
                        }
                        else
                        {
                            newOrderings.Add(order);
                        }
                    }

                    return changed ? select.SetOrderBy(newOrderings) : select;
                }

                private bool TryExpandCompositeOrderBy(OrderExpression order, out List<OrderExpression> orderings)
                {
                    orderings = null;

                    if (!(order.Expression is MemberExpression compositeAccess))
                        return false;

                    var attr = GetCompositeFieldAttribute(compositeAccess.Member);
                    if (attr == null)
                        return false;

                    if (!TryGetUnderlyingColumnExpressions(compositeAccess, attr, out var dateExpr, out var timeExpr))
                        return false;

                    orderings = new List<OrderExpression>
                    {
                        new OrderExpression(order.OrderType, dateExpr),
                        new OrderExpression(order.OrderType, timeExpr)
                    };
                    return true;
                }

                private bool TryGetUnderlyingColumnExpressions(
                    MemberExpression compositeAccess,
                    CompositeFieldAttribute attr,
                    out Expression dateExpr,
                    out Expression timeExpr)
                {
                    dateExpr = null;
                    timeExpr = null;

                    MappingEntity entity = null;
                    Expression entityBody = null;
                    TableAlias alias = null;

                    if (compositeAccess.Expression is EntityExpression entityExpression)
                    {
                        entity = entityExpression.Entity;
                        entityBody = entityExpression.Expression;
                        if (entityBody is AliasedExpression aliased)
                            alias = aliased.Alias;
                    }
                    else if (compositeAccess.Expression is TableExpression tableExpression)
                    {
                        entity = tableExpression.Entity;
                        alias = tableExpression.Alias;
                    }
                    else
                    {
                        return false;
                    }

                    var entityType = entity.StaticType;
                    var dateMember = (MemberInfo)entityType.GetProperty(attr.DateMember)
                                     ?? entityType.GetField(attr.DateMember);
                    var timeMember = (MemberInfo)entityType.GetProperty(attr.TimeMember)
                                     ?? entityType.GetField(attr.TimeMember);

                    if (dateMember == null || timeMember == null)
                        return false;

                    if (entityBody != null)
                    {
                        dateExpr = ColumnizerFindMemberInEntity(entityBody, dateMember);
                        timeExpr = ColumnizerFindMemberInEntity(entityBody, timeMember);
                    }

                    if (dateExpr == null && alias != null && mapping.IsColumn(entity, dateMember))
                    {
                        dateExpr = CreateColumnExpression(entity, dateMember, alias);
                    }

                    if (timeExpr == null && alias != null && mapping.IsColumn(entity, timeMember))
                    {
                        timeExpr = CreateColumnExpression(entity, timeMember, alias);
                    }

                    return dateExpr != null && timeExpr != null;
                }

                private ColumnExpression CreateColumnExpression(MappingEntity entity, MemberInfo member, TableAlias alias)
                {
                    return new ColumnExpression(
                        TypeHelper.GetMemberType(member),
                        mapper.GetColumnType(entity, member),
                        alias,
                        mapping.GetColumnName(entity, member));
                }

                private static Expression ColumnizerFindMemberInEntity(Expression entityExpression, MemberInfo member)
                {
                    if (entityExpression is MemberInitExpression minit)
                    {
                        foreach (var binding in minit.Bindings.OfType<MemberAssignment>())
                        {
                            if (binding.Member.Name == member.Name)
                                return binding.Expression;
                        }
                        return ColumnizerFindMemberInEntity(minit.NewExpression, member);
                    }

                    if (entityExpression is NewExpression nex && nex.Members != null)
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

					// First check AdvantageEntityPolicy for programmatic filter
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

					// Then check for AssociationFilterAttribute (Advantage-specific)
					var filterAttr = member.GetCustomAttributes(typeof(AssociationFilterAttribute), true)
						.Cast<AssociationFilterAttribute>()
						.FirstOrDefault();
					
					if (filterAttr != null && !string.IsNullOrWhiteSpace(filterAttr.Column) && !string.IsNullOrWhiteSpace(filterAttr.Value))
					{
						// Build simple equality: relatedTable.Column = 'Value'
						var filterMember = relatedEntity.StaticType.GetProperty(filterAttr.Column) ?? 
							(MemberInfo)relatedEntity.StaticType.GetField(filterAttr.Column);
						
						if (filterMember != null && _mapping.IsColumn(relatedEntity, filterMember))
						{
							var columnName = _mapping.GetColumnName(relatedEntity, filterMember);
							var columnType = this.GetColumnType(relatedEntity, filterMember);

							var columnExpr = new ColumnExpression(
								TypeHelper.GetMemberType(filterMember), 
								columnType, 
								projection.Select.Alias, 
								columnName);

							var valueExpr = Expression.Constant(filterAttr.Value, TypeHelper.GetMemberType(filterMember));
							var filterCondition = Expression.Equal(columnExpr, valueExpr);

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

			private class NavigationPropertyRewriter : ExpressionVisitor
			{
				private readonly AdvantageMapping mapping;
				private IQueryProvider provider;

				private NavigationPropertyRewriter(AdvantageMapping mapping, IQueryProvider provider)
				{
					this.mapping = mapping;
					this.provider = provider;
				}

				public static Expression Rewrite(AdvantageMapping mapping, Expression expression)
				{
					var finder = new ProviderFinder();
					finder.Find(expression);
					if (finder.Provider == null) return expression;

					return new NavigationPropertyRewriter(mapping, finder.Provider).Visit(expression);
				}

				class ProviderFinder : ExpressionVisitor
				{
					public IQueryProvider Provider { get; private set; }
                    
                    public void Find(Expression expression)
                    {
                        this.Visit(expression);
                    }

					protected override Expression VisitConstant(ConstantExpression c)
					{
						if (Provider == null && c.Value is IQueryable q)
						{
							Provider = q.Provider;
						}
						return base.VisitConstant(c);
					}
				}

				protected override Expression VisitMethodCall(MethodCallExpression m)
				{
					if (m.Method.Name == "Distinct" && m.Arguments.Count == 1)
					{
						var source = this.Visit(m.Arguments[0]);
						
						// Check if source is Select(x => x.Nav.Prop)
						if (source is MethodCallExpression select && 
							select.Method.Name == "Select" && 
							select.Arguments.Count == 2)
						{
							var selector = (LambdaExpression)((UnaryExpression)select.Arguments[1]).Operand;
							var param = selector.Parameters[0];
							var body = selector.Body;

							if (body is MemberExpression mem && IsNavigationProperty(mem, out var entityType, out var member))
							{
								// Rewrite to SelectMany
								return RewriteToSelectMany(select.Arguments[0], param, mem, entityType, member);
							}
						}
						
						// Reconstruct Distinct with potentially rewritten source
						if (source != m.Arguments[0])
						{
							return Expression.Call(m.Method, source);
						}
					}
                    // Removed potentially aggressive Select rewriting that caused regressions in projections with collections or filters
                    // The Distinct fix is handled by the block above.
                    /*
                    else if (m.Method.Name == "Select" && m.Arguments.Count == 2)
                    {
                        // Check if projection contains navigation properties that need rewriting
                        var source = this.Visit(m.Arguments[0]);
                        var selector = (LambdaExpression)((UnaryExpression)m.Arguments[1]).Operand;
                        var param = selector.Parameters[0];
                        
                        // Find first navigation property in projection
                        var finder = new NavigationFinder(mapping, param);
                        finder.Find(selector.Body);
                        
                        if (finder.FoundNavigation != null)
                        {
                            var nav = finder.FoundNavigation;
                            var entityType = TypeHelper.GetElementType(nav.Type) ?? nav.Type;
                            var member = nav.Member;
                            
                            var rewritten = RewriteSelectToSelectMany(source, param, selector, nav, entityType, member);
                            if (rewritten != null)
                                return rewritten;
                        }
                        
                        if (source != m.Arguments[0])
                        {
                            return Expression.Call(m.Method, source, m.Arguments[1]);
                        }
                    } 
                    */

					return base.VisitMethodCall(m);
				}

                class NavigationFinder : ExpressionVisitor
                {
                    private AdvantageMapping mapping;
                    private ParameterExpression param;
                    public MemberExpression FoundNavigation { get; private set; }

                    public NavigationFinder(AdvantageMapping mapping, ParameterExpression param)
                    {
                        this.mapping = mapping;
                        this.param = param;
                    }

                    protected override Expression VisitMemberAccess(MemberExpression m)
                    {
                        if (FoundNavigation == null && m.Expression == param && 
                            mapping.IsRelationship(mapping.GetEntity(m.Expression.Type), m.Member))
                        {
                            FoundNavigation = m;
                            return m;
                        }
                        return base.VisitMemberAccess(m);
                    }

                    public void Find(Expression expression)
                    {
                        this.Visit(expression);
                    }
                }

				private Expression RewriteSelectToSelectMany(Expression source, ParameterExpression param, LambdaExpression selector, MemberExpression nav, Type relatedType, MemberInfo relationMember)
				{
					// Rewrite: source.Select(x => new { P = x.Nav.Prop })
					// To: source.SelectMany(x => Table<Related>.Where(y => y.PK == x.FK).DefaultIfEmpty(), (x, y) => new { P = y.Prop })
					
					// 1. Build Collection Selector: x => Table<Related>.Where(...)
					var relatedTable = this.provider.CreateQuery(
                        Expression.Call(
                            typeof(Queryable), 
                            "AsQueryable", 
                            new[] { relatedType }, 
                            Expression.Constant(Activator.CreateInstance(typeof(List<>).MakeGenericType(relatedType)))
                        )
                    );
					
					var getTableMethod = this.provider.GetType().GetMethod("GetTable", Type.EmptyTypes).MakeGenericMethod(relatedType);
					var tableQuery = getTableMethod.Invoke(this.provider, null);
					var tableExpression = Expression.Constant(tableQuery);

					var yParam = Expression.Parameter(relatedType, "y");
					var entity = mapping.GetEntity(param.Type);
					var relatedEntity = mapping.GetEntity(relatedType);
					
					var fkMembers = mapping.GetAssociationKeyMembers(entity, relationMember).ToList();
					var pkMembers = mapping.GetAssociationRelatedKeyMembers(entity, relationMember).ToList();
					
                    if (fkMembers.Count == 0 || pkMembers.Count == 0) return null;

					Expression whereBody = null;
					for (int i = 0; i < fkMembers.Count; i++)
					{
						var fk = Expression.MakeMemberAccess(param, fkMembers[i]);
						var pk = Expression.MakeMemberAccess(yParam, pkMembers[i]);
						var eq = Expression.Equal(pk, fk);
						whereBody = (whereBody == null) ? eq : Expression.AndAlso(whereBody, eq);
					}
					
					var whereLambda = Expression.Lambda(whereBody, yParam);
					
					var whereCall = Expression.Call(
						typeof(Queryable),
						"Where",
						new[] { relatedType },
						tableExpression,
						whereLambda
					);
					
                    // Use reflection to find DefaultIfEmpty to avoid ambiguity or resolution issues
                    var defaultIfEmptyMethod = typeof(Queryable).GetMethods()
                        .First(m => m.Name == "DefaultIfEmpty" && m.GetParameters().Length == 1)
                        .MakeGenericMethod(relatedType);

					var defaultIfEmptyCall = Expression.Call(
                        defaultIfEmptyMethod,
						whereCall
					);

                    // Explicitly specify the delegate type to match SelectMany signature (IEnumerable instead of IQueryable)
                    var enumerableType = typeof(IEnumerable<>).MakeGenericType(relatedType);
                    var funcType = typeof(Func<,>).MakeGenericType(param.Type, enumerableType);
                    
                    // Note: We avoid Expression.Convert(defaultIfEmptyCall, enumerableType) here because 
                    // QueryBinder in IQToolkit expects the collection selector to be a MethodCall to DefaultIfEmpty directly.
                    // If we wrap it in a Convert, QueryBinder fails to recognize the DefaultIfEmpty call and throws 
                    // "The expression of type ... is not a sequence".
                    var collectionSelector = Expression.Lambda(funcType, defaultIfEmptyCall, param);

					// 2. Build Result Selector: (x, y) => new { P = y.Prop }
					// We need to replace occurrences of x.Nav with y in the original selector body
					var replacer = new NavigationReplacer(nav, yParam);
					replacer.Replace(selector.Body);
					var newBody = replacer.Result;
					
					var resultSelector = Expression.Lambda(newBody, param, yParam);
					
					// 3. Call SelectMany
					// There are two SelectMany overloads with 3 parameters:
					// 1. SelectMany(source, Func<T, IEnumerable<R>>, Func<T, R, TResult>) - what we need
					// 2. SelectMany(source, Func<T, int, IEnumerable<R>>, Func<T, R, TResult>) - has index parameter
					// We need the one where the collection selector is Func<TSource, IEnumerable<TCollection>>
					// (2 generic args), not Func<TSource, int, IEnumerable<TCollection>> (3 generic args)
                    var selectManyMethod = typeof(Queryable).GetMethods()
                        .First(m => m.Name == "SelectMany" 
							&& m.GetParameters().Length == 3
							&& m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)
                        .MakeGenericMethod(param.Type, relatedType, selector.Body.Type);

					return Expression.Call(
                        selectManyMethod,
						source,
						Expression.Quote(collectionSelector),
						Expression.Quote(resultSelector)
					);
				}

				class NavigationReplacer : ExpressionVisitor
				{
					private MemberExpression target;
					private Expression replacement;
					public Expression Result { get; private set; }

					public NavigationReplacer(MemberExpression target, Expression replacement)
					{
						this.target = target;
						this.replacement = replacement;
					}

					public void Replace(Expression expression)
					{
						this.Result = this.Visit(expression);
					}

					protected override Expression VisitMemberAccess(MemberExpression m)
					{
						if (m.Member == target.Member && m.Expression == target.Expression)
						{
							return replacement;
						}
						return base.VisitMemberAccess(m);
					}
				}

				private bool IsNavigationProperty(MemberExpression m, out Type entityType, out MemberInfo member)
				{
					entityType = null;
					member = null;
					
					// Check for x.Nav.Prop pattern
					if (m.Expression is MemberExpression nav && 
						nav.Expression is ParameterExpression && 
						mapping.IsRelationship(mapping.GetEntity(nav.Expression.Type), nav.Member))
					{
						entityType = TypeHelper.GetElementType(nav.Type) ?? nav.Type;
						member = nav.Member;
						return true;
					}
					return false;
				}

				private Expression RewriteToSelectMany(Expression source, ParameterExpression param, MemberExpression mem, Type relatedType, MemberInfo relationMember)
				{
					// Construct: source.SelectMany(x => Table<Related>.Where(y => y.PK == x.FK), (x, y) => y.Prop)
					
					var relatedTable = this.provider.CreateQuery(
                        Expression.Call(
                            typeof(Queryable), 
                            "AsQueryable", 
                            new[] { relatedType }, 
                            Expression.Constant(Activator.CreateInstance(typeof(List<>).MakeGenericType(relatedType)))
                        )
                    );
					
					var getTableMethod = this.provider.GetType().GetMethod("GetTable", Type.EmptyTypes).MakeGenericMethod(relatedType);
					var tableQuery = getTableMethod.Invoke(this.provider, null);
					var tableExpression = Expression.Constant(tableQuery);

					// Build Where lambda: y => y.PK == x.FK
					var yParam = Expression.Parameter(relatedType, "y");
					
					var entity = mapping.GetEntity(param.Type);
					var relatedEntity = mapping.GetEntity(relatedType);
					
					var fkMembers = mapping.GetAssociationKeyMembers(entity, relationMember).ToList();
					var pkMembers = mapping.GetAssociationRelatedKeyMembers(entity, relationMember).ToList();
					
                    if (fkMembers.Count == 0 || pkMembers.Count == 0) return null;

					Expression whereBody = null;
					for (int i = 0; i < fkMembers.Count; i++)
					{
						var fk = Expression.MakeMemberAccess(param, fkMembers[i]);
						var pk = Expression.MakeMemberAccess(yParam, pkMembers[i]);
						var eq = Expression.Equal(pk, fk);
						whereBody = (whereBody == null) ? eq : Expression.AndAlso(whereBody, eq);
					}
					
					var whereLambda = Expression.Lambda(whereBody, yParam);
					
					// Call Where
					var whereCall = Expression.Call(
						typeof(Queryable),
						"Where",
						new[] { relatedType },
						tableExpression,
						Expression.Quote(whereLambda)
					);
					
                    // Construct lambda with explicit return type IEnumerable<T> to satisfy SelectMany signature
                    // while keeping the body as IQueryable (which QueryBinder understands)
                    var funcType = typeof(Func<,>).MakeGenericType(param.Type, typeof(IEnumerable<>).MakeGenericType(relatedType));
                    var collectionSelector = Expression.Lambda(funcType, whereCall, param);

					// Build ResultSelector: (x, y) => y.Prop
					// mem is x.Nav.Prop. We want y.Prop.
					// mem.Member is Prop.
					var propAccess = Expression.MakeMemberAccess(yParam, mem.Member);
					var resultSelector = Expression.Lambda(propAccess, param, yParam);
					
					// Call SelectMany
					var selectManyCall = Expression.Call(
						typeof(Queryable),
						"SelectMany",
						new[] { param.Type, relatedType, mem.Type },
						source,
						Expression.Quote(collectionSelector),
						Expression.Quote(resultSelector)
					);
					
                    // Rewrite Distinct to GroupBy(x => x).Select(g => g.Key)
                    // This forces a subquery that RedundantSubqueryRemover respects (usually)
                    // and avoids the Distinct-loss issue with Count.
                    
                    var keyParam = Expression.Parameter(mem.Type, "k");
                    var groupByCall = Expression.Call(
                        typeof(Queryable),
                        "GroupBy",
                        new[] { mem.Type, mem.Type },
                        selectManyCall,
                        Expression.Quote(Expression.Lambda(keyParam, keyParam))
                    );
                    
                    var groupType = typeof(IGrouping<,>).MakeGenericType(mem.Type, mem.Type);
                    var groupParam = Expression.Parameter(groupType, "g");
                    var keyAccess = Expression.MakeMemberAccess(groupParam, groupType.GetProperty("Key"));
                    
                    return Expression.Call(
                        typeof(Queryable),
                        "Select",
                        new[] { groupType, mem.Type },
                        groupByCall,
                        Expression.Quote(Expression.Lambda(keyAccess, groupParam))
                    );
				}
			}
		}

		/// <summary>
		/// Expands composite fields in SELECT projections BEFORE binding.
		/// This rewrites locgen.DTDepartMateriel to locgen.DATEDEP so the projection
		/// binds to the actual database column, not the virtual composite field.
		/// Only rewrites when inside a MemberInit or New expression (DTO creation).
		/// </summary>
		private class CompositeFieldProjectionExpander : ExpressionVisitor
		{
			private readonly AdvantageMapping mapping;
			private bool insideProjection = false;

			private CompositeFieldProjectionExpander(AdvantageMapping mapping)
			{
				this.mapping = mapping;
			}

			public static Expression Expand(Expression expression, AdvantageMapping mapping)
			{
				return new CompositeFieldProjectionExpander(mapping).Visit(expression);
			}

			protected override Expression VisitMemberInit(MemberInitExpression node)
			{
				// We're inside a projection (DTO creation)
				bool wasInside = insideProjection;
				insideProjection = true;
				var result = base.VisitMemberInit(node);
				insideProjection = wasInside;
				return result;
			}

			protected override NewExpression VisitNew(NewExpression node)
			{
				// We're inside a projection (anonymous type or DTO creation)
				bool wasInside = insideProjection;
				insideProjection = true;
				var result = base.VisitNew(node);
				insideProjection = wasInside;
				return result;
			}

			protected override Expression VisitMemberAccess(MemberExpression m)
			{
				// Visit the source first
				var source = this.Visit(m.Expression);

				// Only rewrite composite fields when inside a projection
				if (insideProjection && HasCompositeFieldAttribute(m.Member))
				{
					// Get the underlying date column
					var attr = GetCompositeFieldAttribute(m.Member);
					if (attr != null && m.Expression != null)
					{
						var entityType = m.Expression.Type;
						var dateMember = (MemberInfo)entityType.GetProperty(attr.DateMember) ?? 
						                 entityType.GetField(attr.DateMember);

						if (dateMember != null)
						{
							// Rewrite to access the date column instead of the composite
							// This ensures the projection SELECT uses DATEDEP, not DTDepartMateriel
							return Expression.MakeMemberAccess(source, dateMember);
						}
					}
				}

				// Reconstruct if source changed
				if (source != m.Expression)
					return Expression.MakeMemberAccess(source, m.Member);

				return m;
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
				
				// PRIORITY 1: Handle property access on composite.Value (e.g., composite.Value.Date)
				// This is the DevExpress pattern: .DATEDEP.Value.Date where DATEDEP is a projected composite field
				if (source != null && source.NodeType == ExpressionType.MemberAccess)
				{
					var sourceMember = (MemberExpression)source;
					
					// Check if source is accessing .Value on a nullable composite field
					// Pattern: compositeField.Value.Date
					if (sourceMember.Member.Name == "Value" && 
					    sourceMember.Expression is MemberExpression innerMember &&
					    TypeHelper.IsNullableType(innerMember.Type) &&
					    HasCompositeFieldAttribute(innerMember.Member))
					{
						// We need to expand the composite first, then apply both .Value and the property
						var expanded = ExpandCompositeField(innerMember);
						if (expanded != null)
						{
							// Apply .Value to the expanded composite
							var withValue = Expression.MakeMemberAccess(expanded, sourceMember.Member);
							// Then apply the final property (Date, Hour, etc.)
							return Expression.MakeMemberAccess(withValue, m.Member);
						}
					}
					
					// Check if source is directly a composite field access  
					// Pattern: compositeField.Date (without .Value - non-nullable composite)
					if (HasCompositeFieldAttribute(sourceMember.Member))
					{
						// Expand composite, then apply the property
						var expanded = ExpandCompositeField(sourceMember);
						if (expanded != null)
						{
							return Expression.MakeMemberAccess(expanded, m.Member);
						}
					}
				}
				
				// PRIORITY 2: Handle .Value access on Nullable<T>
				// When accessing .Value on a nullable composite field, we need to check the underlying composite field first
				if (m.Member.Name == "Value" && TypeHelper.IsNullableType(m.Expression?.Type))
				{
					var underlyingType = TypeHelper.GetNonNullableType(m.Expression.Type);
					
					// The source might be a composite field access
					// e.g., locgen.DTDepartMateriel.Value where DTDepartMateriel is DateTime?
					// We need to expand the composite field first, then handle the .Value
					if (source != null && source.NodeType == ExpressionType.MemberAccess)
					{
						var innerMember = (MemberExpression)source;
						if (HasCompositeFieldAttribute(innerMember.Member))
						{
							// Expand the composite field first
							var expanded = ExpandCompositeField(innerMember);
							if (expanded != null)
							{
								// Then access .Value on the expanded result
								return Expression.MakeMemberAccess(expanded, m.Member);
							}
						}
					}
					
					// Not a composite field, continue normally
					if (source != m.Expression)
						return Expression.MakeMemberAccess(source, m.Member);
					return m;
				}
				
				// PRIORITY 3: Handle direct composite field access
				if (HasCompositeFieldAttribute(m.Member))
				{
					var expanded = ExpandCompositeField(m);
					if (expanded != null)
						return expanded;
				}
				
				// PRIORITY 4: Handle composite field through entity expression
				if (source != null && 
			    source.NodeType == (ExpressionType)DbExpressionType.Entity && 
			    HasCompositeFieldAttribute(m.Member))
				{
					var expanded = ExpandFromEntity((EntityExpression)source, m.Member);
					if (expanded != null)
						return expanded;
				}
				
				if (source != m.Expression)
					return Expression.MakeMemberAccess(source, m.Member);
				
				return m;
			}
			
			private Expression ExpandCompositeField(MemberExpression m)
			{
				// For composite fields accessed through entity expressions
				if (m.Expression != null && m.Expression.NodeType == (ExpressionType)DbExpressionType.Entity)
				{
					var entityExpr = (EntityExpression)m.Expression;
					return ExpandFromEntity(entityExpr, m.Member);
				}
				
				// For other composite field accesses, we can't expand here
				// They should have been rewritten by AdvantageCompositeFieldRewriter
				return null;
			}
			
			private Expression ExpandFromEntity(EntityExpression entityExpr, MemberInfo compositeMember)
			{
				var attr = GetCompositeFieldAttribute(compositeMember);
				if (attr == null) return null;
				
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
					
					return Expression.MakeMemberAccess(minimalEntity, compositeMember);
				}
				
				return null;
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

		/// <summary>
		/// Fixes GROUP BY expressions that reference columns from projections with navigation properties.
		/// When grouping by a property from an anonymous type projection that includes navigation properties,
		/// the GROUP BY clause must correctly reference the underlying column expression.
		/// The issue is that ProjectColumns might create a new column, but the GROUP BY needs to reference
		/// the column from the underlying projection's SELECT, not the current SELECT.
		/// </summary>
		private class GroupByColumnFixer : DbExpressionVisitor
		{
			public static Expression Fix(Expression expression)
			{
				return new GroupByColumnFixer().Visit(expression);
			}

			protected override Expression VisitSelect(SelectExpression select)
			{
				select = (SelectExpression)base.VisitSelect(select);

				if (select.GroupBy != null && select.GroupBy.Count > 0 && select.From != null)
				{
					// Check if FROM is a ProjectionExpression (subquery)
					if (select.From is ProjectionExpression fromProj)
					{
						// Fix GROUP BY expressions to reference columns from the FROM projection
						var fixedGroupBy = new List<Expression>();
						foreach (var groupExpr in select.GroupBy)
						{
							var fixedExpr = FixGroupByExpression(groupExpr, select, fromProj);
							fixedGroupBy.Add(fixedExpr);
						}

						if (fixedGroupBy.Count > 0 && !fixedGroupBy.SequenceEqual(select.GroupBy))
						{
							select = new SelectExpression(
								select.Alias,
								select.Columns,
								select.From,
								select.Where,
								select.OrderBy,
								fixedGroupBy,
								select.IsDistinct,
								select.Skip,
								select.Take,
								select.IsReverse
							);
						}
					}
				}

			return select;
		}

			private Expression FixGroupByExpression(Expression groupExpr, SelectExpression select, ProjectionExpression fromProj)
			{
				// If GROUP BY references a ColumnExpression, we need to ensure it references
				// the correct underlying column, especially when the FROM is a projection with joins
				if (groupExpr is ColumnExpression colExpr)
				{
					// Check if this column references the FROM projection's SELECT alias
					// (This happens when ProjectColumns creates columns for the GROUP BY in QueryBinder.BindGroupBy)
					if (colExpr.Alias == fromProj.Select.Alias)
					{
						// Try to find the corresponding column in the FROM projection's SELECT
						// by matching the column name
						var matchingColumn = fromProj.Select.Columns.FirstOrDefault(c => c.Name == colExpr.Name);
						if (matchingColumn != null)
						{
							// Use the expression from the FROM projection's column directly
							// This ensures we reference the actual underlying column (which might be from a joined table)
							// rather than the intermediate projection column
							
							// If it's a ColumnExpression, we can use it as-is (it already has the correct alias)
							// If it's a more complex expression, we use it directly
							// BUT: Avoid returning a ProjectionExpression into a GROUP BY clause
							if (!(matchingColumn.Expression is ProjectionExpression))
							{
								return matchingColumn.Expression;
							}
						}
					}
				}

				return groupExpr;
			}
		}
	}
}
