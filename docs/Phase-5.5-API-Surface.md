# Phase 5.5 - GameState API Surface & Architecture

**Status**: 📋 **PLANNING** - Not yet implemented

**Created**: 2025-10-10

## Overview

This phase introduces a high-level API surface for common GameState operations, making it easier for the MonoGame runtime to interact with the core game engine. It also analyzes the architectural decision of where the GameState root should live.

## Goals

1. **API Surface Design**: Create a clean, ergonomic API for common game operations
2. **Architecture Analysis**: Determine optimal location for GameState (Pomo.Lib vs Pomo.Core)
3. **MonoGame Integration Readiness**: Prepare the core for seamless runtime integration

---

## 1. API Surface Design

### 1.1 Core Operations & StateChange Integration

**Design Principle**: All mutation operations leverage the existing `StateChange` mechanism from `Pomo.Lib.Domain.State` and `Resolution.apply`. This maintains consistency with the reactive architecture and enables proper FDA transaction handling.

#### **StateChange Architecture Overview**

The existing codebase uses a well-defined pattern for state mutations:

1. **Command Definition**: `Pomo.Lib.Domain.Rules.Command` (currently: `UseAbility of UseAbilityAction`)
2. **Resolution**: `Resolution.step : GameState -> Command -> aval<StateChange>` computes changes reactively
3. **Application**: `Resolution.apply : GameState -> StateChange -> unit` applies changes via FDA transaction
4. **StateChange Structure**: Contains `entities: HashMap<EntityId, EntityComponents>` and `gameTime: int64<Tick> voption`

**Key Insight**: All new API operations should follow this pattern rather than creating a parallel functional API that returns GameState directly.

#### **Proposed API Surface Categories**

The API will provide two distinct layers:

1. **Command-Based Operations** (for mutations that interact with game logic)
   - Build Command instances
   - Use existing Resolution.step/apply mechanism
   - Examples: ActivateAbility, AdvanceTime

2. **Direct Operations** (for simple state changes that don't require resolution)
   - Create StateChange directly
   - Use Resolution.apply or similar transaction helper
   - Examples: EquipItem, AddEffect, ModifyResources

3. **Query Operations** (non-reactive snapshots for MonoGame)
   - Use `AVal.force` when necessary
   - Clear justification required (see section 1.6)
   - Examples: getEntity, getAliveEntities, getDerivedStatsSnapshot

#### **Entity Management**
- `createEntity: Profession -> BaseAttributes -> GameState -> EntityId * StateChange`
  - Creates a new entity with given profession and stats
  - Returns the new EntityId and StateChange to apply
  - MonoGame calls `Resolution.apply state change` to commit

- `removeEntity: EntityId -> GameState -> StateChange`
  - Removes an entity from the game
  - Returns StateChange with entity removal

- `getEntity: EntityId -> GameState -> EntityComponents option`
  - Retrieves entity components (non-reactive query)
  - Uses `AMap.tryFind` directly on entities cmap (no force needed)

#### **Equipment Operations**
- `equipItem: EntityId -> Slot -> Equipment -> GameState -> Result<StateChange, EquipError>`
  - Equips an item to the specified slot
  - Returns StateChange or error (entity not found, invalid slot)
  - Direct operation: creates StateChange with updated EntityComponents

- `unequipItem: EntityId -> Slot -> GameState -> Result<StateChange, EquipError>`
  - Removes equipment from the specified slot
  - Returns StateChange with equipment removed

- `swapEquipment: EntityId -> Slot -> Slot -> GameState -> Result<StateChange, EquipError>`
  - Swaps equipment between two slots (e.g., Weapon1 ↔ Weapon2)
  - Single atomic StateChange for both slot modifications

#### **Ability Operations**
- `activateAbility: EntityId -> AbilityId -> EntityId[] -> GameState -> aval<StateChange>`
  - Primary interface for using abilities in combat
  - **Uses existing Command pattern**: Creates `UseAbility` command and calls `Resolution.step`
  - Returns adaptive StateChange for reactive resolution
  - MonoGame must evaluate with `AVal.force` and apply
  - Validation (cooldown, resources, status effects) happens in Resolution layer

- `learnAbility: EntityId -> AbilityId -> GameState -> Result<StateChange, AbilityError>`
  - Adds a new ability to an entity's ability list
  - Direct operation: modifies Abilities alist

- `forgetAbility: EntityId -> AbilityId -> GameState -> Result<StateChange, AbilityError>`
  - Removes an ability from an entity
  - Direct operation: removes from Abilities alist

#### **Profession Advancement**
- `advanceStage: EntityId -> GameState -> Result<StateChange, AdvancementError>`
  - Advances entity's profession to next stage (First → Second → Third)
  - Validates stage progression rules
  - Updates BaseStats and Identity.Profession

- `canAdvanceStage: EntityId -> GameState -> bool`
  - Query operation: checks if entity meets requirements for stage advancement
  - No state mutation, no StateChange needed

#### **Resource Management**
- `healEntity: EntityId -> int -> GameState -> Result<StateChange, ResourceError>`
  - Restores HP (capped at max HP from DerivedStats)
  - Direct operation: modifies Resources

- `restoreMP: EntityId -> int -> GameState -> Result<StateChange, ResourceError>`
  - Restores MP (capped at max MP from DerivedStats)
  - Direct operation: modifies Resources

- `damageEntity: EntityId -> int -> GameState -> Result<StateChange, ResourceError>`
  - Applies direct damage (bypasses combat formulas)
  - Direct operation: reduces HP, checks for death

- `setResourceStatus: EntityId -> Status -> GameState -> Result<StateChange, ResourceError>`
  - Manually sets entity status (Alive, Dead, Disabled)
  - Direct operation: modifies Resources.Status

#### **Effect Management**
- `applyEffect: EntityId -> EffectId -> int64<Tick> -> EntityId -> GameState -> Result<StateChange, EffectError>`
  - Manually applies an effect to a target (duration, source entity)
  - Direct operation: adds ActiveEffect to Effects alist

- `removeEffect: EntityId -> EffectId -> GameState -> Result<StateChange, EffectError>`
  - Removes all instances of an effect from an entity
  - Direct operation: filters Effects alist

- `clearAllEffects: EntityId -> GameState -> Result<StateChange, EffectError>`
  - Removes all effects from an entity
  - Direct operation: clears Effects alist

#### **Time & Simulation**
- `advanceTime: int64<Tick> -> GameState -> aval<StateChange>`
  - Advances game time and processes tick-based effects
  - **Uses existing tick mechanism**: Calls `GameState.tick` from Gameplay.fs
  - Returns adaptive StateChange (DoT/HoT damage, effect expiration)
  - MonoGame evaluates and applies via `GameState.applyTick`

- `resetCooldowns: EntityId -> GameState -> Result<StateChange, EntityError>`
  - Clears all ability cooldowns (for testing/debug)
  - Direct operation: clears AbilityCooldowns amap

#### **Query Operations (Non-Reactive)**
- `getAliveEntities: GameState -> EntityId[]`
  - Returns all alive entities
  - **Justification**: See section 1.6

- `getReadyAbilities: EntityId -> GameState -> AbilityId[]`
  - Returns abilities not on cooldown
  - **Justification**: See section 1.6

- `getDerivedStatsSnapshot: EntityId -> GameState -> DerivedStats option`
  - Forces evaluation of adaptive stats and returns snapshot
  - **Requires AVal.force**: See section 1.6

- `getEffectiveStats: EntityId -> GameState -> DerivedStats option`
  - Returns fully calculated stats including equipment and effects
  - **Requires AVal.force**: See section 1.6

### 1.2 Error Types

```fsharp
type EquipError =
  | EntityNotFound
  | InvalidSlot
  | IncompatibleEquipment

type AbilityError =
  | AbilityNotFound
  | EntityNotFound
  | OnCooldown
  | InsufficientResources
  | InvalidTarget
  | Stunned
  | Silenced
  | AlreadyKnown
  | NotKnown
  | RequirementsNotMet

type AdvancementError =
  | EntityNotFound
  | AlreadyMaxStage
  | RequirementsNotMet
```

### 1.3 API Design Philosophy

- **StateChange-Centric**: All mutations produce `StateChange` records, maintaining consistency with existing Resolution architecture
- **Command Integration**: Complex operations (abilities, time advancement) use existing Command/Resolution pattern
- **Transactional**: State changes applied via `Resolution.apply` or similar transaction helpers
- **Validation First**: Check preconditions before creating StateChange
- **Explicit Errors**: Use Result types for operations that can fail
- **Reactive Where Possible**: Leverage adaptive values; force evaluation only when necessary (see 1.6)
- **Testable**: All operations are deterministic and unit-testable

### 1.4 Usage Example

```fsharp
// In Pomo.Core/PomoGame.fs - MonoGame Update loop
override this.Update(gameTime) =
  match gameState with
  | Some state ->
      // Process player input
      let input = InputManager.getInput()

      match input with
      | UseAbility(abilityId, targetIds) ->
          // Command-based operation: returns aval<StateChange>
          let changeAVal = activateAbility playerId abilityId targetIds state
          let change = AVal.force changeAVal  // Force evaluation for immediate application
          Resolution.apply state change

      | EquipItemFromInventory(itemId, slot) ->
          // Direct operation: returns Result<StateChange, Error>
          let equipment = loadEquipmentFromInventory itemId
          match equipItem playerId slot equipment state with
          | Ok change ->
              Resolution.apply state change
          | Error InvalidSlot ->
              // Show UI feedback
              ()
          | Error err -> ()

      | AdvanceClass ->
          // Direct operation: returns Result<StateChange, Error>
          match advanceStage playerId state with
          | Ok change ->
              Resolution.apply state change
              // Show "Level Up" animation
          | Error AlreadyMaxStage ->
              // Show "Max level reached"
              ()

      | QueryStats ->
          // Non-reactive query (see section 1.6)
          match getDerivedStatsSnapshot playerId state with
          | Some stats ->
              // Display in UI
              displayStats stats
          | None -> ()

      // Advance time every frame
      let deltaTicks = int64 gameTime.ElapsedGameTime.Ticks * 1L<Tick>
      let tickChangeAVal = advanceTime deltaTicks state
      let tickChange = AVal.force tickChangeAVal
      GameState.applyTick state tickChange  // Use specialized tick application

  | None -> ()
```

### 1.5 StateChange Application Helpers

To maintain consistency, provide helper functions for applying state changes:

```fsharp
module StateChangeHelpers =
  /// Apply a StateChange using Resolution.apply (for entity updates only)
  let applyEntityChange (state: GameState) (change: StateChange) =
    Resolution.apply state change

  /// Apply a StateChange with time update (for tick operations)
  let applyWithTime (state: GameState) (change: StateChange) =
    GameState.applyTick state change

  /// Force and apply an adaptive StateChange
  let forceAndApply (state: GameState) (changeAVal: aval<StateChange>) =
    let change = AVal.force changeAVal
    applyEntityChange state change
```

### 1.6 Non-Reactive Query Operations: Justification and Usage

#### **Why Non-Reactive Queries Are Necessary**

The MonoGame runtime operates in a **pull-based, imperative game loop** where the engine needs immediate, concrete values to:
1. **Render the current frame**: Display HP bars, ability cooldowns, stat tooltips
2. **Make AI decisions**: Select targets, choose abilities based on current state
3. **Validate player input**: Check if an action is legal before attempting it
4. **Update UI elements**: Show character sheets, inventory screens, combat logs

While FDA's reactive model is ideal for **incremental computation within Pomo.Lib**, MonoGame's `Update()` and `Draw()` methods require **point-in-time snapshots** of the world state.

#### **When AVal.force Is Required**

**REQUIRED** for adaptive computations that must be evaluated immediately:
- `activateAbility`: Returns `aval<StateChange>` from Resolution.step → **Must force** in MonoGame.Update to apply immediately
- `advanceTime`: Returns `aval<StateChange>` from GameState.tick → **Must force** in MonoGame.Update for frame-by-frame time advancement
- `getDerivedStatsSnapshot`: Returns fully computed stats from adaptive graph → **Must force** to display in UI

**NOT REQUIRED** for direct data access:
- `getEntity`: Reads from `state.entities` cmap directly → **No force needed** (synchronous cmap access)
- `getAliveEntities`: Can use `AMap.tryFind` or direct iteration → **No force needed** if using synchronous access patterns
- `getReadyAbilities`: Reads from `AbilityCooldowns` amap → **No force needed** with direct access

#### **Where This Code Lives in MonoGame Integration**

**In Pomo.Core (MonoGame Layer):**

```fsharp
// File: Pomo.Core/GameStateQueries.fs
module Pomo.Core.GameStateQueries

open Pomo.Lib.Gameplay
open FSharp.Data.Adaptive

/// Query operations for MonoGame - forces adaptive values when necessary
module Queries =

  /// Get entity without forcing (direct cmap access)
  let getEntity (entityId: int<EntityId>) (state: GameState) : EntityComponents option =
    state.entities |> AMap.tryFind entityId

  /// Get alive entities without forcing (uses existing adaptive projection)
  let getAliveEntities (state: GameState) : int<EntityId>[] =
    let aliveSet = GameState.aAlive state.entities
    // Force the set evaluation for immediate use
    ASet.force aliveSet |> Set.toArray

  /// Get derived stats snapshot - REQUIRES FORCE
  let getDerivedStatsSnapshot (entityId: int<EntityId>) (state: GameState) : DerivedStats option =
    let derivedStatsMap = GameState.getDerivedStats state
    // Force the adaptive map computation
    let stats = derivedStatsMap |> AMap.tryFind entityId
    match stats with
    | Some statsAVal -> Some (AVal.force statsAVal)  // Force the individual stat computation
    | None -> None

  /// Get ready abilities for entity
  let getReadyAbilities (entityId: int<EntityId>) (state: GameState) : int<AbilityId>[] =
    let readySet = GameState.aReadyAbilities state.entities state.gameTime
    // Force evaluation and filter by entity
    ASet.force readySet
    |> Set.toArray
    |> Array.filter (fun (id, _) -> id = entityId)
    |> Array.map snd

// File: Pomo.Core/PomoGame.fs
type PomoGame() =
  // ... (uses Queries module for all non-reactive access)
```

#### **Justification Summary**

| Operation | Requires Force? | Reason | Location |
|-----------|----------------|--------|----------|
| `activateAbility` | ✅ Yes | Returns `aval<StateChange>` from Resolution | MonoGame.Update |
| `advanceTime` | ✅ Yes | Returns `aval<StateChange>` from tick logic | MonoGame.Update (every frame) |
| `getDerivedStatsSnapshot` | ✅ Yes | Adaptive stat computation must be evaluated | MonoGame UI rendering |
| `getAliveEntities` | ⚠️ Partial | Can use `ASet.force` on existing projection | MonoGame AI/targeting |
| `getReadyAbilities` | ⚠️ Partial | Can use `ASet.force` on existing projection | MonoGame UI (ability bar) |
| `getEntity` | ❌ No | Direct cmap access is synchronous | MonoGame validation/queries |

#### **Performance Considerations**

- **AVal.force on computed values**: Acceptable because FDA caches results. Re-forcing unchanged adaptive values returns cached result.
- **Frequency**: Queries like `getDerivedStatsSnapshot` should only be called when UI needs refresh (e.g., on character sheet open), not every frame.
- **Existing Projections**: Where possible, use existing adaptive projections (`GameState.aAlive`, `GameState.aReadyAbilities`) and force those rather than re-computing.

#### **Alternative: Reactive UI Bindings (Future)**

In a more advanced MonoGame integration, we could use reactive subscriptions:

```fsharp
// Future enhancement: reactive UI updates
let statsSubscription =
  GameState.getDerivedStats state
  |> AMap.tryFind playerId
  |> AVal.map (Option.defaultValue DerivedStats.empty)
  |> AVal.addCallback (fun stats -> updateStatsUI stats)
```

This would eliminate the need for forcing in UI code, but requires significant MonoGame integration work (Phase 6+).

**For Phase 5.5, the non-reactive query approach is justified and appropriate.**

---

## 2. Composition Root & GameState Initialization Analysis

### 2.1 The Original Question Clarified

**User's Original Concern**: "The analysis was if we wanted to have the `GameState.create` or similar function within Pomo.Lib or in Pomo.Core, basically where should our 'container root' should live."

This is **NOT** about where the `GameState` type definition lives (clearly in Pomo.Lib), but rather:
1. Where should **service composition** happen? (creating IAbilityStore, IEffectStore, etc.)
2. Where should **GameState initialization** (`GameState.create'`) be called?
3. Where is the **composition root** that wires up the entire application?

### 2.2 Composition Root Pattern

The **composition root** is the place in the application where all dependencies are instantiated and wired together. In dependency injection terminology, this is the "entry point" that:
- Creates concrete implementations of interfaces (e.g., Content-backed stores)
- Assembles the `EngineServices` handle
- Calls `GameState.create'` with the services
- Owns the GameState instance for the lifetime of the game

### 2.3 Analysis: Where Should Composition Root Live?

#### **Option A: Composition Root in Pomo.Core (Recommended)**

**Rationale**:
- **Pomo.Core** is the **application layer** that owns the game lifecycle
- MonoGame's `PomoGame.Initialize()` is the natural composition root
- Services depend on concrete implementations (content files, RNG, etc.) which are application concerns
- Pomo.Lib remains a pure library with only interface definitions

**Implementation**:
```fsharp
// In Pomo.Core/PomoGame.fs (Composition Root)
type PomoGame() as this =
  inherit Game()

  let mutable gameState: GameState option = None

  override this.Initialize() =
    // === COMPOSITION ROOT ===
    // 1. Create concrete service implementations
    let abilityStore =
      { new IAbilityStore with
          member _.tryFind id = Content.AbilityStore.definitions |> Map.tryFind id |> ValueOption.ofOption
          member _.find id = Content.AbilityStore.definitions |> Map.find id }

    let effectStore =
      { new IEffectStore with
          member _.tryFind id = Content.EffectStore.definitions |> Map.tryFind id |> ValueOption.ofOption
          member _.find id = Content.EffectStore.definitions |> Map.find id }

    let formulaStore =
      { new IFormulaStore with
          member _.tryFind id = Content.FormulaStore.definitions |> Map.tryFind id |> ValueOption.ofOption
          member _.find id = Content.FormulaStore.definitions |> Map.find id }

    let rng = fun () -> System.Random().NextDouble()

    // 2. Compose EngineServices handle
    let services = {
      abilityStore = abilityStore
      effectStore = effectStore
      formulaStore = formulaStore
      rng = rng
    }

    // 3. Initialize GameState (calling Pomo.Lib function with services)
    let state = GameState.create' services

    // 4. Perform initial setup (create player, enemies, etc.)
    // ... (using GameStateOperations API)

    gameState <- Some state
```

**Pros**:
- ✅ Clean separation: Pomo.Lib defines **what** (interfaces, logic), Pomo.Core defines **how** (concrete implementations)
- ✅ Pomo.Lib remains framework-agnostic (could work with different game engines)
- ✅ All application-specific concerns (file paths, RNG seeds, etc.) stay in Pomo.Core
- ✅ Natural fit with MonoGame lifecycle (Initialize → Update → Draw)
- ✅ Easy to swap implementations (e.g., test doubles, database-backed stores in future)

**Cons**:
- ❌ Slightly more verbose initialization code in Pomo.Core
- ❌ Pomo.Core must reference Pomo.Lib (but this is expected and correct)

**Verdict**: ✅ **Recommended**

#### **Option B: Composition Root in Pomo.Lib**

If we wanted the composition root in Pomo.Lib, we'd have a convenience function:

```fsharp
// In Pomo.Lib/Gameplay.fs
module GameState =
  let create() =
    // Hard-coded dependencies (less flexible)
    let services = {
      abilityStore = Content.AbilityStore.createStore()
      effectStore = Content.EffectStore.createStore()
      // ...
    }
    create' services
```

**Pros**:
- ✅ Simpler for Pomo.Core (just call `GameState.create()`)
- ✅ Useful for quick testing/prototyping

**Cons**:
- ❌ Couples Pomo.Lib to specific concrete implementations
- ❌ Hard to substitute implementations (testing, database migration, etc.)
- ❌ Violates dependency inversion principle (high-level module depends on low-level details)
- ❌ Less flexible for different application contexts

**Verdict**: ❌ **Not Recommended** (though the convenience function exists for simple use cases)

### 2.4 Decision: Composition Root in Pomo.Core

**Conclusion**: The composition root should live in **Pomo.Core**, specifically in `PomoGame.Initialize()`.

**Key Distinction**:
- `GameState` **type** lives in **Pomo.Lib** (domain layer)
- `GameState.create'` **function** lives in **Pomo.Lib** (takes services as parameter)
- `GameState.create` **convenience function** lives in **Pomo.Lib** (for testing/simple scenarios)
- **Service composition** and **state initialization** happen in **Pomo.Core** (application layer)

This maintains proper architectural boundaries while giving Pomo.Core full control over the application lifecycle.

---

## 3. Integration with MonoGame (Phase 6)

### 3.1 Composition Root Pattern

```fsharp
// In Pomo.Core/PomoGame.fs
type PomoGame() as this =
  inherit Game()

  let mutable gameState: GameState option = None

  override this.Initialize() =
    // Create services (composition root)
    let services = {
      abilityStore = AbilityStore.create()
      effectStore = EffectStore.create()
      formulaStore = FormulaStore.create()
      rng = fun () -> System.Random().NextDouble()
    }

    // Initialize GameState
    gameState <- Some (GameState.create' services)

    // Create player entity
    let state = gameState.Value
    let playerId, state' =
      createEntity
        (Profession.create Power First)
        (BaseAttributes.create 15 10 10 10)
        state

    gameState <- Some state'

  override this.Update(gameTime) =
    match gameState with
    | Some state ->
        // Process input
        let input = InputManager.getInput()
        let state' = handlePlayerAction state input

        // Advance time
        let deltaTicks = gameTime.ElapsedGameTime.Ticks * 1L<Tick>
        let state'' = advanceTime deltaTicks state'

        gameState <- Some state''
    | None -> ()
```

### 3.2 API Module Organization

```
Pomo.Lib/
  Domain.fs            (types)
  Gameplay.fs          (GameState, reactive projections)
  Resolution.fs        (ability resolution logic)
  GameStateOperations.fs  ⬅️ NEW: High-level API surface
  Content.fs           (data)
  FormulaParser.fs     (formulas)
```

---

## 4. Phase 5.5 Deliverables

### 4.1 Code Deliverables
- [ ] `GameStateOperations.fs` module with all API functions
- [ ] Unit tests for each operation
- [ ] Integration tests demonstrating MonoGame-like usage patterns
- [ ] XML documentation for all public functions

### 4.2 Documentation Deliverables
- [x] This design document
- [ ] API reference documentation
- [ ] Migration guide from direct GameState manipulation to API
- [ ] MonoGame integration examples

### 4.3 Success Criteria
- All common operations have clean, tested API functions
- MonoGame runtime can interact with GameState without knowing FDA internals
- Error handling is explicit and comprehensive
- Performance is acceptable (operations complete in <1ms for typical cases)

---

## 5. Command Pattern Analysis & Future Considerations

### 5.1 Existing Command Pattern in Pomo.Lib

**Current Implementation** (`Pomo.Lib.Domain.Rules.Command`):

```fsharp
[<Struct>]
type UseAbilityAction = {
  actor: int<EntityId>
  targets: int<EntityId>[]
  abilityId: int<AbilityId>
}

[<Struct>]
type Command = UseAbility of action: UseAbilityAction
```

**Purpose**: The existing `Command` type represents **validated, resolvable game actions** that interact with the combat/ability system.

**Key Characteristics**:
- **Resolution-Centric**: Commands flow through `Resolution.step : GameState -> Command -> aval<StateChange>`
- **Combat-Focused**: Currently only `UseAbility`, designed for ability execution with full validation and effect resolution
- **Reactive**: Returns adaptive StateChange for incremental computation
- **Deterministic**: Part of the core simulation logic

**Current Usage**:
1. MonoGame/Core creates `Command` from player input
2. `Resolution.step` processes command reactively
3. Returns `aval<StateChange>` with computed results
4. MonoGame forces evaluation and applies via `Resolution.apply`

### 5.2 Proposed GameStateOperations API vs Command Pattern

The API operations proposed in section 1.1 serve a **different purpose** than `Domain.Rules.Command`:

| Aspect | Domain.Rules.Command | GameStateOperations API |
|--------|---------------------|------------------------|
| **Purpose** | Combat/ability resolution logic | High-level state manipulation |
| **Scope** | Complex, validated actions (abilities) | All game operations (equipment, resources, effects) |
| **Integration** | Resolution pipeline with formulas | Direct StateChange creation or Command delegation |
| **Validation** | Integrated into resolution (cooldowns, status effects) | Explicit validation before StateChange creation |
| **Examples** | `UseAbility` | `equipItem`, `healEntity`, `applyEffect`, `advanceStage` |

**Relationship**:
- `activateAbility` API function **uses** `Domain.Rules.Command` internally by creating `UseAbility` command
- Other operations (equipment, resources, effects) create `StateChange` directly without commands
- Commands are for **complex resolution logic**; direct operations are for **simple state changes**

### 5.3 Future Command Pattern Extension (Optional)

If we expand `Domain.Rules.Command` in the future:

```fsharp
type Command =
  | UseAbility of UseAbilityAction
  | MoveEntity of MoveAction          // Future: movement with pathfinding
  | UseItem of UseItemAction          // Future: consumable items
  | StartChanneling of ChannelAction  // Future: channeled abilities
```

**When to add new Command variants**:
- ✅ Action requires complex validation and resolution logic
- ✅ Action interacts with multiple systems (effects, formulas, cooldowns)
- ✅ Action needs to be deterministic and reproducible
- ✅ Action benefits from reactive adaptive computation

**When to use direct operations instead**:
- ❌ Simple state changes (equip item, heal, add effect)
- ❌ Administrative operations (clear cooldowns, remove entity)
- ❌ Query operations (get stats, check alive status)

### 5.4 Event Sourcing Pattern (Future, Different Concern)

The **event sourcing** pattern mentioned in previous documentation represents a **separate concern** from Commands:

```fsharp
// Events = things that HAPPENED (past tense, immutable facts)
type GameEvent =
  | EntityCreated of EntityId * Profession * BaseAttributes
  | AbilityActivated of EntityId * AbilityId * Result
  | EquipmentChanged of EntityId * Slot * Equipment option
  | EffectApplied of EntityId * EffectId * int64<Tick>
  | EntityDied of EntityId

// Commands = things to DO (imperative, can fail)
type Command = UseAbility of UseAbilityAction
```

**Key Distinction**:
- **Commands** represent **intent** (player wants to use ability)
- **Events** represent **facts** (ability was used, dealt X damage)
- **StateChange** is the intermediate result (which entities changed, how)

**If we implement event sourcing** (future Phase 6+):
1. Command → Resolution → StateChange (existing)
2. StateChange → Event log (new)
3. Event log → Replay/Analytics (new)

This would be useful for:
- Combat replays
- Network synchronization
- Debugging (replay from event log)
- Analytics (which abilities were used, how much damage dealt)

### 5.5 Async Operations (MonoGame Animations, Future)

If abilities require animations or delays:

```fsharp
// In Pomo.Core (not Pomo.Lib)
module AnimatedOperations =
  let activateAbilityWithAnimation
    (entityId: EntityId)
    (abilityId: AbilityId)
    (targets: EntityId[])
    (state: GameState)
    : Async<unit> =
    async {
      // 1. Validate and compute StateChange
      let changeAVal = activateAbility entityId abilityId targets state
      let change = AVal.force changeAVal

      // 2. Play animation (MonoGame concern)
      do! playAbilityAnimation entityId abilityId

      // 3. Apply state change
      Resolution.apply state change

      // 4. Play impact effects
      do! playDamageEffects targets
    }
```

**Important**: Async operations belong in **Pomo.Core**, not Pomo.Lib. The core simulation remains synchronous and deterministic.

### 5.6 Summary: Command Patterns in Context

| Pattern | Location | Purpose | Status |
|---------|----------|---------|--------|
| `Domain.Rules.Command` | Pomo.Lib | Combat resolution logic | ✅ Exists (UseAbility) |
| `GameStateOperations` API | Pomo.Lib | High-level state manipulation | 📋 Phase 5.5 |
| Extended Command variants | Pomo.Lib | Future complex actions (move, items) | 🔮 Future |
| Event sourcing | Pomo.Lib + Pomo.Core | Replay/analytics | 🔮 Optional future |
| Async/animated operations | Pomo.Core | MonoGame integration | 🔮 Phase 6+ |

**Conclusion**: The existing `Command` pattern and proposed API operations serve complementary purposes and should coexist. Commands handle complex resolution logic; API operations provide ergonomic access for all state changes.

---

## Summary

Phase 5.5 provides the missing link between the powerful but low-level reactive core (Pomo.Lib) and the high-level game runtime (Pomo.Core). By keeping GameState in Pomo.Lib and providing a clean API surface, we maintain architectural integrity while improving ergonomics for MonoGame integration.

**Next Steps**: Implement `GameStateOperations.fs` and integrate with MonoGame in Phase 6.
