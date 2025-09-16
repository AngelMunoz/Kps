# Code Purification Analysis

## Overview
This document tracks functions in the Pomo game codebase that rely on callbacks, hardcoded data, or external dependencies, and provides a strategy for making the code more pure and testable.

## Summary
**Total Functions Analyzed**: 15 functions across 4 files  
**Functions Requiring Purification**: 11 functions  
**Pure Functions (Good Examples)**: 4 functions  

**Main Issues Identified**:
- Direct access to global stores (`EffectStore.definitions`, `AbilityStore.definitions`)
- Callback-based RNG system (`rng: unit -> float`)
- Direct state mutation
- Inconsistent RNG approaches across modules

## Analysis Status
- [x] Resolution.fs analysis complete
- [x] Gameplay.fs analysis complete  
- [x] Other F# files analysis complete
- [x] High-level purification strategy documented
- [x] Implementation checklist created

## Functions Requiring Purification

### Resolution.fs

#### Functions with External Dependencies:
1. **`checkTauntTarget`** (lines 25-55)
   - **Hardcoded data**: Direct access to `Pomo.Lib.Content.EffectStore.definitions`
   - **Dependency**: Relies on global effect store for effect definitions

2. **`validateAction`** (lines 425-465)
   - **Hardcoded data**: Direct access to `AbilityStore.definitions[abilityId]`
   - **Dependency**: Relies on global ability store

3. **`resolveMeleeAttack`** (lines 467-530)
   - **External dependency**: Uses `rparams.rng` callback for random number generation. To be replaced by `rparams.services.rng()`.
   - **Hardcoded data**: Accesses global stores via validation

4. **`resolveCastSpell`** (lines 532-650)
   - **External dependency**: Uses `rparams.rng` callback for random number generation. To be replaced by `rparams.services.rng()`.
   - **Hardcoded data**: Direct access to `EffectStore.definitions[effectId]` (line 555)

5. **`processEffect`** (lines 365-400)
   - **Hardcoded data**: Direct access to `EffectStore.definitions[effectId]`

6. **`apply`** (lines 685-705)
   - **Side effects**: Mutates game state directly.
   - **External dependency**: Uses `state.rng` callback. To be replaced by a services handle.

#### Functions with Callbacks:
- All resolver functions accept `rng: unit -> float` callback, which will be moved into a services handle.
- `ResolverParams` type contains `rng` callback field, which will be moved into a services handle.

### Gameplay.fs

#### Functions with External Dependencies:
1. **`applyModifiers`** (lines 17-120)
   - **Hardcoded data**: Direct access to `Pomo.Lib.Content.EffectStore.definitions.[activeEffect.EffectId]`
   - **Dependency**: Relies on global effect store for modifier calculations

2. **`create`** (lines 125-127)
   - **External dependency**: Uses `System.Random.Shared.NextDouble()` for RNG. To be replaced by a services handle.
   - **Side effect**: Creates mutable state with global random

3. **`tick`** (lines 135-240)
   - **Hardcoded data**: Direct access to `Pomo.Lib.Content.EffectStore.definitions`
   - **Side effects**: Mutates game state directly.
   - **External dependency**: Uses `StatusEffects.tickEffects` which has its own dependencies

4. **GameState type** (lines 11-16)
   - **Callback dependency**: Contains `rng: unit -> float` field. This will be removed and accessed via a services handle.
   - **Mutable state**: Uses adaptive collections that can be mutated

#### Functions with Callbacks:
- `GameState` type contains `rng: unit -> float` callback, which will be removed.
- `create'` function accepts RNG callback parameter, which will be replaced by a services handle.

#### Functions with Side Effects:
- `tick` function performs extensive state mutations
- Direct assignment to entity resources and effects

### Other Files

#### Effects.fs
1. **`applyEffect`** (lines 7-60)
   - **Pure function**: No external dependencies or side effects
   - **Good example**: Takes all dependencies as parameters

2. **`tickEffects`** (lines 62-210)
   - **Pure function**: No external dependencies, takes effect definitions as parameter
   - **Good example**: All data passed as parameters, no global state access

#### Combat.fs
1. **`calculatePhysicalDamage`** (lines 13-40)
   - **External dependency**: Uses `rng: unit -> float` callback
   - **Otherwise pure**: All other data passed as parameters

2. **`calculateMagicalDamage`** (lines 42-70)
   - **External dependency**: Uses `rng: unit -> float` callback
   - **Otherwise pure**: All other data passed as parameters

3. **`calculateHealing`** (lines 72-82)
   - **External dependency**: Uses `System.Random` directly
   - **Inconsistent**: Different RNG approach than other functions

#### Content.fs
1. **`AbilityStore.definitions`** (lines 8-70)
   - **Hardcoded data**: Static map of ability definitions
   - **Global state**: Accessed directly throughout codebase

2. **`EffectStore.definitions`** (referenced but not in this file)
   - **Hardcoded data**: Static map of effect definitions
   - **Global state**: Accessed directly throughout codebase

## Purification Strategy

### 1. Dependency Injection Pattern
**Goal**: Replace hardcoded global store access with injected dependencies

**Approach**:
- Create service interfaces for data access (`IAbilityStore`, `IEffectStore`)
- Create a central `EngineServices` handle to hold all external dependencies, including the RNG callback.
- Pass this handle as a parameter to functions that need it.
- Update `ResolverParams` to include the services handle.

**Example**:
```fsharp
type EngineServices = {
  abilityStore: IAbilityStore
  effectStore: IEffectStore
  rng: unit -> float // Centralized RNG callback
}

type ResolverParams = {
  entities: amap<int<EntityId>, All>
  derivedStats: amap<int<EntityId>, Attributes.DerivedStats>
  gameTime: cval<int64<Tick>>
  services: EngineServices  // Central handle for dependencies
}
```

### 2. Centralized Random Number Generation
**Goal**: Abstract the RNG dependency by centralizing it.

**Approach**:
- The `unit -> float` callback is an acceptable abstraction for now.
- Instead of passing it directly, it will be part of the `EngineServices` handle. This makes function signatures cleaner and dependencies more explicit.

**Example**:
```fsharp
type EngineServices = {
  // ... other services
  rng: unit -> float // Centralized RNG callback
}

// Functions will access it via the services handle
let resolveMeleeAttack (rparams: ResolverParams) =
  let randomValue = rparams.services.rng()
  // ...
```

### 3. State Transformation
**Goal**: Clarify the boundary between pure logic and state mutation.

**Approach**:
- Core logic functions should be pure, taking state as input and returning a description of the intended changes (e.g., a `StateChange` record).
- A well-defined boundary will then apply these changes to the adaptive state. This separates the "what" from the "how."

**Example**:
```fsharp
type StateChange = {
  entityUpdates: Map<int<EntityId>, All>
  events: GameEvent[]
}

let applyCommand (state: GameState) (cmd: Command) : StateChange =
  // Pure logic that returns changes without mutation
```

### 4. Content Loading Strategy
**Goal**: Decouple core logic from how game data is stored.

**Approach**:
- Define interfaces for content stores (e.g., `IAbilityStore`, `IEffectStore`).
- The core engine will depend on these interfaces, not on concrete file formats.
- An initialization layer will be responsible for loading data from any source (JSON, database, etc.) and providing concrete implementations of the store interfaces.

### 5. Effect System Purification
**Goal**: Make effect processing completely pure and testable

**Approach**:
- Already mostly pure in `Effects.fs` - good example to follow
- Ensure all effect definitions are passed as parameters
- Remove any remaining global state access

## Implementation Checklist

### Phase 1: Foundation Setup
- [ ] Create `IAbilityStore` and `IEffectStore` interfaces
- [ ] Create `EngineServices` type for dependency injection, including the `rng` callback.
- [ ] Create `StateChange` type for immutable updates

### Phase 2: Resolution.fs Purification
- [ ] Update `ResolverParams` to include `EngineServices`
- [ ] Refactor `checkTauntTarget` to use injected effect store
- [ ] Refactor `validateAction` to use injected ability store
- [ ] Update `processEffect` to use injected effect store
- [ ] Refactor combat functions to use the RNG from `EngineServices`.
- [ ] Refactor `apply` function to return `StateChange` instead of mutating

### Phase 3: Gameplay.fs Purification
- [ ] Update `applyModifiers` to use injected effect store
- [ ] Refactor `create` to accept an `EngineServices` handle.
- [ ] Update `GameState` type to remove the direct `rng` field.
- [ ] Refactor `tick` function to return state changes instead of mutating
- [ ] Remove direct state mutations from event processing

### Phase 4: Combat.fs Purification
- [ ] Standardize all combat functions to use the RNG from `EngineServices`.
- [ ] Fix `calculateHealing` to use the consistent RNG approach.
- [ ] Ensure all combat functions are pure except for the RNG access via services.

### Phase 5: Content Loading
- [ ] Implement a loading system that provides concrete `IAbilityStore` and `IEffectStore` implementations.
- [ ] Replace hardcoded stores with the dependency-injected stores.

### Phase 6: Testing & Validation
- [ ] Create unit tests for all purified functions
- [ ] Verify deterministic behavior with a fixed-seed RNG implementation for tests.
- [ ] Performance testing to ensure no regression
- [ ] Integration testing with new dependency injection

### Phase 7: Documentation & Cleanup
- [ ] Update code documentation to reflect pure function contracts
- [ ] Create migration guide for existing code
- [ ] Remove deprecated impure functions
- [ ] Update this analysis document with final results

---
*Last updated: 2025-09-15*