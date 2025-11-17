# Association Filters Feature

## Overview
This feature allows you to apply filters to navigation properties that are enforced at the database level as part of the JOIN condition.

There are TWO ways to apply association filters:

### 1. Programmatic Filter (AdvantageEntityPolicy)

```csharp
var policy = new AdvantageEntityPolicy();

// Filter a singleton association
policy.AssociateWith<LocClt, LocCode>(
    lc => lc.DelaiReglement,
    code => code.TYPE == "REGLTDELAI");

// Include the filtered association in results
policy.IncludeWith<LocClt>(lc => lc.DelaiReglement);

var provider = AdvantageQueryProvider.Create(connectionString, policy);

// Query will only populate DelaiReglement if TYPE == "REGLTDELAI"
var results = provider.GetTable<LocClt>()
    .Where(lc => lc.DelaiReglement != null)
    .ToList();
```

### 2. Attribute-Based Filter (Declarative)

```csharp
public class LocClt
{
    public string REGLTDELAI { get; set; }
    
    [Association(
        KeyMembers = "REGLTDELAI",
        Filter = "TYPE = 'REGLTDELAI'")]
    public LocCode DelaiReglement { get; set; }
}
```

The `Filter` property accepts a raw SQL expression that will be added to the JOIN ON clause.
Column references are automatically prefixed with the related table's alias.

## Generated SQL

Both approaches generate the same SQL with the filter in the JOIN ON condition:

```sql
LEFT OUTER JOIN [LocCode] AS t1
  ON ((t1.[CODIF] = t0.[REGLTDELAI]) AND (t1.[TYPE] = 'REGLTDELAI'))
```

## Key Features

1. **Database-level filtering**: Filter is applied in SQL JOIN, not in memory
2. **Works with IncludeWith**: Compatible with eager loading
3. **Proper null handling**: Navigation property is NULL if filter doesn't match
4. **Type-safe (programmatic)**: Lambda expressions with compile-time checking
5. **Declarative (attribute)**: Simple string-based filters close to the entity definition

## Implementation Details

### Core Changes (Minimal)
- Added `Filter` property to `AssociationAttribute` in `IQToolkit.Data/Mapping/AttributeMapping.cs`
- Added `GetAssociationFilter(entity, member)` method to `AttributeMapping`

### Advantage Provider
- **AdvantageEntityPolicy**: Extends `EntityPolicy` with `AssociateWith<T1,T2>()` methods
- **AdvantageMapper**: Overrides `GetMemberExpression()` to check for:
  1. Programmatic filters from `AdvantageEntityPolicy`
  2. Attribute filters from `[Association]` attribute
- **AdvantageFormatter**: Handles `SqlFilterExpression` to output raw SQL
- **MemberToColumnRewriter**: Converts filter lambdas to SQL column expressions

## Limitations

**Attribute Filter Parsing**: The raw SQL filter string is currently inserted as-is. Future enhancements could:
- Parse the filter to validate column names
- Automatically add table alias prefixes to column references
- Support parameterized values

For now, use simple comparison expressions like:
- `"TYPE = 'VALUE'"`
- `"STATUS = 1"`
- `"ACTIVE = TRUE"`

## Recommendations

- Use **programmatic filters** when:
  - Filter logic needs to be dynamic
  - You want type safety and IntelliSense
  - Filter depends on runtime conditions

- Use **attribute filters** when:
  - Filter is static and known at design time
  - You want the filter close to the entity definition
  - Simple SQL expressions are sufficient
