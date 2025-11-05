# Debugging Composite Fields Not Working

## Symptoms
When you run queries with composite fields like `lg.DTDEP > cutoffDate`, they don't work as expected.

## Possible Causes and Solutions

### 1. **Composite fields are being projected in SELECT**

**Problem**: If you try to SELECT a composite field, the mapper will fail because DTDEP doesn't exist in the database.

**Solution**: Only use composite fields in WHERE clauses, not in SELECT:

```csharp
// ? WRONG - Don't select composite fields
var query = provider.GetTable<LocGen>()
    .Where(lg => lg.DTDEP > cutoffDate)
    .Select(lg => new { lg.NUMLOC, lg.DTDEP }); // DTDEP will fail!

// ? CORRECT - Only use in WHERE
var query = provider.GetTable<LocGen>()
  .Where(lg => lg.DTDEP > cutoffDate)
    .Select(lg => new { lg.NUMLOC, lg.DATEDEP, lg.HEUREDEP });
```

### 2. **The rewriter isn't being called**

**Check**: Add debug output to `AdvantageQueryProvider`:

```csharp
public override string GetQueryText(Expression expression)
{
    Console.WriteLine("BEFORE REWRITE:");
    Console.WriteLine(expression.ToString());
    
   expression = AdvantageCompositeFieldRewriter.Rewrite(expression);
    
    Console.WriteLine("AFTER REWRITE:");
    Console.WriteLine(expression.ToString());
    
    return base.GetQueryText(expression);
}
```

### 3. **AttributeMapping is trying to map composite fields**

**Problem**: The default mapping might try to infer a column for DTDEP.

**Solution**: Ensure composite properties have NO `[Column]` attribute:

```csharp
// ? CORRECT
[CompositeField(DateMember = nameof(DATEDEP), TimeMember = nameof(HEUREDEP))]
public DateTime DTDEP { get; set; }  // NO [Column] attribute!
```

### 4. **The expression tree is more complex than expected**

**Problem**: LINQ creates complex expression trees with method calls, not just binary comparisons.

**Test**: Try this simpler query first:

```csharp
// Simple test - create the lambda manually
Expression<Func<LocGen, bool>> predicate = lg => lg.DTDEP > cutoffDate;
var table = provider.GetTable<LocGen>();
var query = table.Where(predicate);

Console.WriteLine("SQL: " + provider.GetQueryText(query.Expression));
```

### 5. **The rewriter needs to handle MethodCallExpression**

The `Where` method creates a `MethodCallExpression`, not just a `BinaryExpression`. The rewriter needs to traverse into the lambda body.

**Check the visitor**: Make sure `VisitMethodCall` and `VisitLambda` are inherited from `DbExpressionVisitor` and working correctly.

## Debug Steps

1. **Add Console.WriteLine to the rewriter**:

```csharp
protected override Expression VisitBinary(BinaryExpression node)
{
    Console.WriteLine($"Visiting binary: {node.NodeType} - {node}");
    
    if (left is MemberExpression leftMember)
    {
        Console.WriteLine($"  Left member: {leftMember.Member.Name}");
        if (IsCompositeField(leftMember.Member, out var df, out var tf))
   {
     Console.WriteLine($"  IS COMPOSITE! Date={df}, Time={tf}");
        }
    }
    
    // ... rest of code
}
```

2. **Check if the attribute is found**:

```csharp
private static bool IsCompositeField(MemberInfo member, out string dateField, out string timeField)
{
    Console.WriteLine($"Checking if '{member.Name}' is composite field...");
    var attrs = member.GetCustomAttributes(typeof(CompositeFieldAttribute), inherit: false);
    Console.WriteLine($"  Found {attrs.Length} CompositeField attributes");
    
 // ... rest of code
}
```

3. **Test the query text first** (before executing):

```csharp
var query = provider.GetTable<LocGen>()
    .Where(lg => lg.DTDEP > cutoffDate)
.Take(5);

// Don't execute yet - just get the SQL
string sql = provider.GetQueryText(query.Expression);
Console.WriteLine("Generated SQL:");
Console.WriteLine(sql);

// Check if SQL has the rewritten condition
if (sql.Contains("DATEDEP") && sql.Contains("HEUREDEP"))
{
    Console.WriteLine("? Rewriting worked!");
}
else if (sql.Contains("DTDEP"))
{
    Console.WriteLine("? Rewriting FAILED - DTDEP is still in SQL");
}
```

## Expected vs Actual

### What SHOULD happen:

**Input Query**:
```csharp
.Where(lg => lg.DTDEP > cutoffDate)
```

**Output SQL**:
```sql
WHERE (t0.[DATEDEP] > :p0) OR (t0.[DATEDEP] = :p0 AND t0.[HEUREDEP] > :p1)
```

### What might be happening instead:

**Option A**: SQL contains `DTDEP` (rewriter didn't run):
```sql
WHERE t0.[DTDEP] > :p0  -- ERROR! DTDEP doesn't exist
```

**Option B**: Exception during execution (composite field being projected):
```
Column 'DTDEP' does not exist in table
```

## Quick Test

Add this to your Main method to isolate the problem:

```csharp
static void QuickCompositeTest(AdvantageQueryProvider provider)
{
    Console.WriteLine("=== QUICK COMPOSITE TEST ===\n");
 
    try
    {
        var cutoffDate = new DateTime(2024, 6, 28, 13, 33, 0);
     
        // Build query
        var query = provider.GetTable<LocGen>()
 .Where(lg => lg.DTDEP > cutoffDate)
  .Select(lg => lg.NUMLOC) // Only select real column
            .Take(1);
        
     // Get SQL (don't execute yet)
        Console.WriteLine("Attempting to get SQL...");
  string sql = provider.GetQueryText(query.Expression);
        
        Console.WriteLine("\nGenerated SQL:");
        Console.WriteLine(sql);
        Console.WriteLine();
        
    // Check result
        if (sql.Contains("DTDEP"))
    {
            Console.WriteLine("? PROBLEM: SQL still contains DTDEP");
          Console.WriteLine("   The rewriter is not working or not being called");
        }
     else if (sql.Contains("DATEDEP") && sql.Contains("HEUREDEP"))
  {
     Console.WriteLine("? SUCCESS: SQL was rewritten correctly!");
            Console.WriteLine("   Now trying to execute...");
            
     var result = query.ToList();
    Console.WriteLine($"   Execution successful! Found {result.Count} results");
        }
        else
        {
     Console.WriteLine("? UNEXPECTED: SQL doesn't contain expected columns");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n? EXCEPTION: {ex.Message}");
        Console.WriteLine($"Type: {ex.GetType().Name}");
    if (ex.InnerException != null)
        {
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
        }
    }
    
    Console.WriteLine("\n=== END TEST ===\n");
}
```

Run this test and share the output - it will tell us exactly where the problem is.
