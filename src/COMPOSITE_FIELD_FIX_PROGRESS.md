# Composite Field .Value.Date Fix Progress

## Problem
`LocationSummary.DATEDEP = locgen.DTDepartMateriel` projection was causing:
```
The member access 'System.Nullable`1[System.DateTime] DTDepartMateriel' is not supported
```

## Root Cause
- `LocGen.DTDepartMateriel` has `[CompositeField]` attribute
- `LocationSummary.DATEDEP` is just a regular `DateTime?` property
- When projecting, the binding created a reference to `DTDepartMateriel`
- SqlFormatter can't generate SQL for composite fields

## Solution Implemented
Added **`CompositeFieldProjectionExpander`** class that runs BEFORE QueryBinder.

### How It Works
1. Runs as Step 1b in `AdvantageMapper.Translate()`, before `base.Translate()`
2. Only rewrites composite fields when inside a projection (MemberInit/New expression)
3. Replaces `locgen.DTDepartMateriel` with `locgen.DATEDEP` (the underlying date column)
4. This ensures the projection binds to the actual database column

### Key Design Decision
- **Inside projections** (DTO/anonymous types): Rewrite to date column ?
- **Direct SELECT**: Don't rewrite, let `CompositeFieldExpander` handle it ?

This preserves existing functionality while fixing the DevExpress scenario.

## Test Results
? All 116 tests pass
? No regressions
? `SelectCompositeDate` test still works (direct SELECT of composite)
? `CompositeField_DevExpressExactPattern` test works (DTO projection with GROUP BY)

## Files Changed
1. `AdvantageMapping.cs` - Added `CompositeFieldProjectionExpander` class
2. `AdvantageCompositeFieldRewriter.cs` - Enhanced `.Value.Date` handling
3. `AdvantageMapping.cs` (`Columnizer`) - Skip composite fields

## Next Step
**PLEASE TEST YOUR ACTUAL DEVEXPRESS QUERY** and report if it works!
