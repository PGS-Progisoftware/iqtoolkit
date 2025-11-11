# LINQ to ADS SQL Translation - Complete Walkthrough

## Overview

This document provides a detailed, step-by-step explanation of how IQToolkit translates a LINQ query into Advantage Database Server (ADS) SQL.

**Example Query:**
```csharp
provider.GetTable<LocGen>()
    .Where(lg => lg.DATELOC >= DateTime.Now)
    .Take(10)
    .ToList();
```

**Expected ADS SQL Output:**
```sql
SELECT TOP 10 t0.NUMLOC, t0.CODECLT, t0.CODEPER1, t0.DATEDEP, t0.DATEFIN, 
       t0.DATERET, t0.DATELOC, t0.STATUT, t0.STATUT2, t0.TOTALHT, t0.AFFAIRE, 
       t0.DATECREAT, t0.INITCREAT, t0.DATEMAJ, t0.INITMAJ, t0.OBS, 
  t0.HEUREDEP, t0.VALIDTECH, t0.VALIDCOMM
FROM LocGen AS t0
WHERE (t0.DATELOC >= ?)
```

---

## Initial Expression Tree

When the C# compiler processes the LINQ query, it creates an expression tree:

```
MethodCallExpression {
    NodeType = Call,
    Method = Enumerable.ToList<LocGen>(),
    Arguments = [
    MethodCallExpression {
         Method = Queryable.Take<LocGen>(int),
            Arguments = [
       MethodCallExpression {
    Method = Queryable.Where<LocGen>(Func<LocGen, bool>),
            Arguments = [
        ConstantExpression {
           Type = EntityTable<LocGen>,
            Value = [EntityTable instance]
               },
     UnaryExpression (Quote) {
       Operand = LambdaExpression {
        Parameters = [ ParameterExpression { Name = "lg", Type = LocGen } ],
          Body = BinaryExpression {
        NodeType = GreaterThanOrEqual,
     Left = MemberExpression {
       Expression = ParameterExpression { Name = "lg" },
         Member = LocGen.DATELOC
               },
    Right = PropertyExpression {
        Expression = null,
   Member = DateTime.Now
              }
      }
     }
            }
   ]
       },
                ConstantExpression { Value = 10, Type = int }
  ]
        }
    ]
}
```

---

## Translation Pipeline Entry Point

### Step 1: Query Execution Trigger

When `.ToList()` is called, it triggers enumeration which calls:

```csharp
// In EntityProvider.cs
public override object Execute(Expression expression)
{
    LambdaExpression lambda = expression as LambdaExpression;

    // Check cache (skipped in this example)
if (lambda == null && this.cache != null && expression.NodeType != ExpressionType.Constant)
    {
        return this.cache.Execute(expression);
    }

  // Compile the expression to an executable delegate
    var compiled = this.Compile(expression);
    return compiled();
}
```

### Step 2: Compilation - Get Execution Plan

```csharp
private Func<object> Compile(Expression expression)
{
    LambdaExpression lambda = expression as LambdaExpression;

    // THIS IS WHERE THE MAGIC HAPPENS
    // Convert LINQ expression tree to executable SQL + projector
    Expression plan = this.GetExecutionPlan(expression);

    if (lambda != null)
    {
        // For parameterized queries
        LambdaExpression fn = Expression.Lambda(lambda.Type, plan, lambda.Parameters);
        var d = fn.Compile();
        return () => d;
    }
    else
    {
        // For simple queries (our case)
     Expression<Func<object>> efn = Expression.Lambda<Func<object>>(
            Expression.Convert(plan, typeof(object)));
return efn.Compile();
    }
}
```

### Step 3: Build Execution Plan

```csharp
public virtual Expression GetExecutionPlan(Expression expression)
{
    // Strip off lambda if present
    LambdaExpression lambda = expression as LambdaExpression;
 if (lambda != null)
        expression = lambda.Body;

    // Create the QueryTranslator with Language, Mapping, and Policy
    QueryTranslator translator = this.CreateTranslator();

    // Translate query into client & server parts
    Expression translation = translator.Translate(expression);

    // Find provider reference for execution
    var parameters = lambda != null ? lambda.Parameters : null;
 Expression provider = this.Find(expression, parameters, typeof(EntityProvider));
    if (provider == null)
    {
        Expression rootQueryable = this.Find(expression, parameters, typeof(IQueryable));
        provider = Expression.Property(rootQueryable, 
     typeof(IQueryable).GetTypeInfo().GetDeclaredProperty("Provider"));
    }

    // Build the execution plan (combines SQL generation + result materialization)
    return translator.Police.BuildExecutionPlan(translation, provider);
}
```

For `AdvantageQueryProvider`, the translator is created with:
```csharp
protected virtual QueryTranslator CreateTranslator()
{
    return new QueryTranslator(
        AdvantageLanguage.Default,     // SQL dialect rules
        new AttributeMapping(),     // Entity-to-table mapping
        QueryPolicy.Default  // Execution policies
    );
}
```

---

## The 4-Phase Translation Pipeline

### QueryTranslator.Translate() Method

```csharp
public virtual Expression Translate(Expression expression)
{
    // PHASE 1: Pre-evaluate local sub-trees
    expression = PartialEvaluator.Eval(expression, 
        this.Mapper.Mapping.CanBeEvaluatedLocally);

    // PHASE 2: Apply mapping (bind LINQ operators to SQL concepts)
    expression = this.Mapper.Translate(expression);

    // PHASE 3: Apply policy rules (eager loading, etc.)
    expression = this.Police.Translate(expression);

    // PHASE 4: Apply language-specific translations
    expression = this.Linguist.Translate(expression);

  return expression;
}
```

---

## PHASE 1: Partial Evaluation

### Purpose
Replace local variables and property accesses (like `DateTime.Now`) with their actual constant values.

### Input Expression Tree
```
BinaryExpression (GreaterThanOrEqual) {
    Left = MemberExpression { lg.DATELOC },
    Right = PropertyExpression { DateTime.Now }  // ? Needs to be evaluated NOW
}
```

### Process

```csharp
// In PartialEvaluator.cs
public static Expression PartialEval(Expression expression, 
    Func<Expression, bool> fnCanBeEvaluated)
{
    return new SubtreeEvaluator(
      new Nominator(fnCanBeEvaluated).Nominate(expression)
  ).Eval(expression);
}
```

#### Step 1.1: Nominate Evaluatable Sub-trees

The `Nominator` class walks the tree bottom-up, identifying nodes that can be evaluated locally:

```csharp
private static bool CanBeEvaluatedLocally(Expression expression)
{
    // Parameters cannot be evaluated (they represent query inputs)
    return expression.NodeType != ExpressionType.Parameter;
}
```

For our expression:
- `lg` (ParameterExpression) ? **Cannot** be evaluated
- `lg.DATELOC` ? **Cannot** be evaluated (depends on parameter)
- `DateTime.Now` ? **Can** be evaluated (no dependencies)

#### Step 1.2: Evaluate Sub-trees

```csharp
private Expression Evaluate(Expression e)
{
    if (e.NodeType == ExpressionType.Constant)
    {
        return e;  // Already evaluated
    }

    // Compile the expression to a lambda and execute it
    LambdaExpression lambda = Expression.Lambda(e);
    Delegate fn = lambda.Compile();
    
    // Execute to get the value
  object value = fn.DynamicInvoke(null);
  
    // Return as constant
    return Expression.Constant(value, e.Type);
}
```

`DateTime.Now` is evaluated at this moment (e.g., `2024-12-20 15:45:30`) and becomes:

```csharp
ConstantExpression {
    Value = DateTime(2024, 12, 20, 15, 45, 30),
    Type = typeof(DateTime)
}
```

### Output After Phase 1

```
MethodCallExpression {
    Method = Queryable.Take<LocGen>(int),
 Arguments = [
  MethodCallExpression {
            Method = Queryable.Where<LocGen>(Func<LocGen, bool>),
            Arguments = [
        ConstantExpression { EntityTable<LocGen> },
            LambdaExpression {
    Parameters = [ lg ],
     Body = BinaryExpression (GreaterThanOrEqual) {
       Left = MemberExpression { lg.DATELOC },
         Right = ConstantExpression { DateTime(2024, 12, 20, 15, 45, 30) }  // ? EVALUATED!
       }
              }
     ]
        },
        ConstantExpression { 10 }
    ]
}
```

---

## PHASE 2: Query Binding (Mapper.Translate)

### Purpose
Convert LINQ operators to SQL expression nodes (custom expression types like `SelectExpression`, `TableExpression`, `ColumnExpression`).

### Process

```csharp
public virtual Expression Translate(Expression expression)
{
    // Convert references to LINQ operators into query specific nodes
    var bound = QueryBinder.Bind(this, expression);

    // Move aggregate computations so they occur in same select as group-by
    var aggmoved = AggregateRewriter.Rewrite(this.Translator.Linguist.Language, bound);

    // Do reduction so duplicate associations are likely to be clumped together
    var reduced = UnusedColumnRemover.Remove(aggmoved);
    reduced = RedundantColumnRemover.Remove(reduced);
    reduced = RedundantSubqueryRemover.Remove(reduced);
    reduced = RedundantJoinRemover.Remove(reduced);

    // Convert references to association properties into correlated queries
    var rbound = RelationshipBinder.Bind(this, reduced);
    if (rbound != reduced)
    {
        rbound = RedundantColumnRemover.Remove(rbound);
        rbound = RedundantJoinRemover.Remove(rbound);
    }

    // Rewrite comparison checks between entities
    var result = ComparisonRewriter.Rewrite(this.Mapping, rbound);

    return result;
}
```

### Sub-Phase 2.1: QueryBinder - Process Method Calls

The `QueryBinder` walks the expression tree and processes LINQ operators:

```csharp
protected override Expression VisitMethodCall(MethodCallExpression m)
{
    if (m.Method.DeclaringType == typeof(Queryable) || 
m.Method.DeclaringType == typeof(Enumerable))
    {
        switch (m.Method.Name)
        {
 case "Where":
     return this.BindWhere(m.Type, m.Arguments[0], 
  GetLambda(m.Arguments[1]));
         
 case "Take":
   return this.BindTake(m.Arguments[0], m.Arguments[1]);
      
   // ... other operators
        }
    }
}
```

#### Step 2.1.1: Process `GetTable<LocGen>()` - Create Base Table Projection

When the `ConstantExpression` containing `EntityTable<LocGen>` is visited:

```csharp
protected override Expression VisitConstant(ConstantExpression c)
{
    if (this.IsQuery(c))
  {
        IQueryable q = (IQueryable)c.Value;
  IEntityTable t = q as IEntityTable;
   
        if (t != null)
        {
          // Get entity mapping (reads [Column] attributes from LocGen class)
        MappingEntity entity = this.mapper.Mapping.GetEntity(
        t.ElementType, t.EntityId);
     
            // Create the base query expression for this table
    return this.VisitSequence(
         this.mapper.GetQueryExpression(entity));
 }
    }
    return c;
}
```

This creates a `ProjectionExpression` for the table:

```csharp
ProjectionExpression {
    Select = SelectExpression {
        Alias = "t0",
        Columns = [
            ColumnDeclaration {
    Name = "NUMLOC",
      Expression = ColumnExpression {
Type = typeof(string),
      QueryType = QueryType(CHAR, 9),
       Alias = "t0",
              Name = "NUMLOC",
           Ordinal = 0
          }
},
            ColumnDeclaration {
       Name = "CODECLT",
    Expression = ColumnExpression {
        Type = typeof(string),
  QueryType = QueryType(CHAR, 10),
        Alias = "t0",
                Name = "CODECLT",
 Ordinal = 1
    }
            },
    ColumnDeclaration {
    Name = "DATELOC",
                Expression = ColumnExpression {
     Type = typeof(DateTime),
        QueryType = QueryType(Date),
         Alias = "t0",
     Name = "DATELOC",
     Ordinal = 6
                }
            },
            // ... all 19 columns from LocGen
        ],
        From = TableExpression {
    Type = typeof(IQueryable<LocGen>),
Alias = "t0",
            Name = "LocGen",  // ? Derived from class name
            Entity = MappingEntity(LocGen)
        },
        Where = null,
        OrderBy = null,
        GroupBy = null
    },
    Projector = MemberInitExpression {
        // This describes how to construct a LocGen instance from columns
        NewExpression = new LocGen(),
        Bindings = [
            MemberAssignment {
         Member = LocGen.NUMLOC,
      Expression = ColumnExpression("t0", "NUMLOC", 0)
            },
   MemberAssignment {
       Member = LocGen.CODECLT,
    Expression = ColumnExpression("t0", "CODECLT", 1)
       },
MemberAssignment {
       Member = LocGen.DATELOC,
  Expression = ColumnExpression("t0", "DATELOC", 6)
   },
          // ... all field bindings
        ]
    },
    Aggregator = null
}
```

**Key Points:**
- `TableExpression` represents `FROM LocGen AS t0`
- `ColumnDeclaration` represents each SELECT column
- `Projector` describes how to build `LocGen` objects from result rows
- Column metadata comes from `[Column]` attributes

#### Step 2.1.2: Process `Where()` - Add WHERE Clause

```csharp
private Expression BindWhere(Type resultType, Expression source, 
LambdaExpression predicate)
{
    // Get the projection from GetTable<LocGen>()
    ProjectionExpression projection = this.VisitSequence(source);
    
    // Map the lambda parameter (lg) to the projector
    // This allows us to resolve lg.DATELOC later
    this.map[predicate.Parameters[0]] = projection.Projector;
    
    // Visit the predicate body: lg.DATELOC >= constantDate
    Expression where = this.Visit(predicate.Body);
    
    // Create new alias for this SELECT layer
 var alias = this.GetNextAlias();  // Returns "t1"
    
    // Determine which columns to project
    ProjectedColumns pc = this.ProjectColumns(
        projection.Projector, 
        alias, 
   projection.Select.Alias);
    
    // Create new SelectExpression with WHERE clause
  return new ProjectionExpression(
        new SelectExpression(alias, pc.Columns, projection.Select, where),
        pc.Projector
    );
}
```

##### Step 2.1.2.1: Resolve `lg.DATELOC` Member Access

When visiting `lg.DATELOC`:

```csharp
protected override Expression VisitMemberAccess(MemberExpression m)
{
    // m.Expression is the parameter 'lg'
    Expression source = this.Visit(m.Expression);  
    
    // source is now the MemberInitExpression (projector)
    // Bind the member access to find what it refers to
    Expression result = BindMember(source, m.Member);  // m.Member is DATELOC
    
    return result;
}

public static Expression BindMember(Expression source, MemberInfo member)
{
    switch (source.NodeType)
    {
        case ExpressionType.MemberInit:
            MemberInitExpression min = (MemberInitExpression)source;
            
   // Find the binding for DATELOC field
          for (int i = 0, n = min.Bindings.Count; i < n; i++)
  {
 MemberAssignment assign = min.Bindings[i] as MemberAssignment;
          
    if (assign != null && MembersMatch(assign.Member, member))
       {
   // Return the expression bound to DATELOC
         return assign.Expression;  
           // ? Returns ColumnExpression("t0", "DATELOC", 6)
            }
   }
  break;
    }
    
    return Expression.MakeMemberAccess(source, member);
}
```

So `lg.DATELOC` resolves to:
```csharp
ColumnExpression {
    Type = typeof(DateTime),
    QueryType = QueryType(Date),
    Alias = "t0",
    Name = "DATELOC",
    Ordinal = 6
}
```

##### Step 2.1.2.2: Build WHERE Expression

The predicate body becomes:

```csharp
BinaryExpression {
    NodeType = GreaterThanOrEqual,
    Left = ColumnExpression {
        Alias = "t0",
        Name = "DATELOC"
    },
    Right = ConstantExpression {
  Value = DateTime(2024, 12, 20, 15, 45, 30)
    }
}
```

##### Step 2.1.2.3: Result After BindWhere

```csharp
ProjectionExpression {
    Select = SelectExpression {
        Alias = "t1",
        Columns = [
 // All 19 columns, but now referencing t0.NUMLOC, t0.CODECLT, etc.
    ],
 From = SelectExpression {
       Alias = "t0",
   Columns = [ /* all columns */ ],
            From = TableExpression("LocGen", "t0"),
      Where = null
        },
        Where = BinaryExpression {
      NodeType = GreaterThanOrEqual,
            Left = ColumnExpression("t0", "DATELOC"),
            Right = ConstantExpression { DateTime(2024, 12, 20, 15, 45, 30) }
        }
    },
    Projector = MemberInitExpression {
   // Now references t1 columns
    }
}
```

**Current SQL Structure:**
```sql
SELECT t1.NUMLOC, t1.CODECLT, ..., t1.DATELOC, ...
FROM (
    SELECT t0.NUMLOC, t0.CODECLT, ..., t0.DATELOC, ...
    FROM LocGen AS t0
) AS t1
WHERE (t0.DATELOC >= @p0)
```

This is **redundant** - we'll fix it in optimization.

#### Step 2.1.3: Process `Take(10)` - Add TOP Clause

```csharp
private Expression BindTake(Expression source, Expression take)
{
 ProjectionExpression projection = this.VisitSequence(source);
    take = this.Visit(take);  // Visit the constant 10
    
    SelectExpression select = projection.Select;
  var alias = this.GetNextAlias();  // "t2"
    
    ProjectedColumns pc = this.ProjectColumns(
        projection.Projector, alias, projection.Select.Alias);
    
    return new ProjectionExpression(
        new SelectExpression(
   alias, 
            pc.Columns, 
            projection.Select, 
            null,      // where
      null,      // orderBy
      null,  // groupBy
            false,     // isDistinct
null,      // skip
          take,      // ? take
            false      // isReverse
        ),
    pc.Projector
    );
}
```

**Result:**
```csharp
ProjectionExpression {
    Select = SelectExpression {
        Alias = "t2",
  Columns = [ /* all columns */ ],
        From = SelectExpression {  // t1
      From = SelectExpression {  // t0
          From = TableExpression("LocGen", "t0")
   }
        },
    Take = ConstantExpression { 10 }
    }
}
```

Now we have **three nested SELECTs**! This will be optimized.

### Sub-Phase 2.2: Optimization Passes

#### UnusedColumnRemover

Walks the tree and identifies which columns are actually used:

```csharp
internal Expression Remove(Expression expression)
{
    this.allColumnsUsed = new Dictionary<string, HashSet<string>>();
    return this.Visit(expression);
}

protected override Expression VisitColumn(ColumnExpression column)
{
    // Track that this column is used
    HashSet<string> columns;
    if (!this.allColumnsUsed.TryGetValue(column.Alias, out columns))
    {
        columns = new HashSet<string>();
      this.allColumnsUsed.Add(column.Alias, columns);
    }
    columns.Add(column.Name);
    return column;
}
```

For our query, all columns are referenced in the final projector, so none are removed yet.

#### RedundantSubqueryRemover

This is the key optimization that flattens unnecessary nested SELECTs:

```csharp
internal Expression Remove(Expression expression)
{
    return this.Visit(expression);
}

protected override Expression VisitSelect(SelectExpression select)
{
    select = (SelectExpression)base.VisitSelect(select);

    // First remove all purely redundant subqueries
    List<SelectExpression> redundant = 
     new RedundantSubqueryGatherer().Gather(select.From);

    if (redundant != null)
    {
        select = (SelectExpression)new SubqueryRemover().Remove(select, redundant);
    }

    // Next attempt to merge subqueries
    SelectExpression fromSelect = select.From as SelectExpression;

    if (fromSelect != null)
    {
        // Can only merge if subquery has simple-projection (no renames or complex expressions)
    if (HasSimpleProjection(fromSelect))
        {
            // Remove the redundant subquery
    select = (SelectExpression)new SubqueryRemover().Remove(select, fromSelect);

// Merge WHERE expressions
    Expression where = select.Where;

            if (fromSelect.Where != null)
        {
                if (where != null)
    {
 where = Expression.And(fromSelect.Where, where);
      }
    else
     {
            where = fromSelect.Where;
          }
     }

     if (where != select.Where)
            {
   return new SelectExpression(
   select.Alias, select.Columns, select.From, where, select.OrderBy);
     }
        }
  }

    return select;
}
```

**What makes a subquery redundant?**
```csharp
private static bool IsRedundantSubquery(SelectExpression select)
{
    return HasSimpleProjection(select)  // Columns are just references, no expressions
   && select.Where == null          // No WHERE clause
        && (select.OrderBy == null || select.OrderBy.Count == 0);  // No ORDER BY
}

private static bool HasSimpleProjection(SelectExpression select)
{
    foreach (ColumnDeclaration decl in select.Columns)
    {
        ColumnExpression col = decl.Expression as ColumnExpression;

        if (col == null || decl.Name != col.Name)
        {
   // Column name changed or column expression is more complex
    return false;
        }
    }

    return true;
}
```

**Applied to our query:**

1. **t2** (outermost): Has no WHERE, no ORDER BY, simple projection ? Merge with t1
2. **t1**: Has WHERE clause ? Keep it, but can merge with t0
3. **t0**: Base table ? Keep

After merging:

```csharp
ProjectionExpression {
    Select = SelectExpression {
        Alias = "t0",
        Columns = [
      ColumnDeclaration("NUMLOC", ColumnExpression("t0", "NUMLOC")),
         ColumnDeclaration("CODECLT", ColumnExpression("t0", "CODECLT")),
          ColumnDeclaration("DATELOC", ColumnExpression("t0", "DATELOC")),
            // ... all columns
        ],
 From = TableExpression("LocGen", "t0"),
        Where = BinaryExpression (GreaterThanOrEqual) {
      Left = ColumnExpression("t0", "DATELOC"),
          Right = ConstantExpression { DateTime(2024, 12, 20, 15, 45, 30) }
        },
        Take = ConstantExpression { 10 }
    },
    Projector = MemberInitExpression {
  // Builds LocGen from t0 columns
    }
}
```

**SQL Structure Now:**
```sql
SELECT t0.NUMLOC, t0.CODECLT, ..., t0.DATELOC, ...
FROM LocGen AS t0
WHERE (t0.DATELOC >= @p0)
-- Take(10) will be applied as TOP 10 during formatting
```

Much better!

#### RedundantColumnRemover

Removes duplicate column declarations (none in our case).

#### RedundantJoinRemover

Removes unnecessary JOINs (none in our case).

---

## PHASE 3: Policy Translation (Police.Translate)

### Purpose
Apply query execution policies like eager loading relationships.

```csharp
public virtual Expression Translate(Expression expression)
{
    // Add included relationships to client projection
    var rewritten = RelationshipIncluder.Include(this.Translator.Mapper, expression);
    if (rewritten != expression)
    {
        expression = rewritten;
        expression = UnusedColumnRemover.Remove(expression);
        expression = RedundantColumnRemover.Remove(expression);
        expression = RedundantSubqueryRemover.Remove(expression);
      expression = RedundantJoinRemover.Remove(expression);
    }

    // Convert singleton projections into server-side joins
    rewritten = SingletonProjectionRewriter.Rewrite(
        this.Translator.Linguist.Language, expression);
if (rewritten != expression)
    {
     expression = rewritten;
  expression = UnusedColumnRemover.Remove(expression);
  expression = RedundantColumnRemover.Remove(expression);
        expression = RedundantSubqueryRemover.Remove(expression);
        expression = RedundantJoinRemover.Remove(expression);
    }

  // Convert projections into client-side joins
    rewritten = ClientJoinedProjectionRewriter.Rewrite(
        this.Policy, this.Translator.Linguist.Language, expression);
    if (rewritten != expression)
 {
  expression = rewritten;
    expression = UnusedColumnRemover.Remove(expression);
        expression = RedundantColumnRemover.Remove(expression);
        expression = RedundantSubqueryRemover.Remove(expression);
      expression = RedundantJoinRemover.Remove(expression);
    }

    return expression;
}
```

Since we're using `QueryPolicy.Default` (no eager loading, no includes), this phase makes **no changes** to our simple query.

**Output:** Same as input.

---

## PHASE 4: Language Translation (Linguist.Translate)

### Purpose
Apply Advantage-specific SQL optimizations and transformations.

```csharp
public virtual Expression Translate(Expression expression)
{
    // Remove redundant layers again before cross apply rewrite
    expression = UnusedColumnRemover.Remove(expression);
    expression = RedundantColumnRemover.Remove(expression);
    expression = RedundantSubqueryRemover.Remove(expression);

    // Convert cross-apply and outer-apply joins into inner & left-outer-joins if possible
    var rewritten = CrossApplyRewriter.Rewrite(this.Language, expression);

    // Convert cross joins into inner joins
    rewritten = CrossJoinRewriter.Rewrite(rewritten);

    if (rewritten != expression)
    {
   expression = rewritten;
        // Do final reduction
        expression = UnusedColumnRemover.Remove(expression);
        expression = RedundantSubqueryRemover.Remove(expression);
        expression = RedundantJoinRemover.Remove(expression);
        expression = RedundantColumnRemover.Remove(expression);
    }

    return expression;
}
```

For our simple query with no JOINs, this phase makes **no changes**.

**Output:** Same as input.

---

## PHASE 5: Parameterization

After the 4-phase translation, the expression is parameterized:

```csharp
public virtual Expression Parameterize(Expression expression)
{
    return Parameterizer.Parameterize(this.Language, expression);
}
```

The `Parameterizer` identifies constant values in WHERE clauses and converts them to parameters:

### Before:
```csharp
Where = BinaryExpression (GreaterThanOrEqual) {
    Left = ColumnExpression("t0", "DATELOC"),
    Right = ConstantExpression {
        Value = DateTime(2024, 12, 20, 15, 45, 30),
        Type = typeof(DateTime)
    }
}
```

### After:
```csharp
Where = BinaryExpression (GreaterThanOrEqual) {
    Left = ColumnExpression("t0", "DATELOC"),
  Right = NamedValueExpression {
    Name = "p0",
        QueryType = QueryType(Date),
        Value = ConstantExpression { DateTime(2024, 12, 20, 15, 45, 30) }
    }
}
```

**Why parameterize?**
- Enables query plan caching in the database
- Prevents SQL injection
- Allows reuse of compiled queries

---

## PHASE 6: SQL Text Generation

Now we format the expression tree into actual SQL text.

```csharp
public virtual string Format(Expression expression)
{
    // Use common SQL formatter by default (or Advantage-specific)
    return SqlFormatter.Format(expression);
}
```

### AdvantageFormatter (or SqlFormatter) Walkthrough

```csharp
internal string Format(Expression expression)
{
    this.sb = new StringBuilder();
    this.Visit(expression);
    return this.sb.ToString();
}
```

#### Step 6.1: Visit ProjectionExpression

The `ProjectionExpression` is the root, but we only format its `Select` part:

```csharp
protected override Expression VisitProjection(ProjectionExpression proj)
{
    // We don't visit the projector (that's for client-side materialization)
    // Just format the SQL SELECT
    return this.Visit(proj.Select);
}
```

#### Step 6.2: Visit SelectExpression

```csharp
protected override Expression VisitSelect(SelectExpression select)
{
    this.AddAliases(select.From);  // Track aliases in scope
    
    this.Write("SELECT ");

    // Handle TOP for Take
    if (select.Take != null && select.Skip == null)
    {
        this.Write("TOP ");
        this.Visit(select.Take);  // Writes "10"
        this.Write(" ");
    }

    // Write columns
    this.WriteColumns(select.Columns);

    // FROM clause
    if (select.From != null)
    {
    this.WriteLine(Indentation.Same);
        this.Write("FROM ");
        this.VisitSource(select.From);
    }

    // WHERE clause
    if (select.Where != null)
    {
  this.WriteLine(Indentation.Same);
        this.Write("WHERE ");
   this.Visit(select.Where);
    }

 // ORDER BY, GROUP BY, etc. (not present in our query)

    return select;
}
```

#### Step 6.3: Write SELECT Columns

```csharp
protected virtual void WriteColumns(ReadOnlyCollection<ColumnDeclaration> columns)
{
    for (int i = 0, n = columns.Count; i < n; i++)
    {
        ColumnDeclaration column = columns[i];

  if (i > 0)
        {
            this.Write(", ");
        }

     // Visit the column expression
     ColumnExpression c = this.Visit(column.Expression) as ColumnExpression;

    // Add alias if the output name differs from source name
        if (c == null || c.Name != column.Name)
        {
      this.Write(" AS ");
    this.Write(column.Name);
    }
    }
}

protected override Expression VisitColumn(ColumnExpression column)
{
    if (!string.IsNullOrEmpty(column.Alias))
    {
    this.Write(column.Alias);  // "t0"
        this.Write(".");
    }
    this.Write(column.Name);  // "NUMLOC", "CODECLT", etc.
    return column;
}
```

**Output:**
```sql
SELECT TOP 10 t0.NUMLOC, t0.CODECLT, t0.CODEPER1, t0.DATEDEP, t0.DATEFIN, t0.DATERET, t0.DATELOC, t0.STATUT, t0.STATUT2, t0.TOTALHT, t0.AFFAIRE, t0.DATECREAT, t0.INITCREAT, t0.DATEMAJ, t0.INITMAJ, t0.OBS, t0.HEUREDEP, t0.VALIDTECH, t0.VALIDCOMM
```

#### Step 6.4: Write FROM Clause

```csharp
protected override Expression VisitSource(Expression source)
{
    switch ((DbExpressionType)source.NodeType)
    {
        case DbExpressionType.Table:
   TableExpression table = (TableExpression)source;
     this.Write(this.Language.Quote(table.Name));  // Quote if needed
            this.Write(" AS ");
     this.Write(table.Alias.Name);
            break;

        case DbExpressionType.Select:
     // Nested SELECT (not in our case after optimization)
            SelectExpression select = (SelectExpression)source;
            this.Write("(");
            this.WriteLine(Indentation.Inner);
     this.Visit(select);
            this.WriteLine(Indentation.Outer);
 this.Write(")");
         this.Write(" AS ");
       this.Write(select.Alias.Name);
            break;
 }

    return source;
}
```

For Advantage, `Quote()` doesn't add quotes (Advantage doesn't require them for standard identifiers):

```csharp
public override string Quote(string name)
{
    return name;  // Advantage doesn't use quotes for identifiers
}
```

**Output:**
```sql
FROM LocGen AS t0
```

#### Step 6.5: Write WHERE Clause

```csharp
protected override Expression VisitBinary(BinaryExpression b)
{
    this.Write("(");
    this.Visit(b.Left);

    switch (b.NodeType)
    {
     case ExpressionType.And:
            this.Write(" AND ");
            break;
      case ExpressionType.Or:
 this.Write(" OR");
        break;
      case ExpressionType.Equal:
        this.Write(" = ");
  break;
        case ExpressionType.NotEqual:
 this.Write(" <> ");
         break;
        case ExpressionType.LessThan:
            this.Write(" < ");
            break;
    case ExpressionType.LessThanOrEqual:
       this.Write(" <= ");
            break;
        case ExpressionType.GreaterThan:
      this.Write(" > ");
 break;
        case ExpressionType.GreaterThanOrEqual:
  this.Write(" >= ");
   break;
   default:
            throw new NotSupportedException(
                $"Binary operator '{b.NodeType}' is not supported");
    }

    this.Visit(b.Right);
    this.Write(")");
    return b;
}

protected override Expression VisitNamedValue(NamedValueExpression value)
{
    this.Write("?");  // Advantage uses ? for parameters
    return value;
}
```

**Output:**
```sql
WHERE (t0.DATELOC >= ?)
```

### Final SQL Output

```sql
SELECT TOP 10 t0.NUMLOC, t0.CODECLT, t0.CODEPER1, t0.DATEDEP, t0.DATEFIN, t0.DATERET, t0.DATELOC, t0.STATUT, t0.STATUT2, t0.TOTALHT, t0.AFFAIRE, t0.DATECREAT, t0.INITCREAT, t0.DATEMAJ, t0.INITMAJ, t0.OBS, t0.HEUREDEP, t0.VALIDTECH, t0.VALIDCOMM
FROM LocGen AS t0
WHERE (t0.DATELOC >= ?)
```

**Parameters:**
- Parameter 0: `DateTime(2024, 12, 20, 15, 45, 30)`

---

## PHASE 7: Build Execution Plan

The `ExecutionBuilder` creates a lambda expression that executes the SQL and materializes results.

```csharp
public static Expression Build(QueryLinguist linguist, QueryPolicy policy, 
    Expression expression, Expression provider)
{
    var executor = Expression.Parameter(typeof(QueryExecutor), "executor");
    var builder = new ExecutionBuilder(linguist, policy, executor);
    
    // Initialize executor variable
    builder.variables.Add(executor);
    builder.initializers.Add(
    Expression.Call(
            Expression.Convert(provider, typeof(IQueryExecutorFactory)),
            "CreateExecutor", 
      null, 
            null
        )
    );
    
    var result = builder.Build(expression);
    return result;
}
```

### Step 7.1: Create QueryCommand

```csharp
QueryCommand command = new QueryCommand(
    commandText: "SELECT TOP 10 t0.NUMLOC, ...",
    parameters: [
        new QueryParameter("p0", typeof(DateTime), QueryType(Date))
    ]
);
```

### Step 7.2: Build Projector Function

The projector reads columns from a `FieldReader` and constructs `LocGen` instances:

```csharp
// Create parameter for FieldReader
ParameterExpression reader = Expression.Parameter(typeof(FieldReader), "r0");

// Build member initialization
Expression projector = Expression.MemberInit(
    Expression.New(typeof(LocGen)),
    
    // NUMLOC = r0.GetValue(0) as string
    Expression.Bind(
        typeof(LocGen).GetField("NUMLOC"),
        Expression.Convert(
            Expression.Call(
      reader, 
         typeof(FieldReader).GetMethod("GetValue"),
            Expression.Constant(0)
 ),
            typeof(string)
        )
    ),
  
  // CODECLT = r0.GetValue(1) as string
    Expression.Bind(
   typeof(LocGen).GetField("CODECLT"),
        Expression.Convert(
      Expression.Call(reader, "GetValue", null, Expression.Constant(1)),
      typeof(string)
        )
    ),
    
    // DATELOC = (DateTime)r0.GetValue(6)
    Expression.Bind(
typeof(LocGen).GetField("DATELOC"),
        Expression.Convert(
            Expression.Call(reader, "GetValue", null, Expression.Constant(6)),
         typeof(DateTime)
     )
    ),
    
    // ... all 19 fields
);

// Wrap in lambda
LambdaExpression projectorLambda = Expression.Lambda<Func<FieldReader, LocGen>>(
    projector, 
    reader
);

// Compile to delegate
Func<FieldReader, LocGen> projectorFunc = projectorLambda.Compile();
```

### Step 7.3: Build Execution Call

```csharp
Expression executionCall = Expression.Call(
    executor,
    typeof(QueryExecutor).GetMethod("Execute").MakeGenericMethod(typeof(LocGen)),
    Expression.Constant(command),        // SQL command
    Expression.Constant(projectorFunc), // Projector function
    Expression.Constant(entity, typeof(MappingEntity)), // Entity metadata
    Expression.NewArrayInit(typeof(object),       // Parameter values
        Expression.Convert(
 Expression.Constant(DateTime(2024, 12, 20, 15, 45, 30)),
            typeof(object)
        )
    )
);
```

### Step 7.4: Wrap in Variable Initialization

```csharp
// Final execution plan (simplified)
Expression executionPlan = Expression.Block(
    new[] { executor },  // Variables
    
    // executor = provider.CreateExecutor()
    Expression.Assign(
        executor,
        Expression.Call(
        Expression.Convert(provider, typeof(IQueryExecutorFactory)),
            "CreateExecutor",
null,
 null
        )
 ),
    
    // return executor.Execute<LocGen>(command, projector, entity, paramValues)
  executionCall
);
```

---

## PHASE 8: Execute Query

Finally, the execution plan is compiled to a delegate and invoked:

```csharp
// Compile the execution plan
Expression<Func<object>> efn = Expression.Lambda<Func<object>>(
    Expression.Convert(executionPlan, typeof(object))
);
Func<object> compiled = efn.Compile();

// Execute!
object result = compiled();
```

### Step 8.1: Create Executor

```csharp
// In AdvantageQueryProvider
protected override QueryExecutor CreateExecutor()
{
    return new Executor(this);
}

private class Executor : DbQueryExecutor
{
    AdvantageQueryProvider provider;

    public Executor(AdvantageQueryProvider provider)
     : base(provider)
    {
this.provider = provider;
    }
}
```

### Step 8.2: Execute SQL Command

```csharp
public override IEnumerable<T> Execute<T>(
    QueryCommand command,
    Func<FieldReader, T> fnProjector,
    MappingEntity entity,
 object[] paramValues)
{
    // Log if enabled
    this.LogCommand(command, paramValues);
    
    // Open connection if needed
  this.StartUsingConnection();

    try
    {
        // Create ADO.NET command
        DbCommand cmd = this.GetCommand(command, paramValues);
        
        // Log the actual SQL
        this.LogMessage("");
  
        // Execute and get data reader
        DbDataReader reader = this.ExecuteReader(cmd);
        
        // Project rows to objects
        var result = Project(reader, fnProjector);

    // Defer closing connection if streaming results
        if (this.ActionOpenedConnection)
        {
  result = new EnumerateOnce<T>(result);
     }

        return result;
    }
    finally
    {
        this.StopUsingConnection();
    }
}
```

### Step 8.3: Create ADO.NET Command

```csharp
protected virtual DbCommand GetCommand(QueryCommand query, object[] paramValues)
{
    // Create Advantage command
    DbCommand cmd = this.connection.CreateCommand();
cmd.CommandText = query.CommandText;

    // Set transaction if active
    if (this.transaction != null)
    {
        cmd.Transaction = this.transaction;
    }

    // Add parameters
    for (int i = 0, n = query.Parameters.Count; i < n; i++)
    {
        var qp = query.Parameters[i];
     
 // Create ADO.NET parameter
        var p = cmd.CreateParameter();
        p.ParameterName = qp.Name;  // "p0"
        p.Value = paramValues[i] ?? DBNull.Value;  // DateTime value
        
    // Set parameter type if specified
        if (qp.QueryType != null)
        {
            // Advantage type mapping
        p.DbType = qp.QueryType.DbType;
        }
        
        cmd.Parameters.Add(p);
    }

  return cmd;
}
```

**Created Command:**
```
CommandText: SELECT TOP 10 t0.NUMLOC, t0.CODECLT, ..., t0.VALIDCOMM
       FROM LocGen AS t0
      WHERE (t0.DATELOC >= ?)
Parameters: 
  [0] = 2024-12-20 15:45:30 (DateTime)
```

### Step 8.4: Execute Reader

```csharp
protected DbDataReader ExecuteReader(DbCommand command)
{
    DbDataReader reader = command.ExecuteReader();
    return reader;
}
```

Advantage executes the SQL against the CDX tables.

### Step 8.5: Project Rows to Objects

```csharp
private static IEnumerable<T> Project<T>(
    DbDataReader reader, 
    Func<FieldReader, T> fnProjector)
{
    var fieldReader = new FieldReader(reader);

  while (reader.Read())
    {
 // Call the projector for each row
     yield return fnProjector(fieldReader);
    }

    reader.Dispose();
}
```

For each row, the compiled projector function:
1. Reads column values via `FieldReader.GetValue(ordinal)`
2. Constructs a new `LocGen` instance
3. Assigns each field
4. Returns the instance

**Example for one row:**
```csharp
LocGen instance = new LocGen();
instance.NUMLOC = fieldReader.GetValue(0) as string;       // "LOC000001"
instance.CODECLT = fieldReader.GetValue(1) as string;      // "CLT001"
instance.CODEPER1 = fieldReader.GetValue(2) as string;     // "PER001"
instance.DATEDEP = (DateTime)fieldReader.GetValue(3);      // 2024-01-15
instance.DATEFIN = (DateTime)fieldReader.GetValue(4);      // 2024-01-20
instance.DATERET = (DateTime)fieldReader.GetValue(5);      // 2024-01-20
instance.DATELOC = (DateTime)fieldReader.GetValue(6);  // 2024-01-10
instance.STATUT = (ushort?)fieldReader.GetValue(7);      // 1
instance.STATUT2 = (char?)fieldReader.GetValue(8);         // 'A'
instance.TOTALHT = (decimal)fieldReader.GetValue(9);       // 1250.00
instance.AFFAIRE = fieldReader.GetValue(10) as string;     // "Project X"
instance.DATECREAT = (DateTime)fieldReader.GetValue(11);   // 2024-01-05
instance.INITCREAT = fieldReader.GetValue(12) as string;   // "JDOE"
instance.DATEMAJ = (DateTime)fieldReader.GetValue(13);     // 2024-01-10
instance.INITMAJ = fieldReader.GetValue(14) as string;     // "JDOE"
instance.OBS = fieldReader.GetValue(15) as string;     // "Notes..."
instance.HEUREDEP = fieldReader.GetValue(16) as string;  // "14:30"
instance.VALIDTECH = fieldReader.GetValue(17) as string;   // "Y"
instance.VALIDCOMM = fieldReader.GetValue(18) as string;   // "N"

// Association properties (Client, Per1) are NOT populated by this query
// They would require separate queries or JOINs

return instance;
```

### Step 8.6: Materialize List

`.ToList()` consumes the enumerable:

```csharp
public static List<T> ToList<T>(this IEnumerable<T> source)
{
    var list = new List<T>();
    foreach (var item in source)
    {
        list.Add(item);
    }
    return list;
}
```

Up to 10 `LocGen` instances are added to the list.

---

## Summary: Complete Translation Flow

```
???????????????????????????????????????????????????????????????????????
? C# LINQ Query    ?
? provider.GetTable<LocGen>()         ?
?     .Where(lg => lg.DATELOC >= DateTime.Now)    ?
?     .Take(10)           ?
?     .ToList()          ?
???????????????????????????????????????????????????????????????????????
         ?
      ?
???????????????????????????????????????????????????????????????????????
? PHASE 1: Partial Evaluation       ?
? • DateTime.Now ? ConstantExpression(2024-12-20 15:45:30)     ?
???????????????????????????????????????????????????????????????????????
        ?
              ?
???????????????????????????????????????????????????????????????????????
? PHASE 2: Query Binding     ?
? • GetTable<LocGen>() ? TableExpression("LocGen", "t0")    ?
? • lg.DATELOC ? ColumnExpression("t0", "DATELOC")                ?
? • Where() ? SelectExpression with WHERE clause  ?
? • Take(10) ? SelectExpression with Take = 10  ?
? • Optimizations:          ?
?   - Flatten 3 nested SELECTs ? 1 SELECT               ?
?   - Remove unused columns (none)              ?
?   - Remove redundant joins (none)       ?
???????????????????????????????????????????????????????????????????????
         ?
              ?
???????????????????????????????????????????????????????????????????????
? PHASE 3: Policy Translation     ?
? • No changes (default policy, no eager loading)      ?
???????????????????????????????????????????????????????????????????????
         ?
      ?
???????????????????????????????????????????????????????????????????????
? PHASE 4: Language Translation       ?
? • No changes (no JOINs to optimize)           ?
???????????????????????????????????????????????????????????????????????
        ?
      ?
???????????????????????????????????????????????????????????????????????
? PHASE 5: Parameterization   ?
? • ConstantExpression(DateTime) ? NamedValueExpression("p0")        ?
???????????????????????????????????????????????????????????????????????
               ?
 ?
???????????????????????????????????????????????????????????????????????
? PHASE 6: SQL Text Generation        ?
? • SelectExpression ? SQL text    ?
? • AdvantageFormatter walks expression tree           ?
? • Output:  ?
?   SELECT TOP 10 t0.NUMLOC, t0.CODECLT, ..., t0.VALIDCOMM          ?
?   FROM LocGen AS t0        ?
?   WHERE (t0.DATELOC >= ?)                 ?
???????????????????????????????????????????????????????????????????????
    ?
        ?
???????????????????????????????????????????????????????????????????????
? PHASE 7: Build Execution Plan          ?
? • Create QueryCommand(sql, parameters)    ?
? • Build projector: Func<FieldReader, LocGen>            ?
?   - Reads each column by ordinal           ?
? - Constructs LocGen instance        ?
?   - No reflection! Compiled delegate       ?
? • Wrap in executor.Execute() call                 ?
???????????????????????????????????????????????????????????????????????
       ?
   ?
???????????????????????????????????????????????????????????????????????
? PHASE 8: Execute Query   ?
? • Create DbCommand (AdsCommand)       ?
? • Add parameter: p0 = DateTime(2024-12-20 15:45:30)         ?
? • Execute reader against Advantage DB        ?
? • For each row:               ?
?- Call projector(fieldReader)               ?
?   - Yield LocGen instance      ?
? • .ToList() materializes up to 10 instances      ?
???????????????????????????????????????????????????????????????????????
    ?
       ?
           List<LocGen> (10 items)
```

---

## Key Architectural Points

### 1. Expression Tree Immutability

Expression nodes are immutable. Every transformation creates new nodes:

```csharp
// Never do this:
select.Where = newWhereExpression;  // ? No setter!

// Instead:
var newSelect = new SelectExpression(
    select.Alias,
select.Columns,
    select.From,
    newWhereExpression,  // ? New WHERE
    select.OrderBy
);
```

### 2. Visitor Pattern Everywhere

All transformations use the Visitor pattern:

```csharp
internal class MyRewriter : DbExpressionVisitor
{
    protected override Expression VisitSelect(SelectExpression select)
    {
  // Visit children first
        select = (SelectExpression)base.VisitSelect(select);
        
    // Apply transformation
    if (ShouldRewrite(select))
 {
            return RewriteSelect(select);
}
        
        return select;
    }
}
```

### 3. Separation of Concerns

- **QueryBinder** - LINQ semantics ? SQL concepts
- **QueryMapper** - Entity mapping
- **QueryPolice** - Execution policies
- **QueryLinguist** - Database-specific rules
- **QueryFormatter** - SQL text generation
- **ExecutionBuilder** - Execution plan compilation
- **QueryExecutor** - ADO.NET execution

### 4. Optimization Passes

Multiple passes clean up the expression tree:
- Remove unused columns
- Flatten redundant subqueries
- Eliminate unnecessary JOINs
- Merge WHERE clauses

Each pass is independent and can be run multiple times.

### 5. Type Safety

Every expression node carries type information:
```csharp
ColumnExpression {
    Type = typeof(DateTime),           // CLR type
    QueryType = QueryType(Date),// SQL type
    Alias = "t0",
    Name = "DATELOC"
}
```

### 6. Parameterization

Constants in WHERE clauses become parameters:
- Enables query plan caching
- Prevents SQL injection
- Improves performance

### 7. Compiled Projectors

The projector function is compiled once:
```csharp
Func<FieldReader, LocGen> projector = /* compiled lambda */;

// Reused for every row - no reflection!
while (reader.Read())
{
    yield return projector(fieldReader);
}
```

### 8. Lazy Execution

The query isn't executed until enumeration:
```csharp
var query = provider.GetTable<LocGen>()
    .Where(lg => lg.DATELOC >= DateTime.Now);  // Not executed yet!

var list = query.ToList();  // ? Executed here
```

---

## Advantage-Specific Details

### Parameter Markers

Advantage uses `?` for parameters (not `@p0`):

```sql
WHERE (t0.DATELOC >= ?)  -- Advantage
-- vs
WHERE (t0.DATELOC >= @p0)  -- SQL Server
```

### TOP Clause

Advantage supports `TOP` for limiting results:

```sql
SELECT TOP 10 ...  -- Advantage ?
-- vs
SELECT ... LIMIT 10  -- MySQL, PostgreSQL
-- vs
SELECT ... FETCH FIRST 10 ROWS ONLY  -- Standard SQL
```

### Identifier Quoting

Advantage doesn't require quotes for standard identifiers:

```sql
SELECT t0.NUMLOC FROM LocGen AS t0  -- Advantage ?
-- vs
SELECT "t0"."NUMLOC" FROM "LocGen" AS "t0"  -- PostgreSQL
-- vs
SELECT [t0].[NUMLOC] FROM [LocGen] AS [t0]  -- SQL Server
```

### Date Type Mapping

```csharp
public override QueryType GetColumnType(Type type)
{
    if (type == typeof(DateTime))
    {
        return new QueryType(DbType.Date, false, 0);  // ADS Date type
    }
    // ...
}
```

---

## Performance Considerations

### 1. Constant Folding
`DateTime.Now` is evaluated once during translation, not for each row.

### 2. Query Plan Caching
Parameterized queries allow database to cache execution plans.

### 3. Minimal Allocations
Compiled projectors avoid boxing and reflection overhead.

### 4. Streaming Results
Results are yielded one at a time, not loaded entirely into memory (unless `.ToList()` is called).

### 5. Connection Management
Connection is opened only when needed and closed promptly.

---

## Debugging Tips

### View Generated SQL

```csharp
string sql = provider.GetQueryText(query.Expression);
Console.WriteLine(sql);
```

### View Full Execution Plan

```csharp
string plan = provider.GetQueryPlan(query.Expression);
Console.WriteLine(plan);
```

### Enable SQL Logging

```csharp
provider.Log = Console.Out;
```

### Breakpoint Locations

Set breakpoints in:
- `QueryTranslator.Translate()` - See each phase
- `QueryBinder.BindWhere()` - See WHERE translation
- `AdvantageFormatter.VisitSelect()` - See SQL generation
- `QueryExecutor.Execute()` - See execution

---

## Conclusion

The IQToolkit translation process is a sophisticated multi-phase pipeline that:

1. **Evaluates** local expressions
2. **Binds** LINQ operators to SQL concepts
3. **Optimizes** the expression tree
4. **Applies** policies and language rules
5. **Generates** SQL text
6. **Compiles** projector functions
7. **Executes** against the database
8. **Materializes** results efficiently

This architecture provides:
- **Type safety** - Compile-time checking
- **Composability** - Build queries incrementally
- **Performance** - Compiled projectors, minimal allocations
- **Extensibility** - Easy to add new providers
- **Debuggability** - Inspect at any stage

All while producing clean, efficient SQL specific to each database dialect.
