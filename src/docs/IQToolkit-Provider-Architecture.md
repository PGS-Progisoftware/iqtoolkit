# IQToolkit Provider Architecture Documentation

## Overview

IQToolkit is a LINQ IQueryable provider framework that translates LINQ queries into database-specific SQL. This document provides a comprehensive technical overview of the architecture, main components, and the workflow for creating a new provider.

---

## Core Architecture Components

### 1. EntityProvider - The Foundation

`EntityProvider` is the abstract base class that all database providers inherit from. It orchestrates the entire query translation and execution process.

**Key Responsibilities:**
- Manages the three core components: `QueryLanguage`, `QueryMapping`, and `QueryPolicy`
- Provides table access via `GetTable<T>()` methods
- Coordinates query translation through `QueryTranslator`
- Builds execution plans that combine SQL generation with result materialization
- Manages query caching and logging

**Constructor:**
```csharp
public EntityProvider(QueryLanguage language, QueryMapping mapping, QueryPolicy policy)
{
    this.language = language;
    this.mapping = mapping ?? new AttributeMapping();
    this.policy = policy ?? QueryPolicy.Default;
    this.tables = new Dictionary<MappingEntity, IEntityTable>();
}
```

**Core Properties:**
- `QueryLanguage Language` - Database-specific SQL dialect rules
- `QueryMapping Mapping` - Entity-to-table mapping configuration
- `QueryPolicy Policy` - Query execution policies (eager/lazy loading, etc.)
- `QueryCache Cache` - Optional query result caching
- `TextWriter Log` - SQL logging output

**Abstract Methods to Implement:**
```csharp
protected abstract QueryExecutor CreateExecutor();
public abstract void DoTransacted(Action action);
public abstract void DoConnected(Action action);
public abstract int ExecuteCommand(string commandText);
```

---

## The Translation Pipeline

### High-Level Flow

```
LINQ Expression ? Execution Plan ? SQL + Projector ? Execute ? Results
```

### Detailed Translation Steps

#### Step 1: Query Expression Entry Point

```csharp
public override object Execute(Expression expression)
{
    var compiled = this.Compile(expression);
 return compiled();
}

private Func<object> Compile(Expression expression)
{
    Expression plan = this.GetExecutionPlan(expression);
    Expression<Func<object>> efn = Expression.Lambda<Func<object>>(
        Expression.Convert(plan, typeof(object)));
    return efn.Compile();
}
```

#### Step 2: Build Execution Plan

```csharp
public virtual Expression GetExecutionPlan(Expression expression)
{
 // Create translator
    QueryTranslator translator = this.CreateTranslator();
    
    // Translate query into client & server parts
    Expression translation = translator.Translate(expression);
    
    // Build execution plan (combines SQL + materialization logic)
    return translator.Police.BuildExecutionPlan(translation, provider);
}
```

#### Step 3: QueryTranslator - The Orchestrator

The `QueryTranslator` coordinates three specialized components:

```csharp
public class QueryTranslator
{
    public QueryLinguist Linguist { get; }  // Language-specific rules
    public QueryMapper Mapper { get; }    // Mapping rules
    public QueryPolice Police { get; }   // Policy enforcement
    
    public QueryTranslator(QueryLanguage language, QueryMapping mapping, QueryPolicy policy)
    {
 this.Linguist = language.CreateLinguist(this);
        this.Mapper = mapping.CreateMapper(this);
        this.Police = policy.CreatePolice(this);
    }
    
    public virtual Expression Translate(Expression expression)
    {
        // 1. Pre-evaluate local sub-trees (convert local variables to constants)
        expression = PartialEvaluator.Eval(expression, 
         this.Mapper.Mapping.CanBeEvaluatedLocally);
        
   // 2. Apply mapping (bind LINQ operators to SQL concepts)
        expression = this.Mapper.Translate(expression);
     
        // 3. Apply policy rules (includes, defer loading)
  expression = this.Police.Translate(expression);
  
// 4. Apply language-specific translations
        expression = this.Linguist.Translate(expression);
      
      return expression;
    }
}
```

---

## Main Classes for a New Provider

### 1. QueryLanguage - Database SQL Dialect

Defines database-specific language characteristics and SQL generation rules.

**Key Responsibilities:**
- Type system mapping (CLR types ? SQL types)
- SQL syntax features (quoting, multiple commands, subqueries)
- Aggregate function support
- Join handling strategies

**Abstract Members:**
```csharp
public abstract class QueryLanguage
{
    public abstract QueryTypeSystem TypeSystem { get; }
    public abstract Expression GetGeneratedIdExpression(MemberInfo member);
    
public virtual string Quote(string name) { return name; }
    public virtual bool AllowsMultipleCommands { get { return false; } }
    public virtual bool AllowSubqueryInSelectWithoutFrom { get { return false; } }
    public virtual bool AllowDistinctInAggregates { get { return false; } }
    
    public virtual Expression GetRowsAffectedExpression(Expression command);
    public virtual Expression GetOuterJoinTest(SelectExpression select);
    public virtual bool IsScalar(Type type);
    public virtual bool IsAggregate(MemberInfo member);
    public virtual bool CanBeColumn(Expression expression);
    public virtual QueryLinguist CreateLinguist(QueryTranslator translator);
}
```

**Example Implementation:**
```csharp
public class AdvantageLanguage : QueryLanguage
{
    public static readonly AdvantageLanguage Default = new AdvantageLanguage();
    
    public override QueryTypeSystem TypeSystem => AdvantageTypeSystem.Default;
    
    public override string Quote(string name)
    {
        return $"\"{name}\"";  // Use quotes for identifiers
    }
    
    public override bool AllowsMultipleCommands => true;
    
    public override Expression GetGeneratedIdExpression(MemberInfo member)
    {
        return new FunctionExpression(typeof(int), "LASTAUTOINC", null);
    }
}
```

### 2. QueryTypeSystem - Type Mapping

Maps CLR types to database-specific SQL types.

```csharp
public abstract class QueryTypeSystem
{
    public abstract QueryType GetColumnType(Type type);
    public abstract string Format(QueryType type, bool suppressSize);
}

public class AdvantageTypeSystem : QueryTypeSystem
{
    public override QueryType GetColumnType(Type type)
    {
        // Map CLR types to Advantage SQL types
     if (type == typeof(string))
 return new QueryType(SqlDbType.VarChar, true, 255);
        if (type == typeof(int))
        return new QueryType(SqlDbType.Int, false, 0);
        // ... more mappings
    }
}
```

### 3. QueryMapping - Entity Mapping

Defines how CLR entities map to database tables and columns.

**Key Responsibilities:**
- Entity-to-table name mapping
- Property-to-column mapping
- Primary key identification
- Relationship definitions
- Computed/generated column handling

**Abstract Members:**
```csharp
public abstract class QueryMapping
{
    public abstract MappingEntity GetEntity(Type elementType, string entityId);
    public abstract MappingEntity GetEntity(MemberInfo contextMember);
    public abstract IEnumerable<MemberInfo> GetMappedMembers(MappingEntity entity);
    public abstract bool IsPrimaryKey(MappingEntity entity, MemberInfo member);
    public abstract bool IsRelationship(MappingEntity entity, MemberInfo member);
    
    public virtual bool IsGenerated(MappingEntity entity, MemberInfo member);
    public virtual bool IsComputed(MappingEntity entity, MemberInfo member);
    public virtual bool IsReadOnly(MappingEntity entity, MemberInfo member);
    
public abstract QueryMapper CreateMapper(QueryTranslator translator);
}
```

**Common Implementations:**
- `AttributeMapping` - Uses attributes like `[Table]`, `[Column]`, `[Association]`
- `XmlMapping` - Uses XML configuration files
- `ImplicitMapping` - Convention-based (type name = table name, property = column)

### 4. QueryPolicy - Execution Policies

Defines how queries are executed and results are materialized.

```csharp
public class QueryPolicy
{
    public virtual bool IsIncluded(MemberInfo member) { return false; }
    public virtual bool IsDeferLoaded(MemberInfo member) { return false; }
    public virtual QueryPolice CreatePolice(QueryTranslator translator);
}

// Advanced policy with eager loading
public class EntityPolicy : QueryPolicy
{
    public void Include(MemberInfo member);
    public void IncludeWith<TEntity>(Expression<Func<TEntity, object>> fnMember);
    public void AssociateWith(LambdaExpression memberQuery);
    public void Apply<TEntity>(Expression<Func<IEnumerable<TEntity>, IEnumerable<TEntity>>> fnApply);
}
```

### 5. QueryExecutor - Database Execution

Handles actual database command execution and result reading.

```csharp
public abstract class QueryExecutor
{
    public abstract int RowsAffected { get; }
 
    public abstract IEnumerable<T> Execute<T>(
      QueryCommand command,
        Func<FieldReader, T> fnProjector,
        MappingEntity entity,
    object[] paramValues);
    
    public abstract IEnumerable<int> ExecuteBatch(
        QueryCommand query,
        IEnumerable<object[]> paramSets,
        int batchSize,
     bool stream);
    
    public abstract int ExecuteCommand(QueryCommand query, object[] paramValues);
}
```

---

## Custom Expression Tree Nodes

IQToolkit extends LINQ expression trees with database-specific nodes:

```csharp
internal enum DbExpressionType
{
    Table = 1000,       // FROM table
    Column,    // Column reference
    Select,   // SELECT statement
    Projection,            // Result projection with materialization
    Join,    // JOIN clauses
    Aggregate,    // Aggregate functions (COUNT, SUM, etc.)
    Scalar, // Scalar subquery
    Exists,     // EXISTS subquery
    In,                    // IN clause
    AggregateSubquery,     // Aggregate in subquery
    // ... more
}
```

**Key Expression Types:**

```csharp
// SELECT statement
internal class SelectExpression : Expression
{
    public string Alias { get; }
    public ReadOnlyCollection<ColumnDeclaration> Columns { get; }
 public Expression From { get; }
    public Expression Where { get; }
    public ReadOnlyCollection<OrderExpression> OrderBy { get; }
    public IEnumerable<Expression> GroupBy { get; }
}

// Column reference
internal class ColumnExpression : Expression
{
    public string Alias { get; }
    public string Name { get; }
    public QueryType QueryType { get; }
}

// Table reference
internal class TableExpression : Expression
{
    public TableAlias Alias { get; }
    public string Name { get; }
    public MappingEntity Entity { get; }
}

// Projection with materialization
internal class ProjectionExpression : Expression
{
    public SelectExpression Select { get; }
    public Expression Projector { get; }
    public LambdaExpression Aggregator { get; }
}
```

---

## Translation Workflow Details

### Phase 1: Partial Evaluation

**Purpose:** Replace local variables and constants with their actual values.

**Example:**
```csharp
string city = "London";
var query = db.Customers.Where(c => c.City == city);

// Before: c.City == value(Program+<>c__DisplayClass0).city
// After:  c.City == "London"
```

**Implementation:**
```csharp
expression = PartialEvaluator.Eval(expression, 
    this.Mapper.Mapping.CanBeEvaluatedLocally);
```

### Phase 2: Query Binding (Mapper.Translate)

**Purpose:** Convert LINQ operators to SQL expression nodes.

**Steps:**
1. **QueryBinder** - Binds LINQ operators
   - `Where()` ? `SelectExpression` with WHERE clause
   - `Select()` ? `SelectExpression` with column projections
   - `Join()` ? `JoinExpression`
   - `OrderBy()` ? `SelectExpression` with ORDER BY
   - `GroupBy()` ? `SelectExpression` with GROUP BY

2. **AggregateRewriter** - Moves aggregates to correct SELECT level
3. **UnusedColumnRemover** - Removes unreferenced columns
4. **RedundantColumnRemover** - Eliminates duplicate columns
5. **RedundantSubqueryRemover** - Flattens nested queries
6. **RedundantJoinRemover** - Removes unnecessary joins
7. **RelationshipBinder** - Converts association properties to queries
8. **ComparisonRewriter** - Rewrites entity comparisons

**Example:**
```csharp
// LINQ Query
db.Customers
    .Where(c => c.City == "London")
    .Select(c => new { c.Name, c.Phone })

// Becomes SelectExpression tree:
SelectExpression {
    Alias = "t0",
    Columns = [
        ColumnDeclaration("Name", ColumnExpression("t0", "ContactName")),
        ColumnDeclaration("Phone", ColumnExpression("t0", "Phone"))
    ],
    From = TableExpression("Customers", "t0"),
    Where = BinaryExpression(Equal, 
    ColumnExpression("t0", "City"),
        ConstantExpression("London"))
}
```

### Phase 3: Policy Translation (Police.Translate)

**Purpose:** Apply execution policies like eager loading.

**Key Rewriters:**
1. **RelationshipIncluder** - Adds eagerly loaded relationships
2. **SingletonProjectionRewriter** - Converts 1:1 projections to server-side JOINs
3. **ClientJoinedProjectionRewriter** - Handles client-side joins for collections

**Example:**
```csharp
// With policy
policy.IncludeWith<Customer>(c => c.Orders);

// Query becomes:
db.Customers.Select(c => new {
    Customer = c,
    Orders = c.Orders.ToList()  // Eagerly loaded
})
```

### Phase 4: Language Translation (Linguist.Translate)

**Purpose:** Apply database-specific optimizations and transformations.

**Key Rewriters:**
1. **UnusedColumnRemover** - Final column cleanup
2. **RedundantColumnRemover** - Final duplicate removal
3. **RedundantSubqueryRemover** - Final query flattening
4. **CrossApplyRewriter** - Convert CROSS APPLY to JOINs when possible
5. **CrossJoinRewriter** - Optimize cross joins to inner joins
6. **Parameterizer** - Identify query parameters

### Phase 5: SQL Generation

**Purpose:** Convert expression tree to SQL text.

```csharp
string commandText = QueryFormatter.Format(selectExpression);
```

**Output Example:**
```sql
SELECT t0.ContactName AS Name, t0.Phone
FROM Customers AS t0
WHERE (t0.City = @p0)
```

### Phase 6: Execution Plan Building

**Purpose:** Create a compiled function that executes SQL and materializes results.

**ExecutionBuilder** creates:
1. SQL command with parameters
2. `FieldReader` for reading results
3. Projector function that constructs objects from reader
4. Final lambda that orchestrates execution

```csharp
// Simplified execution plan
Expression plan = Expression.Call(
    executor,
    "Execute",
new[] { typeof(Customer) },
    commandExpression,
    projectorExpression,
    entityExpression,
    parametersExpression
);
```

### Phase 7: Execution

```csharp
QueryExecutor executor = CreateExecutor();
return executor.Execute<T>(
    command,
    projector,  // Func<FieldReader, T>
    entity,
    paramValues
);
```

---

## Creating a New Provider - Complete Example

### Step 1: Implement the Provider Class

```csharp
public class MyDbQueryProvider : DbQueryProvider
{
    public MyDbQueryProvider(
        DbConnection connection,
        QueryMapping mapping = null,
        QueryPolicy policy = null)
     : base(
       connection,
          MyDbLanguage.Default,
            mapping ?? new AttributeMapping(),
   policy ?? QueryPolicy.Default)
{
    }
    
    // Optional: Override for custom execution
    protected override QueryExecutor CreateExecutor()
    {
  return new MyDbExecutor(this);
    }
}
```

### Step 2: Implement QueryLanguage

```csharp
public class MyDbLanguage : QueryLanguage
{
    public static readonly MyDbLanguage Default = new MyDbLanguage();
    
    private MyDbLanguage() { }
    
    public override QueryTypeSystem TypeSystem => MyDbTypeSystem.Default;
    
    public override string Quote(string name)
 {
    // Database-specific identifier quoting
 return $"[{name}]";  // SQL Server style
        // or
     return $"\"{name}\""; // Standard SQL / PostgreSQL
        // or
return $"`{name}`";   // MySQL
    }
    
    public override bool AllowsMultipleCommands => true;
    
    public override bool AllowSubqueryInSelectWithoutFrom => true;
    
    public override Expression GetGeneratedIdExpression(MemberInfo member)
    {
        // How to get last inserted ID
        return new FunctionExpression(typeof(int), "SCOPE_IDENTITY", null);
  // or
        return new FunctionExpression(typeof(int), "LAST_INSERT_ID", null);
    }
    
    public override Expression GetRowsAffectedExpression(Expression command)
    {
        return new FunctionExpression(typeof(int), "@@ROWCOUNT", null);
    }
}
```

### Step 3: Implement QueryTypeSystem

```csharp
public class MyDbTypeSystem : QueryTypeSystem
{
    public static readonly MyDbTypeSystem Default = new MyDbTypeSystem();
    
    public override QueryType GetColumnType(Type type)
    {
     type = TypeHelper.GetNonNullableType(type);
        
        switch (Type.GetTypeCode(type))
   {
         case TypeCode.Boolean:
        return new QueryType(SqlDbType.Bit, false, 0);
             
            case TypeCode.Byte:
           return new QueryType(SqlDbType.TinyInt, false, 0);
   
   case TypeCode.Int16:
      return new QueryType(SqlDbType.SmallInt, false, 0);
    
            case TypeCode.Int32:
          return new QueryType(SqlDbType.Int, false, 0);
    
     case TypeCode.Int64:
                return new QueryType(SqlDbType.BigInt, false, 0);
     
            case TypeCode.Decimal:
      return new QueryType(SqlDbType.Decimal, false, 0);
         
            case TypeCode.Single:
   return new QueryType(SqlDbType.Real, false, 0);
     
            case TypeCode.Double:
    return new QueryType(SqlDbType.Float, false, 0);
           
case TypeCode.String:
          return new QueryType(SqlDbType.NVarChar, true, 4000);
        
       case TypeCode.DateTime:
    return new QueryType(SqlDbType.DateTime, false, 0);
    
          case TypeCode.Object:
             if (type == typeof(byte[]))
          return new QueryType(SqlDbType.VarBinary, true, int.MaxValue);
        if (type == typeof(Guid))
         return new QueryType(SqlDbType.UniqueIdentifier, false, 0);
      if (type == typeof(DateTimeOffset))
      return new QueryType(SqlDbType.DateTimeOffset, false, 0);
    if (type == typeof(TimeSpan))
 return new QueryType(SqlDbType.Time, false, 0);
        break;
      }

        return new QueryType(SqlDbType.Variant, false, 0);
 }
    
    public override string Format(QueryType type, bool suppressSize)
    {
        StringBuilder sb = new StringBuilder();
        
 switch (type.SqlDbType)
 {
       case SqlDbType.BigInt:
     sb.Append("BIGINT");
   break;
            case SqlDbType.Int:
          sb.Append("INT");
             break;
  case SqlDbType.SmallInt:
    sb.Append("SMALLINT");
           break;
      case SqlDbType.TinyInt:
                sb.Append("TINYINT");
          break;
 case SqlDbType.Bit:
        sb.Append("BIT");
       break;
          case SqlDbType.Decimal:
             sb.Append("DECIMAL");
     if (type.Precision != 0)
      {
   sb.AppendFormat("({0},{1})", type.Precision, type.Scale);
       }
     break;
   case SqlDbType.Float:
      sb.Append("FLOAT");
             break;
  case SqlDbType.Real:
    sb.Append("REAL");
          break;
    case SqlDbType.VarChar:
     sb.Append("VARCHAR");
              if (type.Length > 0 && !suppressSize)
           {
          sb.AppendFormat("({0})", type.Length);
 }
  break;
case SqlDbType.NVarChar:
    sb.Append("NVARCHAR");
       if (type.Length > 0 && !suppressSize)
          {
     sb.AppendFormat("({0})", type.Length);
                }
     break;
            case SqlDbType.DateTime:
        sb.Append("DATETIME");
     break;
            // ... more types
        }
        
        return sb.ToString();
    }
}
```

### Step 4: Implement QueryExecutor (Optional)

Most providers can use `DbQueryProvider`'s default executor, but you can customize:

```csharp
public class MyDbExecutor : DbQueryExecutor
{
    public MyDbExecutor(MyDbQueryProvider provider)
        : base(provider)
    {
    }
    
    // Override if needed for custom result reading
    public override IEnumerable<T> Execute<T>(
  QueryCommand command,
 Func<FieldReader, T> fnProjector,
        MappingEntity entity,
    object[] paramValues)
    {
        this.LogCommand(command, paramValues);
        this.StartUsingConnection();
      
        try
  {
   DbCommand cmd = this.GetCommand(command, paramValues);
            this.LogMessage("");
     DbDataReader reader = this.ExecuteReader(cmd);
       var result = Project(reader, fnProjector);
          
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
}
```

### Step 5: Implement Custom Formatter (Optional)

For database-specific SQL syntax:

```csharp
public class MyDbFormatter : SqlFormatter
{
    protected MyDbFormatter(QueryLanguage language)
    : base(language)
    {
    }
    
    // Override specific formatting if needed
    protected override Expression VisitSelect(SelectExpression select)
    {
        // Custom SELECT formatting
      this.AddAliases(select.From);
        this.Write("SELECT ");
        
  if (select.IsDistinct)
        {
            this.Write("DISTINCT ");
        }
        
        // Custom TOP/LIMIT handling
        if (select.Take != null && select.Skip == null)
        {
     this.Write("TOP ");
            this.Visit(select.Take);
            this.Write(" ");
        }
        
        // Columns
        this.WriteColumns(select.Columns);
 
        // FROM
        if (select.From != null)
 {
         this.WriteLine(Indentation.Same);
    this.Write("FROM ");
 this.VisitSource(select.From);
   }
        
        // WHERE
     if (select.Where != null)
        {
 this.WriteLine(Indentation.Same);
        this.Write("WHERE ");
            this.Visit(select.Where);
        }
        
        // Custom OFFSET/FETCH for pagination
        if (select.Skip != null)
        {
    this.WriteLine(Indentation.Same);
            this.Write("OFFSET ");
  this.Visit(select.Skip);
            this.Write(" ROWS");
 
  if (select.Take != null)
            {
        this.Write(" FETCH NEXT ");
          this.Visit(select.Take);
         this.Write(" ROWS ONLY");
            }
        }
        
  return select;
    }
}
```

### Step 6: Usage

```csharp
// Create connection
var connection = new MyDbConnection("connection_string");

// Optional: Create custom mapping
var mapping = new AttributeMapping();

// Optional: Create custom policy with eager loading
var policy = new EntityPolicy();
policy.IncludeWith<Customer>(c => c.Orders);

// Create provider
var provider = new MyDbQueryProvider(connection, mapping, policy);

// Get table
var customers = provider.GetTable<Customer>();

// Execute LINQ query
var query = customers
    .Where(c => c.City == "London")
    .Select(c => new { c.Name, c.Phone });

foreach (var customer in query)
{
    Console.WriteLine($"{customer.Name}: {customer.Phone}");
}
```

---

## Key Visitor Pattern Classes

IQToolkit heavily uses the Visitor pattern for expression tree manipulation:

### DbExpressionVisitor

Base visitor for custom SQL expression nodes:

```csharp
internal class DbExpressionVisitor : ExpressionVisitor
{
    protected override Expression Visit(Expression exp)
    {
        if (exp == null) return null;
        
        switch ((DbExpressionType)exp.NodeType)
        {
     case DbExpressionType.Table:
    return this.VisitTable((TableExpression)exp);
case DbExpressionType.Column:
                return this.VisitColumn((ColumnExpression)exp);
            case DbExpressionType.Select:
  return this.VisitSelect((SelectExpression)exp);
            // ... more cases
            default:
       return base.Visit(exp);
        }
 }
    
    protected virtual Expression VisitTable(TableExpression table);
    protected virtual Expression VisitColumn(ColumnExpression column);
    protected virtual Expression VisitSelect(SelectExpression select);
    // ... more visit methods
}
```

### Key Optimization Visitors

1. **UnusedColumnRemover** - Removes unreferenced columns
2. **RedundantSubqueryRemover** - Flattens unnecessary subqueries
3. **RedundantJoinRemover** - Eliminates unnecessary joins
4. **AggregateRewriter** - Moves aggregates to appropriate SELECT level
5. **CrossApplyRewriter** - Converts CROSS APPLY to JOINs when possible
6. **ColumnProjector** - Determines which expressions should be columns
7. **OrderByRewriter** - Handles ORDER BY with complex expressions

---

## Summary Checklist for New Provider

### Required Components:
- [ ] **Provider Class** extending `DbQueryProvider` or `EntityProvider`
- [ ] **QueryLanguage** implementation
- [ ] **QueryTypeSystem** implementation
- [ ] Connection handling (open/close/transactions)

### Optional Components:
- [ ] **Custom QueryExecutor** for specialized execution
- [ ] **Custom SqlFormatter** for database-specific syntax
- [ ] **Custom QueryMapper** for advanced mapping scenarios
- [ ] **Custom QueryPolicy** for eager loading strategies
- [ ] **Custom QueryMapping** beyond AttributeMapping

### Testing Checklist:
- [ ] Basic SELECT queries
- [ ] WHERE clauses with parameters
- [ ] JOIN operations
- [ ] ORDER BY with multiple columns
- [ ] GROUP BY with aggregates
- [ ] Subqueries
- [ ] INSERT/UPDATE/DELETE operations
- [ ] Transactions
- [ ] Relationship navigation
- [ ] Eager loading
- [ ] Pagination (Skip/Take)

---

## Debugging Tips

### 1. View Generated SQL

```csharp
string sql = provider.GetQueryText(query.Expression);
Console.WriteLine(sql);
```

### 2. View Full Execution Plan

```csharp
string plan = provider.GetQueryPlan(query.Expression);
Console.WriteLine(plan);
```

### 3. Enable SQL Logging

```csharp
provider.Log = Console.Out;
```

### 4. Step Through Translation

Set breakpoints in:
- `QueryTranslator.Translate()`
- `QueryBinder.Bind()`
- `QueryLinguist.Translate()`
- Your custom `SqlFormatter` methods

---

## Common Pitfalls

1. **Forgetting to handle NULL values** in type system
2. **Not implementing proper identifier quoting** for reserved keywords
3. **Missing type conversions** in QueryTypeSystem
4. **Incorrect ORDER BY with SKIP/TAKE** (database-specific pagination)
5. **Not handling database-specific function translations**
6. **Forgetting connection/transaction management**

---

## Architecture Diagram

```
???????????????????????????????????????????????????????????????????
?   EntityProvider ?
?  ????????????????????????????????  ????????????????    ?
?  ?QueryLanguage ?  ?QueryMapping  ?  ?QueryPolicy   ?         ?
?  ????????????????  ????????????????  ????????????????     ?
???????????????????????????????????????????????????????????????????
       ?
         ?
???????????????????????????????????????????????????????????????????
?        QueryTranslator         ?
?  ????????????????  ???????????????????????????????? ?
?  ?QueryLinguist ?  ?QueryMapper   ?  ?QueryPolice   ?         ?
?  ????????????????  ????????????????  ????????????????      ?
???????????????????????????????????????????????????????????????????
        ?
    ?
        ??????????????????????????????????????????
        ?    Translation Pipeline (4 Phases)  ?
   ??????????????????????????????????????????
   ? 1. Partial Evaluation     ?
        ?- Replace local variables           ?
        ?     ?
  ? 2. Query Binding (Mapper.Translate)    ?
     ?    - QueryBinder?
        ?    - AggregateRewriter      ?
        ?    - Column/Join/Subquery Removers     ?
        ?          ?
        ? 3. Policy Translation           ?
        ?    - RelationshipIncluder           ?
     ?    - ProjectionRewriter         ?
?       ?
        ? 4. Language Translation          ?
        ?    - CrossApplyRewriter       ?
    ?    - Parameterizer          ?
   ??????????????????????????????????????????
  ?
           ?
    ??????????????????????????????????????????
        ?   SQL Expression Tree       ?
        ?  (SelectExpression, TableExpression,   ?
        ?   ColumnExpression, JoinExpression)    ?
        ??????????????????????????????????????????
     ?
 ?
        ??????????????????????????????????????????
        ?         QueryFormatter        ?
        ?      (Generate SQL Text)               ?
        ??????????????????????????????????????????
       ?
  ?
        ??????????????????????????????????????????
        ?       ExecutionBuilder         ?
  ?  (Build Execution Plan Lambda)         ?
        ??????????????????????????????????????????
  ?
          ?
      ??????????????????????????????????????????
        ?   QueryExecutor     ?
    ?  (Execute SQL & Materialize Results)   ?
        ??????????????????????????????????????????
```

---

## Resources

### Key Files to Study:
- `IQToolkit.Data\EntityProvider.cs` - Base provider implementation
- `IQToolkit.Data\Common\QueryTranslator.cs` - Translation orchestration
- `IQToolkit.Data\Common\Translation\QueryBinder.cs` - LINQ operator binding
- `IQToolkit.Data\Common\Language\QueryLanguage.cs` - Language abstraction
- `IQToolkit.Data\Common\Execution\ExecutionBuilder.cs` - Execution plan building
- Provider examples: `AdvantageQueryProvider.cs`, `MySqlQueryProvider.cs`, etc.

### Blog Series:
The original IQToolkit was documented in Matt Warren's blog series "Building an IQueryable Provider" (Parts I-IX), which explains the evolution from simple to complex provider implementations.

### Additional Documentation:
- See the `docs/blog/` directory for the original blog posts explaining the architecture
- Each provider implementation (Advantage, MySQL, SqlServer, etc.) serves as a reference example

---

## Version Information

This documentation is based on IQToolkit for:
- C# version: 7.3
- .NET target: .NET Standard 1.4 / .NET Framework 4.8
- Last updated: 2024

---

## License

Copyright (c) Microsoft Corporation. All rights reserved.
This source code is made available under the terms of the Microsoft Public License (MS-PL).
