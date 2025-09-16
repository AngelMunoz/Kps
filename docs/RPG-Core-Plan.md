# RPG Core Implementation Plan (F# + MonoGame, Reactive with FSharp.Data.Adaptive)

This document sketches a step-by-step, incremental roadmap to build the core of an RPG focused on magic and physical prowess. We intentionally defer rendering, UI, audio, networking, and platform specifics until the core simulation is solid. We will adopt a reactive core using FSharp.Data.Adaptive (FDA) to model evolving state as incremental data flows.

Good luck to us — and let’s move methodically.

## Guiding Principles

- Core-first: mechanics, rules, and data before visuals.
- Deterministic and testable: pure logic and reproducible simulations.
- Small vertical slices: integrate the smallest end-to-end interactions early.
- Reactive state graph: use FDA to derive views and system inputs from authoritative state.
- Extensibility: design for adding stats, effects, skills, and content over time.
- Performant Reactive Core: Use incremental adaptive collections (`amap`, `aset`, `alist`) for dynamic state to enable efficient, fine-grained updates instead of replacing large collections. For static or slowly changing data, BCL collections are suitable.
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
     - `BaseAttributes` record: Strength, Agility, Intellect, Vitality, Willpower, Luck
     - `DerivedStats` record: MaxHP, MaxMP, AttackPower, SpellPower, Armor, Evasion, CritChance, Resistances (`Map<Element, float>`)
   - Resources (`Resources` record):
     - HP, MP, Stamina
     - `Status` flag: Alive, Dead, Disabled
   - Inventory and Equipment slots (`Slot` type):
     - Head, Chest, Legs, Hands, Weapon1, Weapon2, Accessory

2. Rules and Units

   - Establish canonical units: time in ticks, distances in tiles/meters if needed later.
   - RNG abstraction for deterministic simulation and testability.

3. Project Scaffolding
   - Create Core namespaces: Pomo.Core.Gameplay, Pomo.Core.Rules, Pomo.Core.Content
   - Add test project (future): property tests for stat and effect composition.

Deliverable: types with minimal constructors; no gameplay loop yet.

## Phase 1 — Entity and Component Model

1. Entity Registry

   - Use an `amap<EntityId, Components>` as the central store for entities. This allows for incremental updates when entities are added, removed, or modified.
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
         gameEvents: clist<GameEvent>  // Events emitted by resolutions.
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
     - `rng`: Crucial for determinism. Storing the RNG state ensures that simulations are reproducible, which is vital for debugging and replays.
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

Deliverable: Entity `amap` + `GameState` record + basic FDA graph wiring.

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

   - Steps: Validate -> Cost (resource consumption) -> Resolve (damage/heal/apply) -> Events
   - Produce an array of `GameEvent` values:
     - DamageApplied, Healed, ResourceChanged, EffectApplied, EffectExpired, EntityDied

3. Real-time Action Cooldowns
   - Actions are not turn-based but are limited by timers and cooldowns.
   - Each ability will have a cooldown period.
   - The game loop will update cooldown timers on each tick.
   - An entity can perform an action only if the corresponding cooldown is ready.

Deliverable: end-to-end resolution for MeleeAttack and a simple spell.

## Phase 3 — Combat Maths and Effects

1. Damage/Healing Formulas

   - Physical: AttackPower vs Armor, with crit chance and evasion
   - Magical: SpellPower vs Resist, with elemental types (Fire, Frost, etc.)
   - Variance via RNG; deterministic when seeded

2. Status Effects Framework

   - Effect kinds: Buff, Debuff, Stun, Silence, Taunt. Kinds that imply a magnitude will carry data directly (e.g., `DamageOverTime of int`, `HealOverTime of int`, `Shield of int`).
   - Stacking rules: none, refresh, add-stack up to cap
   - Durations:
     - `Instant`
     - `Lapse of float` (duration in ticks/seconds)
     - `Loop of int * float` (e.g., for DoTs/HoTs)
   - Timers: duration in ticks, ticking each update; managed reactively

3. Resources and Costs
   - HP, MP, Stamina costs; cooldowns per ability
   - FDA: derived cooldown-ready aset of abilities

Deliverable: two or three effects implemented and unit-tested.

## Phase 4 — Ability/Spell System

1. Data-Driven Definitions

   - Ability record: Id, Name, Profession, `SkillType` (Passive/Active), School (Physical, Arcane, etc.), Targeting, Costs, Base Power, Coefficients, Effects, `Modifier` (elemental type)
   - Authoring format (later): JSON/YAML; for now, hard-code some samples

2. Targeting Rules

   - `NoTarget`, `Self`, `SingleTarget` (Enemy, Ally), `MultiTarget of int`, `AoE` (circle/cone/line placeholders)
   - Validation and targeting filters as pure functions

3. Execution
   - Action -> Ability -> Effects -> Events

Deliverable: a handful of abilities across physical and magic themes.

## Phase 5 — Save/Load and Determinism

1. Serializable World Snapshot

   - Encode World and RNG state; versioned with schema evolution in mind

2. Replay Mechanism
   - From an initial world state (seed) and a log of commands, the simulation can be re-run to reproduce a game session exactly. This is critical for debugging.

Deliverable: snapshot save/load for core state.

## Phase 6 — Content and Progression

1. Entities and Archetypes

   - Warrior, Rogue, Mage base kits with starter stats and abilities

2. Loot and Equipment

   - Items with modifiers; rarity affects ranges; equipment affects derived stats

3. Progression Curves
   - XP, level-up, stat points, skill unlocks.
   - Profession system: Characters can `Promote` to the next `Stage` within their `Family` (e.g., Fire Mage I -> Fire Mage II), unlocking new abilities.
   - Skill points can be used to acquire new skills that match the character's `Profession` and `Stage`.

Deliverable: simple loop: fight -> reward -> progress.

## Phase 7 — Minimal Integration with MonoGame

1. Game Loop Hook

   - PomoGame.Update: drive a Core.Update(world, inputs, dtTicks)
   - Inputs placeholder: primitive commands to trigger actions based on player input.

2. Debug Rendering (later)
   - Print logs and derived projections to console or debug overlay

Deliverable: play an action or two through keyboard choices; rendering minimal.

## Reactive Core Sketch (F# + FDA)

```fsharp
open FSharp.Data.Adaptive
open System.Collections.Immutable

// Authoritative world state is held in a single record of adaptive values
type GameState = {
    entities : amap<EntityId, Components>
    gameEvents : alist<Event>
    gameTime : cval<int64>
    rng : cval<System.Random>
}

// Types
type EntityId = EntityId of int
// ... (other types like Stats, Resources, etc.)

module GameState =
    let create () =
        { entities = cmap []
          gameEvents = clist []
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
    let entityChanges, newEvents = step currentEntities cmd

    // Apply the changeset to the amap
    state.entities.Modify(fun m -> Map.fold (fun s k v -> s.SetItem(k, v.Value)) m entityChanges) // Simplified

    // Add new events to the alist
    state.gameEvents.AddMany(newEvents))
```

## Minimal Milestones Checklist
- Phase 0: Types and RNG ✓
- Phase 1: Entity store + FDA projections ✓
- Phase 2: Commands + resolution + events (MeleeAttack + simple spell) ✓
- Phase 3: Combat maths and effects (formulas, status framework, resources/costs) ✓
- Phase 3.5: Architectural Refinement (DI)

## How to Integrate Into PomoGame (later)

- **Composition Root:** In `PomoGame.Initialize`, create concrete services (e.g., `ConsoleLogger`, `JsonContentService`) and compose the `engineServices` handle.
- **Initialize State:** Create the initial `worldState` record.
- **Game Loop:** In `PomoGame.Update`, translate user input into `Commands` and pass the `services` handle and `worldState` to the `apply` function.
- **Debug View:** For now, log events from the `gameEvents` list to the console to verify flows.

## Testing Strategy

- **Unit tests with Fakes:** For core logic modules (e.g., `ActionResolver`), use simple, in-memory "fake" implementations of the `EngineServices` interfaces (e.g., a `FakeContentService` backed by a `Map`). This allows testing logic in isolation, such as verifying that a spell fails correctly when the fake service is configured to not find it.
- **Property-based tests:** Use for stat composition and effect stacking rules to ensure they are mathematically sound across a wide range of inputs.
- **Deterministic simulations:** Use a fixed-seed implementation of the RNG service to test complex, multi-turn scenarios and ensure the simulation is perfectly reproducible.
- **Integration/Scenario tests:** Given a real set of services, an initial world state, and a sequence of commands, assert the final state and emitted events are as expected.

## Future Extensions (post-core)

- Rendering & UI with reactive bindings to FDA projections
- Pathfinding and tactical grid
- AI behavior trees reading derived views
- Multiplayer lockstep or rollback using command logs

— End of plan —
