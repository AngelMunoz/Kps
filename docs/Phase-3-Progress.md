# Phase 3 Progress

This document tracks the progress of Phase 3 implementation based on the `RPG-Core-Plan.md`.

## Implemented Features

- [x] **Damage and Healing Formulas**: The `Pomo.Lib/Combat.fs` file contains functions to calculate physical and magical damage, as well as healing. These functions incorporate stats like `AttackPower`, `Armor`, `SpellPower`, and resistances, and they also include logic for evasion, critical hits, and random variance.
- [x] **Status Effects Framework**: The `Pomo.Lib/Effects.fs` file provides a robust framework for status effects. It supports different stacking behaviors (`NoStack`, `RefreshDuration`, `AddStack`) and various effect durations (`Timed`, `Loop`, `Instant`).
- [x] **Effect Tick and Timers**: The `tickEffects` function in `Pomo.Lib/Effects.fs` correctly processes the duration of active effects, handles their expiration, and triggers periodic effects like Damage over Time (DoT) or Heal over Time (HoT). This is integrated into the main game loop in `Pomo.Lib/Gameplay.fs`.
- [x] **Resource Costs and Cooldowns**: The `aReadyAbilities` function in `Pomo.Lib/Gameplay.fs` provides a reactive set of abilities that are ready to be used, which aligns with the plan's goal of managing ability cooldowns.

## Missing Features

- [ ] **Resource Management (HP/MP/Stamina)**: While the `Domain.fs` file defines `HP`, `MP`, and `Stamina` in the `Resources` record, there is no implementation for consuming these resources when actions are performed. The game needs logic to deduct the appropriate costs (e.g., MP for spells, Stamina for physical abilities) from an entity's resources.
- [ ] **Specific Effect Kinds**: The plan mentions several kinds of effects, such as `Stun`, `Silence`, `Taunt`, and `Shield`. Although the framework is in place, the specific logic for these control and protection effects has not been implemented yet. For example, a `Stun` effect should prevent an entity from taking actions, but this rule is not yet in the codebase.
