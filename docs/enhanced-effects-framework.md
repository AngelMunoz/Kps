# Enhanced Effects Framework Implementation Plan

## Overview

This document tracks the design and implementation of an enhanced effects framework that supports dynamic, formula-based effects with complex interactions. This system extends beyond basic stat modifiers to enable sophisticated gameplay mechanics.

**Status**: ✅ **PHASE 4.5 COMPLETE** - All core features implemented and integrated.

## Implemented Effect Categories

### 1. Damage Amplification with Resource Cost

**Type**: HP-cost damage boost

- When active, invoking abilities consumes % of ability's base damage from user's HP
- Increases ability's final damage proportionally
- **Implementation**: AbilityDamageMod in Resolution.fs

### 2. Resource Conversion

**Type**: HP/MP transformation

- Converts between resource types using formulas
- Instant effect with calculated exchange rates
- Supports HP-cost amplification (HP→HP with negative ratio)
- Supports MP↔HP conversions
- **Implementation**: ResourceConversion in Resolution.fs

### 3. Dynamic Formula-Based Modifiers

**Type**: Formula-driven stat modification

- Calculate stat modifiers using formula system
- Explicit stat targeting (AP, MA, HP, MP, etc.)
- Multiple dynamic mods stack additively
- **Implementation**: DynamicMod in Gameplay.fs

## Implementation Steps

### Step 0.5: Passive Skills & Ability Requirements System ✅

Redesign ability system with separate passive/active definitions:

```fsharp
[<Struct>]
type PassiveAbilityDefinition = {
  Id: int<AbilityId>
  Name: string
  Effects: int<EffectId>[]
  Requirements: AbilityRequirement[]
}

[<Struct>]
type ActiveAbilityDefinition = {
  Id: int<AbilityId>
  Name: string
  Cooldown: int64<Tick>
  Cost: ResourceCost voption
  Targeting: TargetType
  FormulaId: int<FormulaId> voption
  Effects: int<EffectId>[]
  Requirements: AbilityRequirement[]
}

[<Struct>]
type AbilityKind =
  | Passive of PassiveAbilityDefinition
  | Active of ActiveAbilityDefinition

[<Struct>]
type Duration =
  | Instant | Timed of int64<Tick> | Loop of int64<Tick> * int64<Tick>
  | Permanent  // For passive skill effects
```

**Integration:**

- **Passive abilities**: Create `Permanent` duration effects when learned
- **Active abilities**: Use the existing resolution pipeline
- **Requirements**: Validated during standard ability validation
- **Permanent effects**: Skip tick processing, never expire

### Step 1: Resolution Simplification ✅

Adopted a straightforward, linear resolution approach (pre/compute/apply) without introducing a hook infrastructure.

### Step 2: Dynamic Effect Modifiers ✅

Replace static modifiers with formula-based system:

```fsharp
[<Struct>]
type EffectModifier =
  | StaticMod of StatModifier           // Backward compatibility
  | DynamicMod of formulaId: int<FormulaId> * target: Stat  // Formula-based calculation
  | AbilityDamageMod of float           // % modifier to ability damage
  | ResourceConversion of ResourceType * ResourceType * float  // Resource conversion
```

**Implementation Notes:**
- **DynamicMod**: Integrated in `Gameplay.applyModifiers` function. Evaluates formulas with current derived stats as context and applies BaseDamage result to the explicitly specified target stat. Multiple DynamicMod effects targeting the same stat stack additively. **Limitation**: Currently only targets derived stats (AP, MA, HP, MP, etc.); base stats (Power, Magic, Sense, Charm) are not supported.
- **AbilityDamageMod**: Integrated in `calculateDamage` function. Active effects on the attacker are scanned for AbilityDamageMod modifiers, which are summed and applied as percentage boosts to final damage after defense reduction.
- **ResourceConversion**: Integrated in `applyResourceCost` function. Supports HP-cost amplification (HP→HP with negative ratio) and resource type conversions (MP→HP, HP→MP with positive ratios).

**Example Usage:**
```fsharp
// Effect that boosts AP using formula result
EffectModifier.DynamicMod(101<FormulaId>, AP)

// Effect that boosts MA using formula result
EffectModifier.DynamicMod(101<FormulaId>, MA)
```

### Step 3: Linear Effect Processing Pipeline ✅

Resolution processes effects in a simple, linear flow:

1. **Pre-Resolution**: ✅
   - Apply resource costs (including HP-cost amplification rules) - implemented in `applyResourceCost`
   - Gather relevant modifiers and context

2. **Ability Execution**: ✅
   - Calculate base values - damage formulas integrated
   - Apply dynamic modifiers and formulas - AbilityDamageMod applied during damage calculation

3. **Apply Results**: ✅
   - Apply damage/heal and state updates - working in `AbilityResolution.resolve`
   - Apply resource conversion mechanics - implemented in `applyResourceCost`

### Step 4: Effect Definition Extensions ✅

Extended `EffectDefinition` to support new capabilities:

```fsharp
[<Struct>]
type EffectDefinition = {
  Id: int<EffectId>
  Name: string
  Kind: EffectKind
  Stacking: StackingRule
  Duration: Duration
  Modifiers: EffectModifier[]
  FormulaId: int<FormulaId> voption     // Dynamic calculations
}
```

## Example Implementation

### HP-Cost Damage Boost Effect

```fsharp
{
  Id = 200<EffectId>
  Name = "Sacrificial Power"
  Kind = EffectKind.Buff
  Duration = Timed(30000L<Tick>)
  Stacking = StackingRule.RefreshDuration
  Modifiers = [|
    AbilityDamageMod(0.10)  // 10% damage increase
  |]
  FormulaId = ValueSome 100<FormulaId>  // HP cost calculation
}
```

## Integration Points

### Resolution.fs Changes ✅

- `AbilityKind` pattern matching in `validateAction`
- Ability requirement validation (only for `Active` abilities)
- Skip tick processing for `Permanent` duration effects
- Clear pre/compute/apply flow in `resolveAbility`
- HP-cost amplification, dynamic modifiers, and resource conversion implemented

### Gameplay.fs Changes ✅

- Dynamic modifier calculations in `applyModifiers`
- Formula evaluation with derived stats context
- Additive stacking for multiple DynamicMod effects

### Domain.fs Changes ✅

- `AbilityDefinition` replaced with `AbilityKind` discriminated union
- `PassiveAbilityDefinition` and `ActiveAbilityDefinition` added
- `AbilityRequirement` types added
- `Permanent` added to `Duration` type
- Effect and resource types extended

## Testing Strategy

### Unit Tests

- Passive skill effect creation
- Ability requirement validation
- Resource conversion accuracy
- Dynamic modifier calculations

### Integration Tests

- HP-cost damage amplification mechanics
- Resource conversion mechanics
- Complex effect interactions
- Formula-based modifier evaluation

### Property Tests

- Resource conversions maintain balance
- Effect stacking with dynamic modifiers

## Success Criteria

1. ✅ All 3 effect categories implemented and tested
2. ✅ Backward compatibility with existing effects maintained
3. ✅ Performance impact minimal (adaptive collections)
4. ✅ Formula-based effects work with existing formula system

## Phase Completion

**Phase 4.5 Status**: ✅ **COMPLETE**

All core enhanced effects features have been implemented:
- DynamicMod (formula-based stat modifiers)
- AbilityDamageMod (percentage damage boosts)
- ResourceConversion (HP-cost amplification, MP↔HP conversion)
- Passive skills with Permanent duration
- Ability requirements system

The system is ready for Phase 5 (Content and Progression).

---

**Document Status**: Reflects completed Phase 4.5 implementation as of 2025-10-10.
