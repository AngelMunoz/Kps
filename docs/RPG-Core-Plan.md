# RPG Core Implementation Plan (F# + MonoGame, Reactive with FSharp.Data.Adaptive)

This document sketches a step-by-step, incremental roadmap to build the core of an RPG focused on magic and physical prowess. Rendering, UI, audio, networking, and platform specifics are deferred until the core simulation is solid. The core is implemented using FSharp.Data.Adaptive (FDA) to model evolving state as incremental data flows, with all game logic and state managed through adaptive collections and pure data transformations.

## 🎯 **ARCHITECTURAL VISION: BATTLE SUBSYSTEM**

**IMPORTANT**: The current Pomo.Lib implementation represents a **battle subsystem** that will be integrated into a larger game architecture. The full game will support multiple contexts:

- **Lobby State**: Lightweight character management, equipment, no battle processing
- **Battle State**: Full Pomo.Lib engagement with effects, abilities, combat resolution
- **Exploration State**: Selective features (stats, inventory) without full battle overhead

**Shared Domain**: Core types (BaseAttributes, DerivedStats, Equipment, Abilities) are used across all contexts. **Context-Specific State**: Battle-only features (ActiveEffects, Cooldowns, Resources, GameTime) are isolated to battle subsystem.

**Performance Goal**: Zero battle processing overhead when not in battle context.

Good luck to us — and let’s move methodically.

## Guiding Principles

- Core-first: mechanics, rules, and data before visuals.
- Deterministic and testable: pure logic and reproducible simulations.
- Small vertical slices: integrate the smallest end-to-end interactions early.
- Reactive state graph: use FDA to derive views and system inputs from authoritative state.
- Extensibility: design for adding stats, effects, skills, and content over time.
- Performant Reactive Core: Use incremental adaptive collections (`amap`, `aset`, `alist`, `cmap`, `clist`, etc.) for dynamic state to enable efficient, fine-grained updates. All game state, including entities, effects, and abilities, is managed adaptively. For static or rarely changing data, BCL collections are suitable.
- **Dependency Injection via Handles:** Decouple pure logic from side-effecting services (e.g., logging, content loading) by defining abstractions (`IContentService`). Pass these services as a single `EngineServices` "handle" to core logic. This makes dependencies explicit and the core highly testable. The application's entry point will act as the "composition root" to assemble concrete services.
- When multiple adaptive values are required but feel scattered, SRTPs are an option to consider.
- Adaptive values must be adaptive until they must be evaluated, meaning that every computation or adaptive value must be done in the "adaptive realm" until the final result is needed.
  - If multiple values are required, consider using `adaptive { ... }` computation expressions to group them together.
  - Adaptive Collections that must be kept after an adaptive computation should prefer `<Module>.<method>A` to avoid unnecesary converstions to resolved adaptive values. Example: `AList.mapA` instead of `AList.map <opperation> |> AList.toAList`.

## Phase 0 — Foundations

1. Domain Skeleton

   - Define core primitives:
     - `EntityId`
     - `Ticks` (a type alias for `int64<ticks>`)
     - RNG seed/source
   - Factions (`Faction` type):
     - Player, Enemy, Neutral
   - Tags (`Tag` type):
     - Biological, Artificial, Undead
   - Professions/Classes:
     - `Family`: Strength, Magic, Charm, Sensory
     - `Stage`: First, Second, Third
     - `Profession`: A record combining `Family` and `Stage`.
   - Elemental Types (`Element` type):
     - Fire, Earth, Water, Air, Light, Dark, Neutral.
   - Stats model:
     - `BaseAttributes` record: Strength, Magic, Sense, Charm
     - `DerivedStats` record:
     - Strength derived: AttackPower, Accuracy, Dexterity
     - Magic derived: MagicPotential, MagicAttack, MagicDefense
     - Sense derived: DetectAbility, WillPower, Luck
     - Charm derived: HealthPoints, DefensePotential, Hevasion
     - Resistances (`Map<Element, float>`)
   - Resources (`Resources` record):
     - HP, MP
     - `Status` flag: Alive, Dead, Disabled
   - Inventory and Equipment slots (`Slot` type):
     - Head, Chest, Legs, Hands, Weapon1, Weapon2, Accessory

2. Rules and Units

   - Establish canonical units: time in ticks, distances in tiles/meters if needed later.
   - RNG abstraction for deterministic simulation and testability.

3. Project Scaffolding
   - Create Core namespaces: Pomo.Core.Gameplay, Pomo.Core.Rules, Pomo.Core.Content
   - Add test project (future): property tests for stat and effect composition.

Deliverable: Domain types, stat archetypes, measures, and base attributes are fully implemented. All core types (EntityId, Tick, EffectId, AbilityId, Faction, Tag, Family, Stage, Profession, etc.) are present and used throughout the codebase.

## Phase 1 — Entity and Component Model

1. Entity Registry

   - Use an `cmap<EntityId, Components>` as the central store for entities. This allows for incremental updates when entities are added, removed, or modified.
   - Component set (start with immutable records):
     - Identity: name, tags, faction, profession
     - Stats: base + modifiers
     - Resources: current HP/MP/Stamina
     - Effects: `alist<ActiveEffect>` of applied status effects (buffs/debuffs)
     - Abilities: references to skills/spells
     - Position (optional placeholder for later movement)

2. Component Composition

   - Define Modifiers: additive, multiplicative, override layers.
   - Compose derived stats from base + equipment + effects.

3. Reactive State (FDA)

   - The authoritative state is the minimal, essential data from which all other game information is derived. It is held in a central record for clarity.

     ```fsharp
     type GameState = {
         entities: cmap<EntityId, All> // All components per entity.
         gameTime : cval<int64<ticks>> // Master clock for cooldowns, effects, etc.
         rng : cval<System.Random>      // For deterministic, reproducible simulations.
     }
     ```

     - While the properties of this object are changeable, the record itself is immutable and likely it will be created once at initialization and then mutated in place via FDA transactions.
     - Changeable values will be exposed as adaptive collections or values (`amap`, `alist`, `aval`) for computations and consumers, mutations have to be done via FDA transactions within well established places and clear boundaries.

   - **Why this state?**
     - `entities`: The master collection of all game objects. Using an `cmap` allows for efficient, incremental updates.
     - While entities is a `cmap`, the game components will have to access it as an `amap` for reactive computations. This can be achieved by exposing a derived `amap` view of the `cmap` when needed.
     - `gameTime`: Essential for managing all time-based logic in a real-time game.
     - `rng`: Crucial for determinism. Storing the RNG state ensures that simulations are reproducible, which is vital for debugging.
   - **Derived Data:** All other information is computed from this authoritative state. We can use a companion module to house helper functions for these computations.
     ```fsharp
     module GameState =
         let getAlive (state: GameState) : aset<EntityId> =
             // logic to derive alive entities
             ...
         let getDerivedStats (state: GameState) : amap<EntityId, DerivedStats> =
             // logic to derive stats
             ...
     ```
   - This pattern keeps the core state clean while providing a clear, organized way to access derived views of the world.

Deliverable: Entity registry implemented as `amap<int<EntityId>, All>`. `GameState` record contains all entities, game time, and engine services. All components (base stats, resources, effects, abilities, cooldowns) are managed adaptively. FDA graph wiring is complete.

## Phase 2 — Action/Command System (Real-time)

1. Action Model

   - Action/Command DU:
     - MeleeAttack of { actor; target }
     - CastSpell of { actor; target; spellId }
     - UseItem of { actor; itemId; target }
     - Defend of { actor }
     - Wait of { actor }
   - Validation functions: canPerform action given world.

2. Resolution Pipeline

   - Steps: Validate -> Cost (resource consumption) -> Resolve (damage/heal/apply)
   - Update world state through pure transformations

3. Real-time Action Cooldowns
   - Actions are not turn-based but are limited by timers and cooldowns.
   - Each ability will have a cooldown period.
   - The game loop will update cooldown timers on each tick.
   - An entity can perform an action only if the corresponding cooldown is ready.

Deliverable: End-to-end resolution for MeleeAttack, Fireball, Poison Spell, and other sample abilities. Action validation, cooldowns, resource costs, and status effect restrictions are implemented. All ability execution and validation logic is adaptive and deterministic.

## Phase 3 — Combat Maths and Effects

1. Damage/Healing Formulas

   - Physical: AttackPower vs Armor, with crit chance and evasion
   - Magical: SpellPower vs Resist, with elemental types (Fire, Frost, etc.)
   - Variance via RNG; deterministic when seeded

2. Status Effects Framework

   - Effect kinds: Buff, Debuff, Stun, Silence, Taunt. Kinds that imply a magnitude will have an external formula to calculate damage.
   - Stacking rules: none, refresh, add-stack up to cap
   - Durations:
     - `Instant`
     - `Lapse of float` (duration in ticks/seconds)
     - `Loop of int * float` (e.g., for DoTs/HoTs)
   - Timers: duration in ticks, ticking each update; managed reactively

3. Resources and Costs
   - HP, MP costs; cooldowns per ability
   - FDA: derived cooldown-ready aset of abilities

Deliverable: Full effect framework implemented with the following effect kinds:

- Buff
- Debuff
- DamageOverTime (DoT)
- HealOverTime (HoT)
- Stun
- Silence
- Taunt
  Stacking rules supported:
- NoStack
- RefreshDuration
- AddStack (with max stacks)
  Effect durations:
- Instant
- Timed
- Loop (periodic, with tick logic)
  All effects are managed adaptively and unit-tested. Status effects (Stun, Silence, Taunt) and periodic effects (Poison, Regeneration) are present.

## Phase 4 — Ability/Spell System

1. Data-Driven Definitions

   - Ability record: Id, Name, Profession, `SkillType` (Passive/Active), Targeting, Costs, Effects, `FormulaId` reference if needed.
   - Authoring format (later): JSON/YAML; for now, hard-code some samples

2. Targeting Rules

   - `NoTarget`, `Self`, `SingleTarget` (Enemy, Ally), `MultiTarget of int`, `AoE` (circle/cone/line placeholders)
   - Validation and targeting filters as pure functions

3. Execution
   - Action -> Ability -> Effects -> State Updates

Deliverable: Ability system implemented with the following sample abilities:

- Melee Attack
- Fireball
- No-Stack Spell
- Buff Spell
- Poison Spell
- Regen Spell
- Basic Melee Attack (no cost)
- Silence Spell
  Abilities can have resource costs (HP/MP), cooldowns, targeting types (Self, SingleAlly, SingleEnemy, MultiTarget), and reference formulas or effects. All ability definitions are managed adaptively.

## Phase 4.5 — Enhanced Effects Framework ✅ COMPLETE

**Status**: ✅ **COMPLETE**

1. Enhanced Resolution Simplification ✅
   - Process ability costs, damage calculation, and effect modifiers within a straightforward resolution flow (pre/compute/apply) without hook infrastructure

2. Formula-Based Effect Modifiers ✅
   - Replace static modifiers with dynamic formula references
   - Support for ability damage modification, resource conversion
   - DynamicMod: Formula-based stat modifiers with explicit targeting
   - AbilityDamageMod: Percentage damage boosts from active effects
   - ResourceConversion: HP-cost amplification and MP↔HP conversion

3. Passive Skills System ✅
   - Passive and Active ability definitions with separate types
   - Ability requirements system
   - Permanent duration effects for passive skills

Deliverable: Enhanced effects framework supporting complex, formula-driven interactions. All core effect categories (HP-cost amplification, resource conversion, dynamic modifiers) implemented and tested. System maintains backward compatibility with existing effects while enabling advanced gameplay mechanics.

**See**: `docs/enhanced-effects-framework.md` for detailed implementation.

## Phase 5 — Content and Progression

1. Entities and Archetypes

   - \<character> base kits with starter stats and abilities

2. Loot and Equipment

   - Items with modifiers; rarity affects ranges; equipment affects derived stats

Deliverable: \<character> base kits with starter stats and abilities are present.

## Phase 6 — Minimal Integration with MonoGame

1. Game Loop Hook

   - PomoGame.Update: drive a Core.Update(world, inputs, dtTicks)
   - Inputs placeholder: primitive commands to trigger actions based on player input.

2. Debug Rendering (later)
   - Print logs and derived projections to console or debug overlay

Deliverable: Minimal MonoGame integration present. Game loop hooks and input placeholders allow triggering actions and abilities. Rendering and UI are deferred.

## Current Implementation Status (Updated)

### ✅ COMPLETED - Phase 0-4 (Ready for Phase 4.5)

**Core Foundation:**

- ✅ Domain Types: EntityId, Tick, EffectId, AbilityId with UMX measures
- ✅ Classification System: Faction, Tag, Family (Power/Magic/Sense/Charm), Stage, Profession
- ✅ Base Attributes: Power, Magic, Sense, Charm with derived stat calculations
- ✅ Derived Stats: All 12 stats calculated from base attributes
- ✅ Element System: Fire, Water, Earth, Air, Lightning, Light, Dark, Neutral

**Effects & Abilities:**

- ✅ Effect Framework: Buff, Debuff, DamageOverTime, HealOverTime, Stun, Silence, Taunt
- ✅ Stacking Rules: NoStack, RefreshDuration, AddStack (max stacks)
- ✅ Duration System: Instant, Timed, Loop (periodic, tick logic)
- ✅ Ability System: Resource costs (HP/MP), cooldowns, targeting, formula reference, effect application
- ✅ Stat Modifiers: Additive, Subtractive, Multiplicative, Divisive

**Combat & Resolution:**

- ✅ Physical Combat: AttackPower vs DefensePoints, evasion, crits
- ✅ Magical Combat: MagicAttack with elemental resistance
- ✅ Action Validation: Resource costs, cooldowns, status effect restrictions
- ✅ Status Effects: Stun (blocks all), Silence (blocks spells), Taunt (redirects target)
- ✅ Periodic Effects: Poison (DoT), Regeneration (HoT)

**Formula System:**

- ✅ Formula definitions: Physical+Neutral, Fire+Magical, Magic+Neutral, Fire+Physical
- ✅ Formulas use invoker/target stats and elemental attributes/resistances

**Reactive Architecture:**

- ✅ FDA Integration: All game state, entities, effects, and abilities managed with FSharp.Data.Adaptive collections
- ✅ All derived stats and views computed adaptively

**Testing:**

- ✅ Unit and property tests for effects, abilities, and combat resolution

**Planned:**

- Save/load, equipment system, progression logic, and expanded content are planned for future phases.
- ✅ GameState: Centralized state with entities, time, services
- ✅ Derived Views: Alive entities, ready abilities, derived stats as adaptive projections
- ✅ Services: IAbilityStore, IEffectStore with dependency injection

**Testing & Quality:**

- ✅ Phase 3 Tests: 12 comprehensive scenarios covering all major mechanics
- ✅ Deterministic RNG: Reproducible combat and effect outcomes
- ✅ Property Testing: Ready for FsCheck integration
- ✅ Content Definitions: 7 effects + 7 abilities for testing

### ✅ COMPLETED - Phase 4.5: Enhanced Effects Framework

**Status**: ✅ **COMPLETE** - Ready for Phase 5

**Completed Work**:
- ✅ AbilityDamageMod integration - percentage damage boosts from active effects
- ✅ ResourceConversion processing - HP-cost amplification and MP↔HP conversion
- ✅ Resolution improvements - enhanced applyResourceCost and calculateDamage functions
- ✅ DynamicMod with formula-based effect evaluation - explicit stat targeting with formula-calculated modifiers (supports derived stats: AP, MA, HP, MP, etc.)
- ✅ Passive skills with Permanent duration
- ✅ Ability requirements system

**Phase 4 Goals:**

- Enhanced targeting system (Self, Multi-target, AoE)
- Data-driven ability definitions with formulas
- Elemental damage integration
- Advanced ability mechanics (charges, cast time, interruption)
- Expanded content library

### ✅ COMPLETED - Phase 5: Content and Progression

**Status**: ✅ **COMPLETE** - Ready for Phase 6

**Completed Work**:

1. **Equipment System** ✅
   - Domain types: `Equipment`, `ItemId`, `Rarity`, `ItemStatBonus`, `Slot`
   - Equipment slots: Head, Chest, Legs, Hands, Weapon1, Weapon2, Accessory
   - Equipment integration: Added to `EntityComponents` as `HashMap<Slot, Equipment>`
   - EquipmentStore: 18 equipment items with varied rarities (Common to Legendary)
   - Stat bonuses: Equipment provides bonuses to all 12 derived stats
   - Elemental attributes: Equipment can add elemental damage (Fire, Water, Earth, Air, Lightning, Light, Dark)
   - Elemental resistances: Equipment provides damage reduction against elements
   - Stat application: Equipment bonuses integrated into `applyModifiers` in Gameplay.fs
   - Performance optimization: Single-pass aggregation using mutable HashMaps with `HashMap.alterV`

2. **Character Kits** ✅
   - CharacterKitStore: 12 character base kits covering all Family/Stage combinations
   - Families: Power, Magic, Sense, Charm
   - Stages: First (15 primary stat), Second (22 primary stat), Third (30 primary stat)
   - Each kit includes: Profession, Name, BaseStats, StarterAbilities
   - Thematic naming: Warrior→Fighter→Battle Master (Power), Mage→Sorcerer→Archmage (Magic), Scout→Ranger→Scout Master (Sense), Defender→Guardian→Protector (Charm)

**Testing**:
- ✅ Phase5Tests.fs: 12 tests covering character kit validation
- ✅ All 69 tests passing (including equipment integration tests)

**Deliverables**: Character base kits with starter stats and abilities are present. Equipment system fully implemented with stat modifiers, elemental attributes, and resistances affecting derived stats.

## Phase 5.5 — GameState API Surface & Architecture

**Status**: 📋 **PLANNING** - Not yet implemented

**Goals**:
1. **API Surface Design**: Create clean, ergonomic API for common GameState operations using existing StateChange mechanism
2. **Architecture Analysis**: Clarify composition root location (where GameState.create' should be called)
3. **Command Pattern Alignment**: Analyze existing Domain.Rules.Command vs proposed API operations
4. **Non-Reactive Query Justification**: Document where and why AVal.force is necessary for MonoGame integration
5. **MonoGame Integration Readiness**: Prepare core for seamless runtime integration

**Key Architectural Decisions**:

1. **StateChange Integration**: All API operations leverage existing `StateChange` mechanism from Resolution.apply
   - Command-based operations (abilities, time) return `aval<StateChange>` using Resolution.step
   - Direct operations (equipment, resources, effects) return `Result<StateChange, Error>`
   - Query operations use AVal.force only when necessary (see justification table)

2. **Composition Root Location**: Service composition and GameState initialization in **Pomo.Core**
   - GameState type and create' function remain in **Pomo.Lib** (domain layer)
   - Service implementations and composition root in **Pomo.Core/PomoGame.Initialize()** (application layer)
   - Maintains clean dependency flow and allows framework-agnostic Pomo.Lib

3. **Command Pattern Distinction**:
   - Existing `Domain.Rules.Command` (UseAbility) for complex resolution logic
   - GameStateOperations API for high-level state manipulation
   - Complementary patterns serving different purposes

4. **Non-Reactive Queries**: Justified for MonoGame's pull-based game loop
   - `activateAbility`, `advanceTime`: Must force aval<StateChange> for immediate application
   - `getDerivedStatsSnapshot`: Must force adaptive stat computation for UI display
   - `getEntity`, `getAliveEntities`: Can use direct cmap/amap access (no force needed)
   - Query code lives in **Pomo.Core** to isolate forcing from reactive Pomo.Lib

**Planned Operations**:
- **Entity Management**: createEntity, removeEntity, getEntity → StateChange or direct access
- **Equipment Operations**: equipItem, unequipItem, swapEquipment → Result<StateChange, Error>
- **Ability Operations**: activateAbility → aval<StateChange>, learnAbility, forgetAbility → Result<StateChange, Error>
- **Profession Advancement**: advanceStage → Result<StateChange, Error>, canAdvanceStage → bool query
- **Resource Management**: healEntity, restoreMP, damageEntity, setResourceStatus → Result<StateChange, Error>
- **Effect Management**: applyEffect, removeEffect, clearAllEffects → Result<StateChange, Error>
- **Time & Simulation**: advanceTime → aval<StateChange>, resetCooldowns → Result<StateChange, Error>
- **Query Operations**: getAliveEntities, getReadyAbilities, getDerivedStatsSnapshot → Non-reactive snapshots

**Deliverables**:
- [ ] `GameStateOperations.fs` module with all API functions (Pomo.Lib)
- [ ] `GameStateQueries.fs` module for non-reactive queries (Pomo.Core)
- [ ] StateChange application helpers
- [ ] Unit tests for each operation
- [ ] Integration tests demonstrating MonoGame-like usage patterns
- [x] Design documentation (`docs/Phase-5.5-API-Surface.md`)

**See**: `docs/Phase-5.5-API-Surface.md` for comprehensive design, justifications, and implementation guidance.

## Phase 5.6 — Database Migration & Content System

**Status**: 📋 **PLANNING** - Optional future enhancement

**Goals**:
1. **SQLite Schema Design**: Migrate hardcoded Content.fs to relational database
2. **Dynamic Content Loading**: Enable runtime content updates without recompilation
3. **Content Authoring Tools**: Support modding and designer-friendly workflows
4. **Formula Integration**: Store and validate formulas in database with FormulaParser.fs

**Planned Components**:

**Database Schema**:
- Effects, EffectModifiers, Abilities, AbilityEffects, AbilityRequirements
- Formulas (with text-based formula definitions)
- Equipment, EquipmentStatBonuses, EquipmentElementalAttributes, EquipmentElementalResistances
- CharacterKits, CharacterKitAbilities
- Schema versioning and content versioning tables

**Migration Strategy**:
1. Create SQLite schema with proper constraints and indexes
2. Generate seed data from existing Content.fs definitions
3. Implement ContentDatabase class for loading data
4. Create Database-backed IEffectStore, IAbilityStore, IFormulaStore
5. Build content editing CLI tools
6. Support dual-mode (hardcoded vs database) during transition

**Formula Integration**:
- Store formulas as text in Formulas table
- Use existing FormulaParser.fs to parse at load time
- Cache parsed expressions for performance
- Validate all formulas during database seeding

**Complexity Analysis**:
- Estimated effort: ~56-82 hours (7-10 days)
- Risk: Medium (schema migrations, performance if not cached)
- **Recommendation**: Defer to post-Phase 6

**Deliverables**:
- [ ] `schema.sql` - Complete SQLite database schema
- [ ] `GenerateSeedData.fsx` - Script to generate seed data from Content.fs
- [ ] `ContentDatabase.fs` - Database access layer
- [ ] `DatabaseStores.fs` - Database-backed store implementations
- [ ] `ContentEditor.fsx` - CLI tools for content editing
- [ ] Unit tests for database operations
- [x] Design documentation (`docs/Phase-5.6-Database-Migration.md`)

**See**: `docs/Phase-5.6-Database-Migration.md` for detailed implementation plan.

**Note**: This phase is **optional** and can be deferred. The current `Content.fs` hardcoded approach is sufficient for Phase 6 (MonoGame integration). Consider implementing only if content volume grows significantly or modding support becomes a priority.

## Phase 6 — Minimal Integration with MonoGame

**Status**: 🎯 **CURRENT** - Ready to begin

1. Game Loop Hook

   - PomoGame.Update: drive a Core.Update(world, inputs, dtTicks)
   - Inputs placeholder: primitive commands to trigger actions based on player input.

2. Debug Rendering (later)
   - Print logs and derived projections to console or debug overlay

Deliverable: Minimal MonoGame integration present. Game loop hooks and input placeholders allow triggering actions and abilities. Rendering and UI are deferred.

## Reactive Core Sketch (F# + FDA)

```fsharp
open FSharp.Data.Adaptive
open System.Collections.Immutable

// Authoritative world state is held in a single record of adaptive values
type GameState = {
    entities : amap<EntityId, Components>
    gameTime : cval<int64>
    rng : cval<System.Random>
}

// Types
type EntityId = EntityId of int
// ... (other types like Stats, Resources, etc.)

module GameState =
    let create () =
        { entities = cmap []
          gameTime = cval 0L<ticks>
          rng = cval (System.Random 42) }

    let aAlive (state: GameState) : aset<EntityId> =
      state.entities
      |> AMap.filter(fun _ c -> c.Resources.Status = Attributes.Status.Alive)
      |> AMap.toASet
      |> ASet.map(fun (id, _) -> id)

    let aDerived (state: GameState) : amap<EntityId, Derived> =
      state.entities |> AMap.map (fun id c ->
          // ... stat calculation logic
          let attack = c.BaseStats.Strength * 2
          let spell = c.BaseStats.Intellect * 2
          // ... etc
          { attack = attack; spell = spell; armor = 0; resist = 0; crit = 0.0; evasion = 0.0 })

// Command application (pure function)
// ...

// Mutating the reactive world (boundary)
let apply (state: GameState) (cmd:Command) =
  transact (fun _ ->
    let currentEntities = state.entities.GetValue()
    let entityChanges = step currentEntities cmd

    // Apply the changeset to the amap
    state.entities.Modify(fun m -> Map.fold (fun s k v -> s.SetItem(k, v.Value)) m entityChanges)) // Simplified
```

## Milestones Checklist

- ✅ Phase 0: Types and RNG
- ✅ Phase 1: Entity store + FDA projections
- ✅ Phase 2: Commands + resolution (MeleeAttack + simple spell)
- ✅ Phase 3: Combat maths and effects (formulas, status framework, resources/costs)
- ✅ Phase 3.5: Architectural Refinement (DI)
- ✅ Phase 4: Enhanced Ability/Spell System
- ✅ Phase 4.5: Enhanced Effects Framework
- ✅ Phase 5: Content and Progression (Equipment + Character Kits)
- 📋 Phase 5.5: GameState API Surface & Architecture (Planning complete)
- 📋 Phase 5.6: Database Migration & Content System (Planning complete, optional)
- 🎯 **CURRENT**: Phase 6 - MonoGame Integration

## How to Integrate Into PomoGame (later)

- **Composition Root:** In `PomoGame.Initialize`, create concrete services (e.g., `ConsoleLogger`, `JsonContentService`) and compose the `engineServices` handle.
- **Initialize State:** Create the initial `worldState` record.
- **Game Loop:** In `PomoGame.Update`, translate user input into `Commands` and pass the `services` handle and `worldState` to the `apply` function.
- **Debug View:** For now, log state changes and derived projections to the console to verify flows.

## Testing Strategy

- **Unit tests with Fakes:** For core logic modules (e.g., `ActionResolver`), use simple, in-memory "fake" implementations of the `EngineServices` interfaces (e.g., a `FakeContentService` backed by a `Map`). This allows testing logic in isolation, such as verifying that a spell fails correctly when the fake service is configured to not find it.
- **Property-based tests:** Use for stat composition and effect stacking rules to ensure they are mathematically sound across a wide range of inputs.
- **Deterministic simulations:** Use a fixed-seed implementation of the RNG service to test complex, multi-turn scenarios and ensure the simulation is perfectly reproducible.
- **Integration/Scenario tests:** Given a real set of services, an initial world state, and a sequence of commands, assert the final state is as expected.

## Future Extensions (post-core)

- Rendering & UI with reactive bindings to FDA projections
- Pathfinding and tactical grid
- AI behavior trees reading derived views
- Multiplayer lockstep or rollback using command logs

— End of plan —
