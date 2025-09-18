# RPG Core Refactoring Plan: Stat-Scaling Effects

This document outlines the plan to refactor the core combat system to allow periodic effects, such as Damage-over-Time (DoT) and Heal-over-Time (HoT), to scale with the caster's stats at the moment of application.

## 1. Goal

The current system defines the power of periodic effects as a static `int` (e.g., `DamageOverTime of 5`). The goal is to make this dynamic, so an effect's potency is calculated based on the caster's `SpellPower` (or other stats) when the effect is applied.

For example, a "Poison" spell should deal more damage per tick when cast by a high-Intellect character than when cast by a low-Intellect one.

## 2. Proposed Changes

This refactoring will primarily touch two areas: the domain model where effects are defined (`Domain.fs`) and the gameplay logic where effects are processed (`Gameplay.fs`).

### Step 1: Evolve the Domain Model in `Pomo.Lib/Domain.fs`

The `EffectKind` type needs to be updated to store a scaling formula instead of a fixed value.

1.  **Create a new `EffectPower` type:** This record will define the base power of an effect and its scaling factor.

    ```fsharp
    module Effects =
      type EffectPower = {
        Base: int
        SpellPowerRatio: float
      }
      // ...
    ```

2.  **Update `EffectKind`:** Modify `DamageOverTime` and `HealOverTime` to use the new `EffectPower` type.

    ```fsharp
    // ...
    type EffectKind =
      | Buff
      | Debuff
      | DamageOverTime of EffectPower
      | HealOverTime of EffectPower
      | Stun
      | Silence
      | Taunt
      | Shield of int
    // ...
    ```

### Step 2: Refactor Effect Processing Logic in `Pomo.Lib/Gameplay.fs`

The logic for processing effect ticks needs access to the stats of the effect's original caster. The current `StatusEffects.tickEffects` function is too isolated for this. The logic must be moved into the main `Gameplay.tick` function.

1.  **Access Derived Stats:** The `Gameplay.tick` function already has access to the `derivedStatsMap` for all entities.

2.  **Calculate Scaled Power during Tick:** When processing an active effect, the logic will:
    a. Look up the effect's definition from the `effectStore`.
    b. Get the `SourceId` from the active effect to identify the original caster.
    c. Look up the caster's `DerivedStats` from the `derivedStatsMap`.
    d. Calculate the final tick damage/healing using the `EffectPower` formula:
    `final_power = power.Base + (caster_spell_power * power.SpellPowerRatio)`

### Example Implementation Sketch for `Gameplay.fs`

```fsharp
// Inside the Gameplay.tick function...
// ... existing logic to get derivedStatsMap ...

// This logic would replace the isolated call to StatusEffects.tickEffects
let! tickResult, updatedEffects, generatedEvents = adaptive {
  let! effects = components.Effects |> AList.toAVal
  let mutable totalDamage = 0
  let mutable totalHealing = 0
  // ...

  for effect in effects do
    // ... check if effect should tick ...
    let effectDef = state.services.effectStore.find effect.EffectId
    let sourceStats = derivedStatsMap.[effect.SourceId] // Get original caster's stats

    match effectDef.Kind with
    | Effects.EffectKind.DamageOverTime power ->
      let scaledDamage =
        power.Base + int (float sourceStats.SpellPower * power.SpellPowerRatio)
      totalDamage <- totalDamage + scaledDamage

    | Effects.EffectKind.HealOverTime power ->
      let scaledHealing =
        power.Base + int (float sourceStats.SpellPower * power.SpellPowerRatio)
      totalHealing <- totalHealing + scaledHealing

    | _ -> ()
  // ... rest of the logic
}
```

## 3. Impact on Content

After this change, all existing effect definitions in `Content` that use `DamageOverTime` or `HealOverTime` will need to be updated to use the new `EffectPower` record. For example, a static value of `5` would become `{ Base = 5; SpellPowerRatio = 0.0 }`.

This refactoring will provide a much more flexible and dynamic system for creating interesting and scalable abilities.

## 4. Architectural Evolution: Unifying All Actions as Effects

The changes above pave the way for a more profound architectural simplification: treating **all** combat results, including initial damage and healing, as effects.

### The Principle

**An ability itself does nothing. It is simply a container for one or more effects. The effects are what change the game state.**

This moves the system from having special-cased logic for initial impact (in `resolveMeleeAttack` and `resolveCastSpell`) to a fully unified, data-driven model.

### How It Would Work

1.  **Introduce Instant Effects:** The `EffectKind` DU in `Domain.fs` would be expanded to include `Instant` effects.

    ```fsharp
    type EffectKind =
      // New kinds for instant effects
      | Damage of EffectPower
      | Heal of EffectPower

      // Existing kinds for periodic/lasting effects
      | DamageOverTime of EffectPower
      | HealOverTime of EffectPower
      // ...etc.
    ```

2.  **Abilities as Effect Collections:** An `AbilityDefinition` becomes a simple list of effects.

    - **Simple Melee Attack:** An ability with one effect: `Damage` with `Duration = Instant`, scaling with `AttackPower`.
    - **Fireball Spell:** An ability with two effects:
      1.  `Damage` with `Duration = Instant` (the initial explosion).
      2.  `DamageOverTime` (the lingering burn).
    - **Pure Healing Spell:** An ability with one effect: `Heal` with `Duration = Instant`.

3.  **Simplified Resolution Functions:** The `resolveMeleeAttack` and `resolveCastSpell` functions would become almost identical. Their sole responsibility would be to iterate through an ability's effects and dispatch them to a unified "effect processor," which would handle all calculations and state changes.

### Benefits of This Approach

- **Unification:** Eliminates the distinction between "initial damage" and "other effects." All state changes follow the same logic path.
- **Flexibility:** Creating complex abilities (e.g., a sword that damages, stuns, and applies a bleed) requires no new code, only new data definitions.
- **Data-Driven Design:** The "what" of an ability is moved entirely into data (effect definitions), while the code only contains the "how" (the processing logic). This is ideal for long-term maintenance and content creation.
