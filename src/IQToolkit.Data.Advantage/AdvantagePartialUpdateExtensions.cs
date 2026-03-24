using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using IQToolkit;
using IQToolkit.Data;
using IQToolkit.Data.Common;

namespace IQToolkit.Data.Advantage
{
    public static class AdvantagePartialUpdateExtensions
    {
        private sealed class CompositeFieldInfo
        {
            public MemberInfo CompositeMember { get; }
            public MemberInfo DateMember { get; }
            public MemberInfo TimeMember { get; }

            public CompositeFieldInfo(MemberInfo compositeMember, MemberInfo dateMember, MemberInfo timeMember)
            {
                CompositeMember = compositeMember;
                DateMember = dateMember;
                TimeMember = timeMember;
            }
        }

        /// <summary>
        /// Partial update based on an existing entity instance.
        /// Requires a prior SELECT to obtain the entity.
        /// </summary>
        public static int UpdatePartial<T>(
            this IEntityTable<T> table,
            T entity,
            Expression<Func<T, object>> fields)
            where T : class
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            var provider = table.Provider as EntityProvider;
            if (provider == null)
                throw new InvalidOperationException("The table's provider must be an EntityProvider.");

            var mapping = provider.Mapping;

            var basicMapping = mapping as BasicMapping;
            if (basicMapping == null)
                throw new InvalidOperationException("Partial updates require a BasicMapping-derived mapping (e.g., AdvantageMapping).");

            // Resolve mapping entity for T and this table
            var entityMeta = mapping.GetEntity(typeof(T), table.EntityId);

            // Only support entities mapped to a single table in v1
            var tableName = basicMapping.GetTableName(entityMeta);

            // Build table expression
            var tableAlias = new TableAlias();
            var tex = new TableExpression(tableAlias, entityMeta, tableName);

            // Build WHERE predicate based on primary key(s)
            var where = BuildPrimaryKeyPredicate(entityMeta, basicMapping, provider.Language, tex, entity);

            // Determine which members to update
            var selectedMembers = GetSelectedMembers(fields);

            // Build column assignments limited to selected & updatable members
            var assignments = BuildAssignments(entityMeta, basicMapping, provider.Language, tex, entity, selectedMembers);

            if (assignments.Count == 0)
            {
                // Nothing to update; treat as no-op
                return 0;
            }

            var updateCommand = new UpdateCommand(tex, where, assignments);

            // Build execution plan directly via ExecutionBuilder using a lightweight translator/linguist.
            var providerExpression = Expression.Constant(provider, typeof(EntityProvider));
            var translator = new QueryTranslator(provider.Language, provider.Mapping, provider.Policy);
            var linguist = provider.Language.CreateLinguist(translator);
            Expression plan = ExecutionBuilder.Build(linguist, provider.Policy, updateCommand, providerExpression);

            var lambda = Expression.Lambda<Func<int>>(plan);
            var executor = lambda.Compile();
            return executor();
        }

        /// <summary>
        /// Partial update without loading the entity first.
        /// WHERE is supplied as a predicate, and SET comes from an anonymous object whose
        /// property names match mapped column members and whose values are constant/closure values.
        /// 
        /// Example:
        /// table.UpdatePartial(
        ///     x => x.CodeArticle == "1001",
        ///     x => new { Status = LocStatus.Location }
        /// );
        /// </summary>
        public static int UpdatePartial<T>(
            this IEntityTable<T> table,
            Expression<Func<T, bool>> where,
            Expression<Func<T, object>> set)
            where T : class
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (where == null) throw new ArgumentNullException(nameof(where));
            if (set == null) throw new ArgumentNullException(nameof(set));

            var provider = table.Provider as EntityProvider;
            if (provider == null)
                throw new InvalidOperationException("The table's provider must be an EntityProvider.");

            var mapping = provider.Mapping;

            var basicMapping = mapping as BasicMapping;
            if (basicMapping == null)
                throw new InvalidOperationException("Partial updates require a BasicMapping-derived mapping (e.g., AdvantageMapping).");

            var entityMeta = mapping.GetEntity(typeof(T), table.EntityId);
            var tableName = basicMapping.GetTableName(entityMeta);

            var tableAlias = new TableAlias();
            var tex = new TableExpression(tableAlias, entityMeta, tableName);

            // WHERE must be a simple equality on the primary key (single-column PK in v1)
            var whereExpr = BuildWhereFromPredicate(entityMeta, basicMapping, provider.Language, tex, where);

            var assignments = BuildAssignmentsFromSet(entityMeta, basicMapping, provider.Language, tex, set);

            if (assignments.Count == 0)
            {
                return 0;
            }

            var updateCommand = new UpdateCommand(tex, whereExpr, assignments);

            var providerExpression = Expression.Constant(provider, typeof(EntityProvider));
            var translator = new QueryTranslator(provider.Language, provider.Mapping, provider.Policy);
            var linguist = provider.Language.CreateLinguist(translator);
            Expression plan = ExecutionBuilder.Build(linguist, provider.Policy, updateCommand, providerExpression);

            var lambda = Expression.Lambda<Func<int>>(plan);
            var executor = lambda.Compile();
            return executor();
        }

        private static Expression BuildPrimaryKeyPredicate<T>(
            MappingEntity entityMeta,
            BasicMapping mapping,
            QueryLanguage language,
            TableExpression tex,
            T entityInstance)
        {
            Expression where = null;

            foreach (var pkMember in mapping.GetPrimaryKeyMembers(entityMeta))
            {
                // Column on table side
                var memberType = TypeHelper.GetMemberType(pkMember);
                var columnType = language.TypeSystem.GetColumnType(memberType);
                var columnName = mapping.GetColumnName(entityMeta, pkMember);

                var columnExpr = new ColumnExpression(
                    memberType,
                    columnType,
                    tex.Alias,
                    columnName);

                // Constant value from entity instance
                object value = pkMember.GetValue(entityInstance);
                var valueExpr = Expression.Constant(value, memberType);

                var equal = columnExpr.Equal(valueExpr);
                where = where == null ? equal : where.And(equal);
            }

            if (where == null)
            {
                throw new InvalidOperationException($"Entity '{entityMeta.StaticType.Name}' has no primary key members defined; cannot build partial update predicate.");
            }

            return where;
        }

        private static Expression BuildWhereFromPredicate<T>(
            MappingEntity entityMeta,
            BasicMapping mapping,
            QueryLanguage language,
            TableExpression tex,
            Expression<Func<T, bool>> where)
        {
            var pkMembers = mapping.GetPrimaryKeyMembers(entityMeta).ToList();
            if (pkMembers.Count != 1)
            {
                throw new NotSupportedException("UpdatePartial with a predicate currently supports only single-column primary keys.");
            }

            var pkMember = pkMembers[0];

            Expression body = where.Body;
            body = StripConvert(body);

            var be = body as BinaryExpression;
            if (be == null || be.NodeType != ExpressionType.Equal)
            {
                throw new NotSupportedException("UpdatePartial where-predicate must be a simple equality on the primary key, e.g. x => x.Id == someKey.");
            }

            var param = where.Parameters[0];

            MemberExpression memberExpr = null;
            Expression valueExpr = null;

            var left = StripConvert(be.Left);
            var right = StripConvert(be.Right);

            if (left is MemberExpression lm && IsParameterMember(lm, param))
            {
                memberExpr = lm;
                valueExpr = right;
            }
            else if (right is MemberExpression rm && IsParameterMember(rm, param))
            {
                memberExpr = rm;
                valueExpr = left;
            }
            else
            {
                throw new NotSupportedException("UpdatePartial where-predicate must compare the primary key member to a constant/closure value.");
            }

            if (!Equals(memberExpr.Member, pkMember))
            {
                throw new NotSupportedException("UpdatePartial where-predicate must target the primary key member.");
            }

            var memberType = TypeHelper.GetMemberType(pkMember);
            var columnType = language.TypeSystem.GetColumnType(memberType);
            var columnName = mapping.GetColumnName(entityMeta, pkMember);

            var columnExpr = new ColumnExpression(
                memberType,
                columnType,
                tex.Alias,
                columnName);

            object valueObj = Evaluate(valueExpr);
            var constExpr = Expression.Constant(valueObj, memberType);

            return columnExpr.Equal(constExpr);
        }

        private static List<ColumnAssignment> BuildAssignments<T>(
            MappingEntity entityMeta,
            BasicMapping mapping,
            QueryLanguage language,
            TableExpression tex,
            T entityInstance,
            HashSet<MemberInfo> selectedMembers)
        {
            var assignments = new List<ColumnAssignment>();

            foreach (var member in mapping.GetMappedMembers(entityMeta))
            {
                if (!selectedMembers.Contains(member))
                    continue;

                // Composite field expansion (e.g. DTDEP => DATEDEP + HEUREDEP)
                var composite = GetCompositeFieldInfo(entityMeta, mapping, member);
                if (composite != null)
                {
                    object raw = member.GetValue(entityInstance);
                    var compositeValues = GetCompositeDateTimeComponents(raw);
                    var dateValue = compositeValues.Item1;
                    var timeValue = compositeValues.Item2;

                    // DATE component
                    var dateClrType = TypeHelper.GetMemberType(composite.DateMember);
                    var dateQt = language.TypeSystem.GetColumnType(dateClrType);
                    var dateName = mapping.GetColumnName(entityMeta, composite.DateMember);

                    var dateCol = new ColumnExpression(
                        dateClrType,
                        dateQt,
                        tex.Alias,
                        dateName);

                    var dateExpr = Expression.Constant(dateValue, dateClrType);
                    assignments.Add(new ColumnAssignment(dateCol, dateExpr));

                    // TIME component
                    var timeClrType = TypeHelper.GetMemberType(composite.TimeMember);
                    var timeQt = language.TypeSystem.GetColumnType(timeClrType);
                    var timeName = mapping.GetColumnName(entityMeta, composite.TimeMember);

                    var timeCol = new ColumnExpression(
                        timeClrType,
                        timeQt,
                        tex.Alias,
                        timeName);

                    var timeExpr = Expression.Constant(timeValue, timeClrType);
                    assignments.Add(new ColumnAssignment(timeCol, timeExpr));

                    continue;
                }

                // Only simple column members that are updatable
                if (!mapping.IsColumn(entityMeta, member))
                    continue;

                if (!mapping.IsUpdatable(entityMeta, member))
                    continue;

                var memberType = TypeHelper.GetMemberType(member);
                var columnType = language.TypeSystem.GetColumnType(memberType);
                var columnName = mapping.GetColumnName(entityMeta, member);

                var columnExpr = new ColumnExpression(
                    memberType,
                    columnType,
                    tex.Alias,
                    columnName);

                object value = member.GetValue(entityInstance);
                var valueExpr = Expression.Constant(value, memberType);

                assignments.Add(new ColumnAssignment(columnExpr, valueExpr));
            }

            return assignments;
        }

        private static List<ColumnAssignment> BuildAssignmentsFromSet<T>(
            MappingEntity entityMeta,
            BasicMapping mapping,
            QueryLanguage language,
            TableExpression tex,
            Expression<Func<T, object>> set)
        {
            Expression body = set.Body;
            body = StripConvert(body);

            List<KeyValuePair<MemberInfo, Expression>> updates;

            var nex = body as NewExpression;
            if (nex != null)
            {
                if (nex.Members == null || nex.Members.Count != nex.Arguments.Count)
                {
                    throw new NotSupportedException("UpdatePartial set-expression must use named anonymous object members.");
                }

                updates = new List<KeyValuePair<MemberInfo, Expression>>(nex.Arguments.Count);
                for (int i = 0; i < nex.Arguments.Count; i++)
                {
                    updates.Add(new KeyValuePair<MemberInfo, Expression>(nex.Members[i], nex.Arguments[i]));
                }
            }
            else
            {
                var mi = body as MemberInitExpression;
                if (mi == null)
                {
                    throw new NotSupportedException(
                        "UpdatePartial set-expression must be an anonymous object (x => new { A = 1 }) or member-init (x => new T { A = 1 }). " +
                        $"Actual node type: {body.NodeType}.");
                }

                var memberAssignments = mi.Bindings.OfType<MemberAssignment>().ToList();
                updates = new List<KeyValuePair<MemberInfo, Expression>>(memberAssignments.Count);
                foreach (var b in memberAssignments)
                {
                    updates.Add(new KeyValuePair<MemberInfo, Expression>(b.Member, b.Expression));
                }
            }

            var param = set.Parameters[0];
            var assignments = new List<ColumnAssignment>();

            for (int i = 0; i < updates.Count; i++)
            {
                var anonMember = updates[i].Key;
                var arg = updates[i].Value;

                if (ContainsParameter(arg, param))
                {
                    throw new NotSupportedException("UpdatePartial set-expression values must not reference the lambda parameter; use constants or closure values.");
                }

                // Match anonymous member name back to a mapped entity member
                var targetMember =
                    mapping.GetMappedMembers(entityMeta).FirstOrDefault(m => string.Equals(m.Name, anonMember.Name, StringComparison.Ordinal));

                if (targetMember == null)
                {
                    throw new NotSupportedException($"No mapped member named '{anonMember.Name}' found on entity '{entityMeta.StaticType.Name}'.");
                }

                var composite = GetCompositeFieldInfo(entityMeta, mapping, targetMember);
                if (composite != null)
                {
                    object compositeRaw = Evaluate(arg);
                    var compositeValues = GetCompositeDateTimeComponents(compositeRaw);
                    var dateValue = compositeValues.Item1;
                    var timeValue = compositeValues.Item2;

                    // DATE component
                    var dateClrType = TypeHelper.GetMemberType(composite.DateMember);
                    var dateQt = language.TypeSystem.GetColumnType(dateClrType);
                    var dateName = mapping.GetColumnName(entityMeta, composite.DateMember);

                    var dateCol = new ColumnExpression(
                        dateClrType,
                        dateQt,
                        tex.Alias,
                        dateName);

                    var dateExpr = Expression.Constant(dateValue, dateClrType);
                    assignments.Add(new ColumnAssignment(dateCol, dateExpr));

                    // TIME component
                    var timeClrType = TypeHelper.GetMemberType(composite.TimeMember);
                    var timeQt = language.TypeSystem.GetColumnType(timeClrType);
                    var timeName = mapping.GetColumnName(entityMeta, composite.TimeMember);

                    var timeCol = new ColumnExpression(
                        timeClrType,
                        timeQt,
                        tex.Alias,
                        timeName);

                    var timeExpr = Expression.Constant(timeValue, timeClrType);
                    assignments.Add(new ColumnAssignment(timeCol, timeExpr));

                    continue;
                }

                if (!mapping.IsColumn(entityMeta, targetMember) || !mapping.IsUpdatable(entityMeta, targetMember))
                {
                    continue;
                }

                var memberType = TypeHelper.GetMemberType(targetMember);
                var columnType = language.TypeSystem.GetColumnType(memberType);
                var columnName = mapping.GetColumnName(entityMeta, targetMember);

                var columnExpr = new ColumnExpression(
                    memberType,
                    columnType,
                    tex.Alias,
                    columnName);

                object simpleValue = Evaluate(arg);
                var valueExpr = Expression.Constant(simpleValue, memberType);

                assignments.Add(new ColumnAssignment(columnExpr, valueExpr));
            }

            return assignments;
        }

        private static Expression StripConvert(Expression expression)
        {
            while (expression is UnaryExpression uex &&
                   (uex.NodeType == ExpressionType.Convert || uex.NodeType == ExpressionType.ConvertChecked))
            {
                expression = uex.Operand;
            }
            return expression;
        }

        private static bool IsParameterMember(MemberExpression memberExpression, ParameterExpression parameter)
        {
            return memberExpression.Expression == parameter;
        }

        private static bool ContainsParameter(Expression expression, ParameterExpression parameter)
        {
            var found = false;
            var finder = new ParameterFinder(parameter, () => found = true);
            finder.Find(expression);
            return found;
        }

        private static object Evaluate(Expression expression)
        {
            var lambda = Expression.Lambda(expression);
            var compiled = lambda.Compile();
            return compiled.DynamicInvoke();
        }

        private static CompositeFieldInfo GetCompositeFieldInfo(
            MappingEntity entityMeta,
            BasicMapping mapping,
            MemberInfo member)
        {
            var attr = (CompositeFieldAttribute)member
                .GetCustomAttributes(typeof(CompositeFieldAttribute), true)
                .FirstOrDefault();
            if (attr == null)
            {
                return null;
            }

            var type = entityMeta.StaticType;

            var dateMember = (MemberInfo)(type.GetProperty(attr.DateMember) ?? (MemberInfo)type.GetField(attr.DateMember));
            var timeMember = (MemberInfo)(type.GetProperty(attr.TimeMember) ?? (MemberInfo)type.GetField(attr.TimeMember));

            if (dateMember == null || timeMember == null ||
                !mapping.IsColumn(entityMeta, dateMember) || !mapping.IsColumn(entityMeta, timeMember) ||
                !mapping.IsUpdatable(entityMeta, dateMember) || !mapping.IsUpdatable(entityMeta, timeMember))
            {
                throw new InvalidOperationException(
                    $"Composite field '{member.Name}' on '{entityMeta.StaticType.Name}' requires both mapped, updatable members " +
                    $"'{attr.DateMember}' and '{attr.TimeMember}'. Check your mapping.");
            }

            return new CompositeFieldInfo(member, dateMember, timeMember);
        }

        private static Tuple<object, object> GetCompositeDateTimeComponents(object value)
        {
            if (value == null)
            {
                return new Tuple<object, object>(null, null);
            }

            // Handle nullable DateTime by unboxing underlying value
            var type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                value = Convert.ChangeType(value, Nullable.GetUnderlyingType(type));
                if (value == null)
                {
                    return new Tuple<object, object>(null, null);
                }
            }

            var dt = (DateTime)value;
            var date = dt.Date;
            var time = dt.ToString("HH:mm");
            return new Tuple<object, object>(date, time);
        }

        private sealed class ParameterFinder : ExpressionVisitor
        {
            private readonly ParameterExpression _parameter;
            private readonly Action _onFound;

            public ParameterFinder(ParameterExpression parameter, Action onFound)
            {
                _parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
                _onFound = onFound ?? throw new ArgumentNullException(nameof(onFound));
            }

            public void Find(Expression expression)
            {
                this.Visit(expression);
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _parameter)
                {
                    _onFound();
                }
                return base.VisitParameter(node);
            }
        }

        private static HashSet<MemberInfo> GetSelectedMembers<T>(Expression<Func<T, object>> fields)
        {
            Expression body = fields.Body;

            // Strip boxing convert
            if (body.NodeType == ExpressionType.Convert || body.NodeType == ExpressionType.ConvertChecked)
            {
                body = ((UnaryExpression)body).Operand;
            }

            var result = new HashSet<MemberInfo>();

            switch (body.NodeType)
            {
                case ExpressionType.MemberAccess:
                    {
                        var m = (MemberExpression)body;
                        result.Add(m.Member);
                        break;
                    }

                case ExpressionType.New:
                    {
                        var nex = (NewExpression)body;
                        for (int i = 0; i < nex.Arguments.Count; i++)
                        {
                            var arg = nex.Arguments[i];
                            if (arg.NodeType == ExpressionType.Convert || arg.NodeType == ExpressionType.ConvertChecked)
                            {
                                arg = ((UnaryExpression)arg).Operand;
                            }

                            if (arg is MemberExpression me)
                            {
                                result.Add(me.Member);
                            }
                            else
                            {
                                throw new NotSupportedException($"Unsupported field selection argument '{arg.NodeType}'. Only simple member accesses are supported.");
                            }
                        }

                        break;
                    }

                default:
                    throw new NotSupportedException($"Unsupported field selection expression '{body.NodeType}'. Use e => e.Member or e => new {{ e.Member1, e.Member2 }}.");
            }

            return result;
        }
    }
}

