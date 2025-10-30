# Implementation Plan: Advanced Ability System

This document outlines a phased implementation plan to enhance the ability system, enabling the creation of complex, dynamic abilities as described in issue #27. It will serve as a reference for development.

## 1. Overview & Goals

The primary goal is to implement a set of core features that, when combined, will support the abilities detailed below. The implementation is broken into phased, incremental steps, ordered by complexity and dependency.

## 2. Target Ability Descriptions

The following abilities are the primary drivers for this implementation plan.

- **Deadly Swamp:** An Earth and MA (Magic Attack) powered skill. The user enters a targeting mode to select a circular area. Upon selection, initial damage is dealt to enemies within the area. The area persists for a duration, applying residual damage over time and slowing any enemies within it.

- **Meteor Shower:** An area-of-effect attack. The user selects a circular area. After a pre-activation animation completes, the ability randomly selects up to 5 targets within the area and strikes them.

- **Mermaid's Song:** A multi-target chaining attack. The user selects an initial enemy. The ability then "chains" to the nearest subsequent enemy within a specific range, repeating this for a maximum number of chains. The effects are applied after a pre-activation animation finishes.

- **Fan of Stones:** A multi-target cone attack. The user selects a primary target. A cone is formed based on the caster's position and the target's position. Up to a maximum number of enemies within that cone are randomly selected and struck by individual, separate projectiles.

- **Raining Icicles:** A multi-projectile area attack. The user selects a circular area. A random number of icicles begin to fall at random points within the area. Damage is calculated for an enemy only if it is at an icicle's precise impact point when it lands.

- **Dash:** The user activates the ability and then accelerates N times its base speed for a fraction of a second in the direction the user is moving, leaving behind a trail-like animation.

- **Seeker Punch:** The user enters into targeting mode. When an enemy is selected, a "rush" animation shows a trail-like animation behind the caster. Once the caster arrives at the target, it shows the animation of the target being hit and then applies damage through normal damage calculation means.

## 3. Core Feature Breakdown

To support the target abilities, the following core engine features are required:

1.  **Ability Activation Phases:** Introduce an optional `CastingTime` during which the user is locked in an animation before an ability activates. This also includes a `PreActivationVisualEffect` that can play before the ability's main effects are triggered.
2.  **Enhanced Visual Effect Dispatch:** Enable a single ability use to generate multiple, independent visual effects, such as spawning several projectiles for different targets simultaneously.
3.  **Advanced Targeting Mechanisms:** Extend the `TargetType` system to support more complex target selection logic beyond single-target and simple AoE.
4.  **Persistent Ground Effects:** Create lingering area effects on the battlefield that apply effects to any entity that enters or remains within them.
5.  **Centralized Movement Stat:** Promote movement speed to a first-class derived stat, allowing it to be modified by effects and equipment.
6.  **Consolidated Active Objects:** Refactor the game state to use a single collection for all dynamic, temporary objects (projectiles, AoEs, etc.), including new ability-driven movement states.

## 4. Phased Implementation Plan

### Phase 1: Foundational Activation Enhancements [COMPLETED]

This phase introduces the concepts of casting time and pre-activation visuals, which are prerequisites for many of the desired abilities.

- **Task 1.1: Update Domain Model [COMPLETED]**

  - In `Pomo.Lib/Domain.fs`, modify the `ActiveAbilityDefinition` record to include two new optional fields:
    - `CastingTime: TimeSpan voption`
    - `PreActivationVisualEffectId: int<VisualEffectId> voption` (A new measure type `VisualEffectId` may be needed).

- **Task 1.2: Implement Casting Time Logic [COMPLETED]**

  - Modify the ability execution logic to handle `CastingTime`. When an ability with a casting time is used, the actor should enter a "casting" state.
  - The ability's primary effects (damage, effect application) should be delayed until the casting time completes. This can be managed by creating a `PendingResolution` with a `TriggerTick` set to `gameTime + castingTime`.

- **Task 1.3: Implement Pre-Activation Visuals [COMPLETED]**
  - Extend the logic from 1.2. If `PreActivationVisualEffectId` is present, the system should dispatch this visual effect when the casting phase begins.

### Phase 2: Advanced Targeting Mechanisms [COMPLETED]

This phase implements the specific target selection logic required by the new abilities. These can be worked on in parallel, but are ordered here by estimated complexity.

- **Task 2.1: Update `TargetType` Domain [COMPLETED]**

  - In `Pomo.Lib/Domain.fs`, replace the generic `MultiTarget of int` with specific, descriptive cases:
    ```fsharp
    type TargetType =
      | Self
      | SingleAlly
      | SingleEnemy
      | GroundTarget of radius: float32
      // New Types
      | AreaRandomTargets of radius: float32 * maxTargets: int
      | ChainTargets of maxChains: int * chainRange: float32
      | ConeTargets of angle: float32 * range: float32 * maxTargets: int
    ```

- **Task 2.2: Implement `AreaRandomTargets` [COMPLETED]**

  - **Required for:** _Meteor Shower_
  - Implement the selection logic: find all valid entities within the target area and randomly select up to `maxTargets`.

- **Task 2.3: Implement `ConeTargets` [COMPLETED]**

  - **Required for:** _Fan of Stones_
  - Implement the geometric selection logic: from a central point, calculate a cone based on the caster's facing direction and select random targets within it.

- **Task 2.4: Implement `ChainTargets` [COMPLETED]**

  - **Required for:** _Mermaid's Song_
  - Implement the iterative selection logic: starting from the initial target, find the next closest valid entity within `chainRange`, and repeat up to `maxChains`.

- **Task 2.5: Incorporate Engagement Rules [COMPLETED]**
  - **Required for:** All advanced targeting mechanisms.
  - Define and implement rules for determining valid targets based on factors such as friendly/hostile status, line of sight, aggro range, and other combat-specific conditions. These rules should be integrated into the `TargetResolution` functions to filter potential targets.

### Phase 3: Enhanced Visual Effect Dispatch [COMPLETED]

This phase enables abilities to feel more dynamic by spawning multiple projectiles or impacts from a single cast.

- **Task 3.1: Refactor Ability Processing [COMPLETED]**
  - **Required for:** _Fan of Stones_, _Raining Icicles_
  - The core ability processor needs to be updated. After target selection (Phase 2) returns a list of targets or points, the processor should iterate through them and be capable of generating a separate `VisualEffectChange` (e.g., `AddProjectile`) for each one.

- **Task 3.2: Extend Targeting for Position-Based Effects [COMPLETED]**
  - **Required for:** _Raining Icicles_
  - To support abilities that target random points in an area, the `TargetType` DU was extended with `AreaRandomPoints`.
  - The `resolveUseAbility` function was refactored to handle both entity and position-based targets, using a new `ResolvedTarget` DU. This allows abilities to generate visual effects at specific points on the ground, not just on entities.

- **Task 3.3: Stabilize and Refactor [COMPLETED]**
  - **Required for:** Overall system stability.
  - Fixed inconsistencies in `Pomo.Lib/EnemyAI.fs` where the `TargetType` DU was not handled correctly.
  - Corrected `voption` handling in `Pomo.Lib/Gameplay.fs` for `PendingResolutionId` to prevent runtime errors.
  - Refactored `selectAbilityForTarget` in `Pomo.Lib/EnemyAI.fs` to correctly gather all valid abilities for an AI entity.

### Phase 4: Persistent Ground Effects

This is the most complex feature, introducing long-lived stateful objects to the battlefield.

- **Task 4.1: Design `ActiveZone`**

  - **Required for:** _Deadly Swamp_
  - In `Domain.fs`, define a new type, `ActiveZone`, to represent a lingering AoE. It should contain:
    - `Id: Guid<ActiveZoneId>`
    - `Position`, `Shape`, `Radius`
    - `EndTime: TimeSpan`
    - `EffectsToApply: int<EffectId>[]`

- **Task 4.2: Update Game State**

  - Add a new `activeZones: cmap<Guid<ActiveZoneId>, ActiveZone>` to the `ScenarioState`.
  - An ability can now create an `ActiveZone` as one of its effects.

- **Task 4.3: Implement Zone Collision/Effect System**
  - Create a new system that runs each game tick. This system will iterate through all `activeZones` and all `entities` to check for collisions.
  - When an entity enters a zone, the zone's effects are applied.

### Phase 5: Centralize Movement Speed as a Derived Stat [COMPLETED]

This is a foundational change to make movement speed a proper stat, enabling it to be modified by the effects system. This simplifies the implementation of speed-related abilities like `Dash`.

- **Task 5.1: Update Domain (`Pomo.Lib/Domain.fs`) [COMPLETED]**

  - Add `MovementSpeed` to the `Stat` discriminated union.
  - Movement speed is a derived stat only (not a base attribute), hardcoded to 100 in `DerivedStats.applyModifiers`.
  - Remove the `Speed: float32` field from the `Movement` component record.

- **Task 5.2: Update Stat Calculation (`Pomo.Lib/Gameplay.fs`) [COMPLETED]**

  - In `DerivedStats.applyModifiers`, initialize `MovementSpeed` with hardcoded value 100.
  - Ensure `MovementSpeed` is correctly modified by `StaticMod` and `DynamicMod` effects, just like any other stat.

- **Task 5.3: Refactor `GameState.runTickEffects` (`Pomo.Lib/Gameplay.fs`) [COMPLETED]**

  - Changed the order of operations within the tick:
    1.  Calculate `DerivedStats` for the entity based on its state _before_ movement.
    2.  Pass the calculated `derivedStats.MovementSpeed` to the movement update function.
    3.  Execute the movement update.
    4.  Apply any resource changes (from DoTs/HoTs) using the stats calculated in step 1.

- **Task 5.4: Update Movement Logic (`Pomo.Lib/Movement.fs`) [COMPLETED]**
  - Updated the signature of `Update.withPath` to accept `movementSpeed: float32` as a parameter instead of reading it from the `Movement` component.
  - Removed the `applyDexterityModifier` function, as speed is now fully data-driven by the `MovementSpeed` stat.

### Phase 6: Consolidated Active Objects & Movement Abilities

This phase refactors how temporary state objects are managed and uses this new architecture to implement movement-based abilities.

- **Task 6.1: Consolidate Active Objects (`Pomo.Lib/Domain.fs`)**

  - **Required for:** _Seeker Punch_, _Dash_, and future extensibility.
  - Define a new discriminated union `ActiveObject` to hold all temporary, dynamic game objects.
    ```fsharp
    type ActiveObject =
      | Projectile of VisualEffects.ActiveProjectile
      | Aoe of VisualEffects.ActiveAoe
      | Impact of VisualEffects.ActiveImpact
      | FloatingText of VisualEffects.FloatingText
      | PendingResolution of PendingResolution
      // New movement states
      | Rush of ActiveRush
      | Dash of ActiveDash
    ```
  - In `ScenarioState`, replace the individual `cmaps` for `projectiles`, `aoes`, `impacts`, `floatingTexts`, and `pendingResolutions` with a single collection: `activeObjects: cmap<Guid, ActiveObject>`.
  - Refactor the `VisualEffectChange` DU to `AddObject`, `UpdateObject`, and `RemoveObject` to operate on the new consolidated collection. This will require updating all systems that generate these changes.

- **Task 6.2: Implement Active Movement States (`Pomo.Lib/Domain.fs`)**

  - **Required for:** _Seeker Punch_, _Dash_
  - Define the `ActiveRush` and `ActiveDash` records to be used within the `ActiveObject` DU.
    ```fsharp
    type ActiveRush = { Id: Guid; ActorId: Guid<EntityId>; TargetId: Guid<EntityId>; OnArrivalAbilityId: int<AbilityId>; Speed: float32; CreationTick: TimeSpan }
    type ActiveDash = { Id: Guid; ActorId: Guid<EntityId>; Velocity: Position; Duration: TimeSpan; CreationTick: TimeSpan }
    ```

- **Task 6.3: Update `GameState.tick` (`Pomo.Lib/Gameplay.fs`)**

  - **Required for:** All active objects.
  - The `tick` function will be refactored to iterate over the single `activeObjects` map. It will branch on the `ActiveObject` case to delegate to the appropriate resolution logic (e.g., `Projectile.resolve`, `Rush.resolve`).
  - New resolver modules (`Rush.resolve`, `Dash.resolve`) will be created. The `Rush` logic will move the actor and resolve the `onArrivalAbility`. The `Dash` logic will move the actor based on its velocity for the specified duration, bypassing normal movement speed rules.

- **Task 6.4: Update Command Handler (`Pomo.Lib/CommandHandler.fs`)**

  - **Required for:** _Seeker Punch_, _Dash_
  - Update `resolveUseAbility` to create an `ActiveObject.Rush` or `ActiveObject.Dash` and return an `AddObject` change in the resulting `StateChange`.

- **Task 6.5: Implement Trail Effects**
  - **Required for:** _Dash_, _Seeker Punch_
  - A new `EffectKind` will be introduced: `Trail of visualEffectId: int<VisualEffectId> * spawnInterval: TimeSpan`. The `StatusEffects.tickEffects` function will be updated to handle this, periodically generating `AddObject` changes for the trail visuals.

## 5. Ability Implementation Roadmap

- **Mermaid's Song:** Implementable after **Phase 2.4** and **Phase 2.5**.
- **Meteor Shower:** Implementable after **Phase 2.2** and **Phase 2.5**.
- **Fan of Stones:** Implementable after **Phase 3.1** and **Phase 2.5**.
- **Raining Icicles:** Implementable after **Phase 3.1** and **Phase 2.5**.
- **Deadly Swamp:** Implementable after **Phase 4.3** and **Phase 2.5**.
- **Dash:** Implementable after **Phase 6.5**.
- **Seeker Punch:** Implementable after **Phase 6.5**.
