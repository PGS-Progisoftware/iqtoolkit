# Composite Field Feature for Advantage Provider

## Overview

The Composite Field feature allows you to query combined DATE and TIME columns as a single `DateTime` property in your LINQ queries. This is particularly useful for DBF databases (like Advantage Database) that store date and time separately.

## Problem

In DBF tables, datetime information is often split into two columns:
- A `DATE` column (e.g., `DATEDEP`)
- A `CHAR(5)` column in "HH:MM" format (e.g., `HEUREDEP`)

Without this feature, you cannot easily query these as a combined datetime value.

## Solution

The `CompositeFieldAttribute` marks a property as a composite field and specifies which backing fields contain the date and time parts.

## Usage

### 1. Mark Your Composite Fields

```csharp
using IQToolkit.Data.Advantage;

public class LocGen
{
    // Backing fields (actual database columns)
    public DateTime DATEDEP { get; set; }      // DATE column
    
    [Column(DbType = "CHAR(5)")]
    public string HEUREDEP { get; set; }   // TIME column as "HH:MM"
    
    public DateTime DATEMAJ { get; set; }
    
    [Column(DbType = "CHAR(5)")]
    public string HEUREMAJ { get; set; }
    
    // Composite fields (virtual - not in database)
    [CompositeField(DateMember = nameof(DATEDEP), TimeMember = nameof(HEUREDEP))]
    public DateTime DTDEP { get; set; }
    
    [CompositeField(DateMember = nameof(DATEMAJ), TimeMember = nameof(HEUREMAJ))]
    public DateTime DTMAJ { get; set; }
}
```

### 2. Query Using the Composite Field

```csharp
var provider = new AdvantageQueryProvider(connectionString);
var cutoffDate = new DateTime(2024, 6, 28, 13, 33, 0);

// Query using the composite field
var query = provider.GetTable<LocGen>()
    .Where(lg => lg.DTDEP > cutoffDate)
    .ToList();
```

### 3. Generated SQL

The provider automatically translates the composite field comparison into proper SQL:

```sql
SELECT * FROM LOCGEN t0
WHERE (t0.[DATEDEP] > :p0) 
   OR (t0.[DATEDEP] = :p0 AND t0.[HEUREDEP] > :p1)
```

With parameters:
- `:p0` = `'2024-06-28'`
- `:p1` = `'13:33'`

## Supported Operators

All comparison operators are supported:

### Equal (==)
```csharp
.Where(lg => lg.DTDEP == someDate)
```
Translates to:
```sql
(DATEDEP = :date AND HEUREDEP = :time)
```

### Not Equal (!=)
```csharp
.Where(lg => lg.DTDEP != someDate)
```
Translates to:
```sql
(DATEDEP != :date OR HEUREDEP != :time)
```

### Greater Than (>)
```csharp
.Where(lg => lg.DTDEP > someDate)
```
Translates to:
```sql
(DATEDEP > :date) OR (DATEDEP = :date AND HEUREDEP > :time)
```

### Greater Than or Equal (>=)
```csharp
.Where(lg => lg.DTDEP >= someDate)
```
Translates to:
```sql
(DATEDEP > :date) OR (DATEDEP = :date AND HEUREDEP >= :time)
```

### Less Than (<)
```csharp
.Where(lg => lg.DTDEP < someDate)
```
Translates to:
```sql
(DATEDEP < :date) OR (DATEDEP = :date AND HEUREDEP < :time)
```

### Less Than or Equal (<=)
```csharp
.Where(lg => lg.DTDEP <= someDate)
```
Translates to:
```sql
(DATEDEP < :date) OR (DATEDEP = :date AND HEUREDEP <= :time)
```

## How It Works

1. **Attribute Detection**: The `CompositeFieldAttribute` marks a property as composite and identifies the backing fields.

2. **Expression Rewriting**: The `AdvantageCompositeFieldRewriter` intercepts LINQ expressions before SQL generation.

3. **SQL Generation**: Composite field comparisons are expanded into proper SQL conditions that compare both the date and time parts.

4. **Automatic Integration**: The rewriting happens automatically in `AdvantageQueryProvider.Execute()` and `GetQueryText()`.

## Requirements

- The date member must be of type `DateTime`
- The time member must be of type `string` in "HH:MM" format
- Both backing fields must exist on the entity class
- The composite field itself does not need to be mapped to a database column

## Benefits

? **Type-safe**: Use strongly-typed DateTime comparisons in your LINQ queries

? **Automatic**: No manual SQL writing or special query syntax required

? **Correct logic**: Handles the complex date+time comparison logic automatically

? **All operators**: Supports all standard comparison operators (==, !=, <, <=, >, >=)

? **Provider-specific**: Isolated to the Advantage provider, doesn't affect other IQToolkit providers

## Example Test Code

```csharp
static void TestCompositeField(AdvantageQueryProvider provider)
{
    var cutoffDate = new DateTime(2024, 6, 28, 13, 33, 0);
    
    // Test greater than
var query = provider.GetTable<LocGen>()
        .Where(lg => lg.DTDEP > cutoffDate)
 .Take(10);
    
    Console.WriteLine("Generated SQL:");
    Console.WriteLine(provider.GetQueryText(query.Expression));
    
    var results = query.ToList();
    Console.WriteLine($"Found {results.Count} records");
}
```

## Implementation Files

- `CompositeFieldAttribute.cs` - The attribute definition
- `AdvantageCompositeFieldRewriter.cs` - The expression rewriter
- `AdvantageQueryProvider.cs` - Integration into the provider (Execute and GetQueryText methods)

## Notes

- The composite property getter/setter are not used for data retrieval - they're only markers for LINQ queries
- You can still access the individual backing fields (DATEDEP, HEUREDEP) directly if needed
- The rewriter only affects LINQ queries; it doesn't change how data is loaded from the database
