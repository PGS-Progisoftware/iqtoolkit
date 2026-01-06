# Issue Tracking: GroupBy Query Fix

## Problem Description
The user reports that a LINQ query using `GroupBy` and `Select(Key)` returns all rows instead of distinct keys.

### Query 1 (Correct)
```csharp
provider.GetTable<LocClt>()
    .Select(x => x.Secteur.Libelle)
    .Distinct();
```

### Query 2 (Incorrect)
```csharp
provider.GetTable<PCSLib.Data.DBF.LocClt>()
    .Select(locclt => new LocCltProjection
    {
        CodeClient = locclt.IdTiers,
        SecteurCode = locclt.CodeSecteur,
        SecteurLibelle = locclt.Secteur.Libelle,
    })
    .GroupBy(p => p.SecteurLibelle)
    .Select(g => g.Key);
```

## Investigation Plan
1.  Analyze `IQToolkit.Data.Advantage` project structure.
2.  Examine `AdvantageQueryProvider` and `AdvantageLanguage` (or similar translator class).
3.  Look for how `GroupBy` is handled in the expression tree visitor/translator.
4.  Identify why the projection before `GroupBy` might be causing issues.

## Progress
- [x] Created tracking file.
- [x] Analyzed `IQToolkit.Data.Advantage` project.
- [x] Identified `MemoColumnPruner` in `AdvantageLanguage.cs` as the likely cause.
- [x] Verified that `MemoColumnPruner` removes columns from `GroupBy` if they are identified as Memo.
- [x] Applied fix to disable `GroupBy` pruning in `MemoColumnPruner`.
- [x] Verified build of `IQToolkit.Data.Advantage`.

## Investigation Findings

### Current Implementation Analysis

1. **NavigationPropertyRewriter** (AdvantageMapping.cs:374-719):
   - Rewrites `Select(x => x.Nav.Prop)` to `SelectMany` with explicit joins
   - This happens BEFORE base.Translate() in AdvantageMapper.Translate()

2. **QueryBinder.BindGroupBy** (QueryBinder.cs:522-576):
   - When GroupBy is applied, it uses `ProjectColumns` to create columns for the GROUP BY expression
   - The key expression is visited with the projection's projector mapped to the keySelector parameter
   - This creates ColumnExpressions that reference the projection's alias

3. **GroupByColumnFixer** (AdvantageMapping.cs:802-882):
   - Attempts to fix GROUP BY expressions that reference columns from projections
   - Only handles case where FROM is a ProjectionExpression
   - Tries to find matching column in FROM projection and update alias reference

### Problem Hypothesis

When projecting a relation (`locclt.Secteur.Libelle`), the flow is:
1. NavigationPropertyRewriter rewrites to SelectMany with JOIN
2. This creates a projection with columns from both tables
3. GroupBy is applied: `GroupBy(p => p.SecteurLibelle)`
4. QueryBinder.ProjectColumns creates a ColumnExpression for `p.SecteurLibelle` that references the projection's alias
5. The GROUP BY clause uses this ColumnExpression, but it should reference the underlying joined table's column directly

**Issue**: The GROUP BY might be referencing a column from the intermediate projection instead of the actual joined table column, causing incorrect SQL that doesn't properly group.

### Next Steps
- [ ] Add logging to see actual SQL generated for Query 2
- [ ] Trace through expression tree to see what ColumnExpressions are created
- [ ] Verify if GroupByColumnFixer is being called and what it's doing
- [ ] Check if the issue is in how ProjectColumns handles member access on projections

## Fix Attempt 1

**Problem Identified**: When GroupBy is applied after a projection with navigation properties:
1. `QueryBinder.BindGroupBy` calls `ProjectColumns(keyExpr, projection.Select.Alias, projection.Select.Alias)`
2. This creates ColumnExpressions that reference the projection's SELECT alias
3. The GROUP BY expressions are then used in a new SELECT with the FROM being `projection.Select`
4. The issue: GROUP BY should reference the actual underlying column from joined tables, not the intermediate projection column

**Root Cause**: When `ProjectColumns` processes the key expression (e.g., `p.SecteurLibelle`), it creates a ColumnExpression that references the projection's alias. However, the actual column comes from a joined table, and the GROUP BY should reference that table's column directly.

**Fix Applied**: Simplified `GroupByColumnFixer.FixGroupByExpression` to:
- Check if GROUP BY column references the FROM projection's SELECT alias
- Find the corresponding column in the FROM projection's SELECT
- Use the expression from that column directly (which references the actual joined table column)
- This ensures GROUP BY references the correct underlying column instead of the intermediate projection column

**Status**: Fix implemented, needs testing to verify it resolves the issue

### Implementation Details

The fix is in `GroupByColumnFixer.FixGroupByExpression` method:
- When a GROUP BY expression is a ColumnExpression that references the FROM projection's SELECT alias
- We find the corresponding column in the FROM projection's SELECT columns
- We return the expression from that column directly (which references the actual joined table column)
- This ensures the GROUP BY clause in SQL references the correct table column, not the intermediate projection column

**Files Modified**:
- `src/IQToolkit.Data.Advantage/AdvantageMapping.cs`: Enhanced `GroupByColumnFixer.FixGroupByExpression` method

**Next Steps**:
- Test Query 2 to verify it now returns distinct keys instead of all rows
- Check the generated SQL to ensure GROUP BY references the correct table columns
- Verify that Query 1 still works correctly
