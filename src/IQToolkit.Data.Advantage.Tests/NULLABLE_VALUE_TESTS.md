# Nullable.Value Test Coverage

## Overview
This document describes the automated tests for the `Nullable<T>.Value` property handling in the Advantage LINQ provider. These tests validate that accessing `.Value` on nullable types in LINQ queries is correctly translated to SQL.

## Problem Statement
When using `Nullable<T>.Value` in LINQ queries (especially in Select projections), the expression:
```csharp
DATELOC = locgen.DATELOC.Value
```

Was throwing a `NotSupportedException: "The member access 'System.DateTime Value' is not supported"`

## Solution
The `AdvantageFormatter` was enhanced to handle the `.Value` property on nullable types by simply passing through the underlying expression, since SQL doesn't have a concept of `.Value` - the column itself represents the value.

## Test Location
File: `IQToolkit.Data.Advantage.Tests/NullableTests.cs`

## Test Categories

### 1. Nullable.Value in Select Projection Tests
These tests validate that `.Value` works correctly in SELECT projections followed by various operations.

#### `NullableDateTime_Value_In_Select_With_Count()`
- **Purpose**: Tests `Select(...nullable.Value...).Count()`
- **Scenario**: The original bug scenario where COUNT after SELECT with .Value was failing
- **Expected**: Should count 4 records without errors

#### `NullableDateTime_Value_In_Select_With_ToList()`
- **Purpose**: Tests `Select(...nullable.Value...).ToList()`
- **Scenario**: Validates .Value in projection followed by materialization
- **Expected**: Should return 3 records (with HasValue guard)

#### `NullableDateTime_Value_Multiple_Fields_In_Select_With_Count()`
- **Purpose**: Tests complex projection with multiple fields including nullable.Value
- **Scenario**: Mirrors real-world LocationSummary projection pattern
- **Expected**: Should count 3 records correctly

#### `NullableDateTime_Value_In_Anonymous_Type_With_Take_And_Count()`
- **Purpose**: Tests exact pattern from user's code: `Select(...).Take(10).Count()`
- **Scenario**: The specific pattern that was failing in production code
- **Expected**: Should return count ? 10 without errors

#### `NullableDateTime_Value_With_CompositeField_Pattern()`
- **Purpose**: Tests nullable.Value on composite fields
- **Scenario**: Common pattern with composite date/time fields
- **Expected**: Should return results without errors

#### `NullableDateTime_Value_In_DTO_Projection()`
- **Purpose**: Tests projecting nullable.Value to DTO class
- **Scenario**: Assigning nullable.Value to non-nullable DTO property
- **Expected**: Should return 3 records with proper values

#### `NullableDateTime_Value_First_And_Single()`
- **Purpose**: Tests .Value with First() and Single() operations
- **Scenario**: Common query terminal operations
- **Expected**: Should return correct records

#### `Multiple_NullableDateTime_Value_Fields_In_Projection()`
- **Purpose**: Tests multiple .Value accesses in same projection
- **Scenario**: Accessing same nullable field multiple times
- **Expected**: Should count 3 records correctly

### 2. Nullable.Value.Property Tests
These tests validate that `.Value.Property` (chained property access) works correctly.

#### `NullableDateTime_Value_Year_In_Where()`
- **Purpose**: Tests `nullable.Value.Year` in WHERE clause
- **Scenario**: Filtering by year extracted from nullable date
- **Expected**: Should return 3 records from year 2023

#### `NullableDateTime_Value_Month_In_Where()`
- **Purpose**: Tests `nullable.Value.Month` in WHERE clause
- **Scenario**: Filtering by month from nullable date
- **Expected**: Should return 3 records from January

#### `NullableDateTime_Value_Day_In_Where()`
- **Purpose**: Tests `nullable.Value.Day` in WHERE clause
- **Scenario**: Filtering by day of month
- **Expected**: Should return 2 records with day = 1

#### `NullableDateTime_Value_Property_In_Select()`
- **Purpose**: Tests nullable.Value.Property in SELECT projection
- **Scenario**: Extracting year in projection
- **Expected**: Should return 3 records with Year = 2023

#### `NullableDateTime_Value_Property_In_OrderBy()`
- **Purpose**: Tests nullable.Value.Property in ORDER BY clause
- **Scenario**: Sorting by day of month
- **Expected**: Should return results ordered by day

### 3. Nullable with HasValue Guard Tests
These tests validate the recommended pattern of checking HasValue before accessing Value.

#### `NullableDateTime_HasValue_Guard_Before_Value_Access()`
- **Purpose**: Tests proper null-safe pattern
- **Scenario**: `HasValue && .Value.Property` pattern
- **Expected**: Should return 3 non-null records

#### `NullableDateTime_Count_With_HasValue()`
- **Purpose**: Tests COUNT with HasValue guard
- **Scenario**: Counting records with null-safe check
- **Expected**: Should count 3 records

### 4. GetValueOrDefault Tests
These tests validate the alternative nullable access pattern.

#### `NullableDateTime_GetValueOrDefault_Pattern()`
- **Purpose**: Tests GetValueOrDefault() method
- **Scenario**: Safe access to nullable with default fallback
- **Expected**: Should return 4 records, null record gets year 1

## Test Data
The tests use `TestEntity` which has:
- 4 total records
- 3 records with non-null DateCol (all from 2023-01-01 or 2023-01-02)
- 1 record with null DateCol (Id = 4)

## SQL Translation
When a nullable.Value is encountered in LINQ:
```csharp
locgen.DATELOC.Value
```

Is translated to SQL as:
```sql
t0.[DATELOC]
```

The `.Value` is simply removed since SQL columns directly represent values, whether nullable or not.

## Related Files
- `IQToolkit.Data.Advantage/AdvantageFormatter.cs` - Contains the fix
- `Test.Advantage.Core/Program.cs` - Contains the original failing scenario
- `IQToolkit.Data.Advantage.Tests/TestEntity.cs` - Test entity definition
- `IQToolkit.Data.Advantage.Tests/TestSetup.cs` - Test database setup

## Running the Tests
```bash
# Run all nullable tests
dotnet test --filter "FullyQualifiedName~NullableTests"

# Run specific test
dotnet test --filter "FullyQualifiedName~NullableDateTime_Value_In_Select_With_Count"
```

## Success Criteria
All tests should pass, indicating:
1. ? `.Value` on nullable types doesn't throw NotSupportedException
2. ? SELECT with `.Value` followed by Count() works
3. ? SELECT with `.Value` followed by ToList() works
4. ? `.Value.Property` chaining works in WHERE, SELECT, ORDER BY
5. ? Multiple `.Value` accesses in same query work
6. ? `.Value` works with composite fields
7. ? `.Value` works in DTO projections
8. ? HasValue guard pattern works correctly
9. ? GetValueOrDefault() pattern works correctly

## Known Limitations
- Accessing `.Value` on a NULL column will result in SQL NULL handling (NULL comparisons return false)
- In-memory LINQ to Objects would throw `InvalidOperationException` for `.Value` on null
- Best practice is to use `HasValue` guard or `GetValueOrDefault()` when null values are possible

## Future Enhancements
- Add tests for other nullable value types (int?, decimal?, etc.)
- Add tests for nullable.Value in GroupBy keys
- Add tests for nullable.Value in JOIN conditions
- Add performance benchmarks for nullable handling
