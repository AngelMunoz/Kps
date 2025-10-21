# Known Issues

## Navigation Grid Mismatch with Scenario Bounds

**Date**: 2025-01-11
**Status**: Open
**Priority**: Medium

### Description
The navigation grid does not match the scenario bounds. The player entity stops walking halfway through the scenario, but the path debug visualization draws beyond that point.

### Reproduction
1. Load the wilderness scenario (default test scenario)
2. Right-click to move the player to a distant location
3. Observe that the player stops moving before reaching the target
4. The green path preview extends beyond where the player stops

### Root Cause
The navigation grid is created with hardcoded dimensions (32.0f cell size, 16.0f offset) in `ScenarioLoader.loadScenario`, but these don't align with the actual scenario bounds from `ScenarioDefinitions`.

### Affected Code
- `Pomo.Core/TestScenarioBuilder.fs` - `ScenarioLoader.loadScenario`
- `Pomo.Lib/Content.fs` - `ScenarioDefinitions.createWildernessScenario` (1000x800 bounds)

### Suggested Fix
The grid creation should use the scenario's actual bounds:
```fsharp
Grid.createGrid scenario scenario.BoundsWidth scenario.BoundsHeight
```

Or derive appropriate cell size from bounds dynamically.
