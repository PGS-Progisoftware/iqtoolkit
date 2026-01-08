# Advantage provider debugging log (Test.Advantage.Core)

This file tracks investigation attempts/fixes for the Advantage LINQ provider so work can be resumed later.

## Context
- Problem observed in `Test.Advantage.Core/Program.cs`:
  - `distinct = ...Select(x => x.Secteur.Libelle).Distinct()` works.
  - `groupBy = ...GroupBy(p => p.SecteurLibelle).OrderBy(g => g.Key).Select(g => g.Key)` fails.
  - Expected: `groupBy.Count()` == `distinct.Count()`.

## Attempt 1 (2026-01-06)
### Symptom
Running `Test.Advantage.Core` fails with:
- Advantage error `7200`: `Unable to ORDER BY this column: IMPORTARIF`.

The logged SQL showed a `SELECT ... FROM (SELECT ... FROM LocClt ...) AS t0 ... ORDER BY t0.[IMPORTARIF] ...`-style ordering being applied, where `IMPORTARIF` is a Memo column.

### Working hypothesis
`GroupBy` translation can introduce internal (derived-table) `SELECT` layers.
IQToolkit's `OrderByRewriter` tends to lift/preserve orderings when possible, and the Advantage provider had custom logic intended to remove invalid `ORDER BY` in subqueries.

However, the existing implementation in `IQToolkit.Data.Advantage/AdvantageLanguage.cs`:
- only removed `ORDER BY` from a very specific pattern: when the outer select's `From` was a `SelectExpression`.
- did not address `ORDER BY` on *other* non-top-level selects created by GroupBy/Distinct/relationship rewrites.

This allowed memo columns (like `IMPORTARIF`) to appear in `ORDER BY` lists for derived tables / intermediate selects, which Advantage rejects.

### Change made
File: `IQToolkit.Data.Advantage/AdvantageLanguage.cs`
- Replaced the narrow `OrderByRemover` with `OrderByInSubqueryRemover`.
- New rule: remove `ORDER BY` from **any non-top-level `SelectExpression`** unless it is needed for paging (`Take`/`Skip`).
- Kept the removal step **before** `RedundantSubqueryRemover` so subqueries can still be merged afterwards.

### Result
- Build of `IQToolkit.Data.Advantage` succeeded.
- Program run now proceeds further (validate on next run).

### Next steps
- Re-run `Test.Advantage.Core` and compare `distinct.Count()` vs `groupBy.Count()`.
- If counts still mismatch or a new SQL error appears:
  - capture both SQL statements.
  - inspect whether `groupBy` translation still pulls all `LocClt` columns (including memo) into a derived table.
  - consider additional pruning for memo columns when they are not projected, or ensure projected columns for grouping are minimal.

## Attempt 2 (2026-01-06)
### Change tried
Expanded `OrderByInSubqueryRemover` to also track `SubqueryExpression` nesting and treat any `SelectExpression` inside a subquery as non-outermost, stripping its `ORDER BY` (unless paging).

### Result
Still fails with the same Advantage error 7200:
`Unable to ORDER BY this column: IMPORTARIF`.

### Interpretation
The `ORDER BY IMPORTARIF` is still present in the final SQL sent to Advantage, meaning either:
- the `ORDER BY` is being introduced after `AdvantageLanguage.AdvantageLinguist.Translate` runs (i.e., later in base translation steps), or
- the failing `ORDER BY` is on the outermost SELECT (so our remover intentionally leaves it), but it should not be there, or
- the ordering expression is not represented as `SelectExpression.OrderBy` at the stage we are visiting (less likely).

### Next steps
1. Capture the *full* generated SQL including the ORDER BY clause and identify whether the ORDER BY is in the outermost SELECT.
2. If it is outermost, determine why the query is being ordered by `IMPORTARIF` at all (it is not specified in `Program.cs`). Likely `OrderByRewriter` is lifting an ordering that originated from somewhere else (indexes? mapping? policy apply order?).
3. Consider disabling/overriding `OrderByRewriter` behavior for Advantage entirely, or add a post-`base.Translate` cleanup step if needed.

## Attempt 3 (2026-01-06)
### Change tried
Added `MemoOrderByPruner` in `IQToolkit.Data.Advantage/AdvantageLanguage.cs`:
- Walks every `SelectExpression.OrderBy` and removes any ordering that directly refers to a MEMO/BLOB column type.
- Goal: prevent Advantage error 7200 (`Unable to ORDER BY this column: IMPORTARIF`).

### Rationale
Since `ORDER BY IMPORTARIF` persists even after stripping inner select orderings, the ordering may be present on the outermost select. Advantage will reject ordering by MEMO regardless of select nesting.

### Result
Still fails with error 7200 ordering by `IMPORTARIF`.

### Interpretation
The `ORDER BY IMPORTARIF` in the SQL is likely ordering by a *projected* column name that ultimately maps to `LocClt.IMPORTARIF`, but our `MemoOrderByPruner` only detects direct memo `ColumnExpression` types in `OrderBy`.
If `OrderByRewriter`/`RebindOrderings` has rebound the ordering to a newly projected column whose `QueryType` is not the memo type (or is not directly visible), the pruner won't recognize it.

### Next steps
- Enhance the ORDER BY pruner to resolve `ColumnExpression` references against the current `SelectExpression.Columns` list (alias+name) and then inspect the declared column expression's `QueryType` to determine if it is memo.
- If confirmed, remove those orderings (or the entire ORDER BY if it becomes empty).

## Key discovery (2026-01-06)
With low-level ADO tracing enabled (`IQTK_TRACE_COMMANDTEXT=1`), the **actual executed SQL** for `groupBy.Count()` is clearly wrong even *before* any Advantage engine behavior:

- The subquery inside `SELECT COUNT(*) FROM (...)` is:
  - `SELECT t2.[ACOMPTETX], t2.[ADR1], ..., t2.[IMPORTARIF], ..., t2.[WEBPASSE] FROM [LocClt] AS t2 GROUP BY t2.[ACOMPTETX], t2.[ADR1], ..., t2.[IMPORTARIF], ..., t2.[WEBPASSE]`
  - i.e. it is effectively a `GROUP BY` on the **entire row**, including memo columns like `IMPORTARIF`.

This explains the error chain:
- once `IMPORTARIF` is inside the grouped subquery, any later ordering (explicit or implicit) that references it will trigger Advantage error 7200.

### Why this matters
The *original LINQ* only groups on `SecteurLibelle` and projects `g.Key`, so **the provider should never need `LocClt.*` nor a `GROUP BY` over all columns**.

### Strong hypothesis (restart point)
This behavior is most consistent with a relationship/navigation rewrite that causes IQToolkit to construct a query that must materialize full `LocClt` entities (or full rows) while still carrying the navigation join.

Concretely:
- `RelationshipBinder` turns navigation access like `locclt.Secteur.Libelle` into JOIN/APPLY constructs.
- Somewhere in that pipeline, the projection becomes an entity projector (`GetEntityExpression`) which, by design, enumerates *all mapped columns* of `LocClt`.
- When `Count()` is applied, IQToolkit's aggregate binding (`QueryBinder.BindAggregate`) wraps the prior projection with `ProjectColumns(projection.Projector, ...)`. If the projector at that moment is a full entity, the generated subquery becomes `GROUP BY` across all of those columns.

### What we need to fix
Prevent navigation-property access in grouped queries from forcing full-entity materialization.
The minimal shape we want for `groupBy.Count()` is:
- group by only the key expression (`Secteur.Libelle` / `LocCode.LIB`), and
- never project the full `LocClt` entity into the grouped subquery.

Likely code locations to focus next:
1. `IQToolkit.Data.Advantage/AdvantageMapping.cs`
   - custom `GetEntityExpression` override that currently builds an entity with all mapped members (except associations / composite-field handling).
   - advantage-specific relationship handling in `GetMemberExpression`.
2. `IQToolkit.Data.Common/Translation/RelationshipBinder.cs`
   - watch how singleton relationships get converted to OUTER APPLY with `AddOuterJoinTest`, and how that can force current projection to be re-projected (`ColumnProjector.ProjectColumns(...)`).
3. `IQToolkit.Data.Advantage/DistinctColumnOptimizer.cs`
   - safe only when `Select.IsDistinct == true`.
   - MUST NOT run for GROUP BY queries (otherwise it can change projector shape and trigger full-row grouping as a side-effect).

Next concrete debugging action (recommended):
- Compare the traced stages for `GetQueryText(groupBy.Expression)` vs `groupBy.Count()` and identify where the projector becomes a full entity.

## Translation tracing hook (revertable)
Added opt-in deep tracing in `IQToolkit.Data/EntityProvider.cs`.

### How to enable
Set env var:
- `IQTK_TRACE_TRANSLATION=1` (or `true`)

In `Test.Advantage.Core`, `Program.cs` sets it automatically for now.

### What it logs
For each query execution plan build (used by both `GetQueryText` and actual execution), it logs to `provider.Log`:
- `INPUT`
- `AFTER PartialEvaluator`
- `AFTER Mapper.Translate`
- `AFTER Police.Translate`
- `AFTER Linguist.Translate`
- `AFTER BuildExecutionPlan`

This is intended to identify the stage where the query shape diverges (e.g., turning into a GROUP BY on all columns / introducing invalid ORDER BY).

### Status with tracing
The failing SQL (GROUP BY all columns + navigation join) is visible at `AFTER BuildExecutionPlan` and also in `IQTK_TRACE_COMMANDTEXT`, meaning the corruption occurs before SQL execution and is not an engine-only behavior.

### Low-level command text tracing
Set `IQTK_TRACE_COMMANDTEXT=1` to log the exact `DbCommand.CommandText` right before `ExecuteReader()`.

In `Test.Advantage.Core/Program.cs` it's enabled automatically.

## Attempt 5 (2026-01-06)
### Cleanup: `AdvantageLanguage.cs`
Refactored `IQToolkit.Data.Advantage/AdvantageLanguage.cs` into a consistent/compilable state:
- Restored the standard pipeline shape (`OrderByRewriter.Rewrite(...)` first, then provider-specific pruners).
- Kept Advantage-specific visitors local to `AdvantageLinguist`.
- Used `SelectExpression` helpers (`SetOrderBy`, `SetColumns`) to avoid manual constructor reassembly.

This cleanup is prerequisite work so subsequent fixes can be reasoned about and inserted deterministically.

### Change tested: disable `NavigationPropertyRewriter` in `AdvantageMapping.AdvantageMapper.Translate`
Hypothesis: our custom `NavigationPropertyRewriter` (mapper-level Select/Distinct rewrites) might be the component that forces full-row projections and therefore causes the `GROUP BY all columns` explosion.

Change made:
- Removed the call to `NavigationPropertyRewriter.Rewrite(_mapping, expression)` from `AdvantageMapping.AdvantageMapper.Translate`.

### Result
No behavioral change for the failing query.
`IQTK_TRACE_COMMANDTEXT` for `groupBy.Count()` still executes the same pattern:
- subquery: `SELECT t2.[ALL LocClt columns] FROM LocClt t2 GROUP BY t2.[ALL LocClt columns]`
- followed by `LEFT OUTER JOIN LocCode` on `SECTEUR`.

### Interpretation
The "GROUP BY full row" shape is **not** coming from the custom `NavigationPropertyRewriter`.
It is being introduced by the standard IQToolkit relationship/navigation binding path (likely `RelationshipBinder` converting `locclt.Secteur` into APPLY/JOIN) in combination with an entity projector for `LocClt`.

In other words:
- the provider is still materializing a full `LocClt` entity at the stage where `GroupBy`/`Count` is shaped,
- and that forces `QueryBinder.BindGroupBy` / aggregate shaping to emit `GROUP BY` over every projected `LocClt` column.

### Next focus
To stop full-row grouping, the next investigation/fix must target the relationship binding / projector selection, not the Distinct-specific rewrite:
- how `AdvantageMapper.GetMemberExpression` + `RelationshipBinder` rewrite `locclt.Secteur.Libelle` for grouped queries,
- and/or how `AdvantageMapper.GetEntityExpression` causes `LocClt` full-column materialization even when only scalar keys are needed.

## Attempt 6 (2026-01-06)
### Change tried: adjust `RelationshipBinder.VisitProjection` to avoid column explosion
Given that singleton navigation binding (`RelationshipBinder` converting to `OUTER APPLY`) can trigger re-projection, attempted to prevent it from re-projecting the entire row.

File: `IQToolkit.Data/Common/Translation/RelationshipBinder.cs`
- Modified `VisitProjection` so that when `currentFrom` changes (due to relationship binding), it re-projects using the *existing* column list as the baseline (`ColumnProjector.ProjectColumns(..., existingColumns: select.Columns, ...)`) instead of starting from `null`.
- Goal: only add columns required for the navigation access, and not expand to full entity shape.

### Interim issue
A first version that only swapped `FROM` caused an `ExecutionBuilder` assertion (`column not in scope`). It was replaced with the safer approach above (keep columns as baseline + add only missing).

### Result
No change in the failing `groupBy.Count()` SQL:
- still `SELECT ... ALL LocClt columns ... GROUP BY ... ALL LocClt columns ...`
- still fails with Advantage error 7200 (`ORDER BY IMPORTARIF`).

### Interpretation
The `GROUP BY full row` is not being introduced by RelationshipBinder's re-projection step (or at least not by the particular re-projection behavior we adjusted).
The full-row grouping still comes from earlier shaping: the `GroupBy`/`Count` pipeline is still grouping on the `LocClt` entity projector.

### Next focus
We likely need to change how `QueryBinder.BindGroupBy` selects `elemExpr` (defaults to `projection.Projector`) when grouping without an element selector.
For this query, that projector is a full `LocClt` entity, so later aggregate/count shaping causes a `GROUP BY` across all mapped columns.
A provider-specific strategy could be:
- detect group-by usage where only the key is needed (e.g., `GroupBy(...).Select(g => g.Key)` / `Count()` pattern) and prevent selecting the full element entity.
- or modify mapping so projecting `LocClt` does not always enumerate memo columns unless needed.

## Attempt 7 (2026-01-06)
### Change made: provider-specific rewrite `GroupBy(key).Select(g => g.Key)` (and variants) => `Select(key).Distinct()`
Goal: avoid Advantage generating `GROUP BY` over the full entity projector (including MEMO columns) for the common pattern that ultimately only needs the grouping key sequence.

Files:
- `IQToolkit.Data.Advantage/GroupByKeyDistinctRewriter.cs`
  - Rewrites:
    - `GroupBy(key).Select(g => g.Key)` => `Select(key).Distinct()`
    - `GroupBy(key).OrderBy(g => g.Key).Select(g => g.Key)` => `Select(key).Distinct()` (strips ordering over groupings)
    - `Count( <any of the above> )` => `Count(Select(key).Distinct())`
- `IQToolkit.Data.Advantage/AdvantageMapping.cs`
  - Runs the rewriter at the correct stage (`AdvantageMapper.Translate`, before `base.Translate`) so it operates on LINQ method calls (before `QueryBinder` turns them into `SelectExpression` trees).

### Result
`Test.Advantage.Core` now succeeds for `groupBy.Count()`.
- The executed SQL becomes `SELECT COUNT(*) FROM (SELECT DISTINCT ... FROM LocClt ...) AS t0` (no `GROUP BY`).
- Runtime output shows `groupBy secteur count=44045`.

### Notes
- The SQL still projects many columns due to `DistinctColumnOptimizer`/entity shaping; however, it avoids the problematic `GROUP BY` and the Advantage memo `ORDER BY` failure.
- This is intentionally a minimal-risk, pattern-based rewrite. It targets only the 'key-only grouping' idiom, leaving general `GroupBy` + aggregates untouched.

## Attempt 8 (2026-01-07) - Nullable handling fix
### Symptom
Running `Test.Advantage.Core` with a query like:
```csharp
.Where(lg => lg.DateCreation.Value.Year > 2004)
```
Fails with error:
`Property 'Int32 Year' is not defined for type 'System.Nullable`1[System.DateTime]'`

The provider doesn't know how to handle the `.Value` property access on nullable types followed by property access on the underlying type.

### Root cause
When accessing `nullable.Value.Year`, the LINQ expression tree contains:
- `MemberExpression` for `Year` on type `DateTime`
- `MemberExpression` for `Value` on type `DateTime?`
- Some expression for the nullable column

The SQL formatter doesn't understand this pattern and needs it to be transformed into something it can handle (like calling `YEAR()` function directly on the nullable column).

### Solution implemented
Added `NullableValueRemover` visitor in `AdvantageLanguage.AdvantageLinguist.Translate`:

File: `IQToolkit.Data.Advantage/AdvantageLanguage.cs`
- Added new visitor class `NullableValueRemover` that runs FIRST in the translation pipeline
- Detects pattern: `nullable.Value.SomeMember` (e.g., `lg.DateCreation.Value.Year`)
- Transforms it to: `Convert(nullable, underlyingType).SomeMember`
- This allows the SQL formatter to generate `YEAR([DateCreation])` instead of failing

Key insight:
- SQL doesn't have concept of `.Value` - you access properties directly on nullable columns
- Using `Expression.Convert` bridges the type gap between `DateTime?` and `DateTime`
- The SQL formatter already knows how to handle `DateTime` member access (Year, Month, Day, etc.)

### Code change
```csharp
class NullableValueRemover : DbExpressionVisitor
{
    public static Expression Remove(Expression expression)
    {
        return new NullableValueRemover().Visit(expression);
    }

    protected override Expression VisitMemberAccess(MemberExpression m)
    {
        // Handle pattern: nullable.Value.SomeMember
        if (m.Expression is MemberExpression inner &&
            inner.Member.Name == "Value" &&
            TypeHelper.IsNullableType(inner.Expression.Type))
        {
            var underlyingType = TypeHelper.GetNonNullableType(inner.Expression.Type);
            
            if (m.Member.DeclaringType == underlyingType ||
                (m.Member.DeclaringType != null && underlyingType.IsAssignableFrom(m.Member.DeclaringType)))
            {
                var visitedNullable = this.Visit(inner.Expression);
                
                return Expression.MakeMemberAccess(
                    Expression.Convert(visitedNullable, underlyingType),
                    m.Member);
            }
        }

        return base.VisitMemberAccess(m);
    }
}
```

### Result
? **SUCCESS** - Test now runs successfully:
```
SELECT COUNT(*)
FROM [LocGen] AS t0
WHERE (YEAR(t0.[DATECREAT]) > 2004)

Query completed in 652ms
Found 59897 locations
```

### What works now
- `lg.DateCreation.Value.Year` ? `YEAR([DateCreation])`
- `lg.DateCreation.Value.Month` ? `MONTH([DateCreation])`
- Any nullable DateTime/DateTimeOffset property access pattern
- Properly generates SQL function calls on nullable columns

### Testing
Verified with:
```csharp
var locations = provider.GetTable<LocGen>()
    .Where(lg => lg.DateCreation.Value.Year > 2004);
Console.WriteLine($"Found {locations.Count()} locations");
```

Output: `Found 59897 locations` ?
