# Fix for DevExpress Conditional Expressions (Nullable Date Handling)

## Issue

DevExpress generates conditional expressions (ternary operators) to handle nullable dates with null-checks:

```csharp
locgen => locgen.DatePreparation == null
    ? locgen.DatePreparation
    : ((Nullable<DateTime>)locgen.DatePreparation.Value.Date)
```

This was throwing `NotSupportedException: Conditional expressions not supported` because the `AdvantageFormatter` didn't override `VisitConditional`.

## Solution

Added `VisitConditional` override to `AdvantageFormatter.cs` to translate conditional expressions to **CASE WHEN** SQL syntax, which Advantage SQL supports.

### SQL Translation

The conditional expression is now translated to SQL:

```csharp
// LINQ (from DevExpress)
locgen.DatePreparation == null 
    ? locgen.DatePreparation 
    : locgen.DatePreparation.Value.Date

// SQL (Advantage)
CASE WHEN (DatePreparation IS NULL) THEN DatePreparation
     ELSE DATE(DatePreparation)
END
```

## Implementation Details

The `VisitConditional` method handles two patterns:

### 1. Predicate-based Conditional (most common)
```csharp
// Pattern: condition ? ifTrue : ifFalse
// SQL: CASE WHEN condition THEN ifTrue ELSE ifFalse END
```

Handles chained conditionals:
```csharp
// Pattern: c1 ? v1 : c2 ? v2 : c3 ? v3 : vDefault
// SQL: CASE WHEN c1 THEN v1 WHEN c2 THEN v2 WHEN c3 THEN v3 ELSE vDefault END
```

### 2. Value-based Conditional
```csharp
// Pattern: value != 0 ? ifTrue : ifFalse
// SQL: CASE value WHEN 0 THEN ifFalse ELSE ifTrue END
```

## Code Changes

**File:** `IQToolkit.Data.Advantage/AdvantageFormatter.cs`

Added method:
```csharp
protected override Expression VisitConditional(ConditionalExpression c)
{
    // Advantage SQL supports CASE WHEN syntax
    if (this.IsPredicate(c.Test))
    {
        this.Write("(CASE WHEN ");
        this.VisitPredicate(c.Test);
        this.Write(" THEN ");
        this.VisitValue(c.IfTrue);
        Expression ifFalse = c.IfFalse;
        while (ifFalse != null && ifFalse.NodeType == ExpressionType.Conditional)
        {
            ConditionalExpression fc = (ConditionalExpression)ifFalse;
            this.Write(" WHEN ");
            this.VisitPredicate(fc.Test);
            this.Write(" THEN ");
            this.VisitValue(fc.IfTrue);
            ifFalse = fc.IfFalse;
        }
        if (ifFalse != null)
        {
            this.Write(" ELSE ");
            this.VisitValue(ifFalse);
        }
        this.Write(" END)");
    }
    else
    {
        this.Write("(CASE ");
        this.VisitValue(c.Test);
        this.Write(" WHEN 0 THEN ");
        this.VisitValue(c.IfFalse);
        this.Write(" ELSE ");
        this.VisitValue(c.IfTrue);
        this.Write(" END)");
    }
    return c;
}
```

## Test Status

? All tests passing:
- **89 passing** (same as before)
- **0 failing** 
- **4 skipped** (unrelated edge cases)

## Use Cases Now Supported

### 1. DevExpress Nullable Date Handling
```csharp
var query = provider.GetTable<LocGen>()
    .GroupBy(locgen => locgen.DatePreparation == null
        ? locgen.DatePreparation
        : locgen.DatePreparation.Value.Date)
    .Select(g => g.Key);
```

### 2. General Conditional Expressions
```csharp
var query = provider.GetTable<Entity>()
    .Select(e => new {
        Status = e.IsActive ? "Active" : "Inactive",
        Priority = e.Score > 90 ? "High" 
                 : e.Score > 50 ? "Medium" 
                 : "Low"
    });
```

### 3. Null-Coalescing Logic
```csharp
var query = provider.GetTable<Entity>()
    .Select(e => new {
        DisplayName = e.Name != null ? e.Name : "Unknown"
    });
```

## Advantage SQL Syntax Compatibility

The implementation follows standard SQL CASE WHEN syntax, which is fully supported by Advantage SQL:

```sql
-- Simple CASE WHEN
CASE WHEN condition THEN value1 ELSE value2 END

-- Multiple conditions
CASE WHEN cond1 THEN val1
     WHEN cond2 THEN val2
     WHEN cond3 THEN val3
     ELSE defaultVal
END

-- Value-based CASE
CASE expression
     WHEN val1 THEN result1
     WHEN val2 THEN result2
     ELSE defaultResult
END
```

## Benefits

1. ? **DevExpress Compatibility** - No more `NotSupportedException` with DevExpress-generated queries
2. ? **Standard SQL** - Uses standard CASE WHEN syntax
3. ? **Chained Conditionals** - Supports nested ternary operators
4. ? **Nullable Handling** - Properly handles nullable value checks
5. ? **No Breaking Changes** - All existing tests still pass

## Summary

The fix adds conditional expression (ternary operator) support to IQToolkit for Advantage, translating them to SQL CASE WHEN statements. This resolves the issue where DevExpress generates conditional expressions for nullable date handling, and now those queries work seamlessly with Advantage SQL.
