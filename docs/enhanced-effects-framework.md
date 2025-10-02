# Enhanced Effects Framework Implementation Plan

## Overview

This document tracks the design and implementation of an enhanced effects framework that supports dynamic, formula-based effects with complex interactions. This system extends beyond basic stat modifiers to enable sophisticated gameplay mechanics.

## Target Effect Categories

### 1. Damage Amplification with Resource Cost

**Type**: HP-cost damage boost

- When active, invoking abilities consumes % of ability's base damage from user's HP
- Increases ability's final damage proportionally
- **Hook**: OnAbilityInvoke (pre-execution)

### 2. Resource Conversion

**Type**: HP/MP transformation

- Converts between resource types using formulas
- Instant effect with calculated exchange rates
- **Hook**: Instant (direct resource manipulation)

### 4. Distance-Based Damage

**Type**: Positional damage calculation

- AoE/MultiHit with distance-modified damage
- Damage calculation includes spatial factors
- **Hook**: OnAbilityInvoke + custom targeting (future)

## AbilityContext Flow

### Current vs Enhanced Resolution Flow

```mermaid
graph TD
    A[Command: UseAbility] --> B[validateAction]
    B --> B1[Check Ability Requirements]
    B1 --> C{Validation Result}
    C -->|Valid| D[Build AbilityContext]
    C -->|Invalid| E[Return Empty StateChange]

    D --> F[Process OnAbilityInvoke Effects]
    F --> G[Calculate Base Damage]
    G --> H[Apply Effect Modifiers]
    H --> I[Process OnDamageReceived Effects]
    I --> J[Apply Final Damage]
    J --> K[Process OnAbilityComplete Effects]
    K --> L[Return StateChange]

    subgraph "Current System"
        B --> G2[Calculate Damage Directly]
        G2 --> J2[Apply Damage]
        J2 --> L2[Apply Effects]
    end

    subgraph "Enhanced System"
        B1 --> D
        D --> F
        F --> G
        H --> I
    end
```

### Context Pipeline Pattern

```mermaid
graph LR
    A[Initial Context] --> B[OnAbilityInvoke Effects]
    B --> C[Updated Context + HP Cost]
    C --> D[Damage Calculation]
    D --> E[Context + Damage Result]
    E --> F[OnDamageReceived Effects]
    F --> G[OnAbilityComplete Effects]
    G --> I[Final Context + Events]
```

### AbilityContext Construction Point

The context is built in `resolveAbility` after validation succeeds:

```fsharp
// Current location in resolveAbility after validation
| ValueSome struct (actorComponents, struct (targetId, targetComponents), costOpt, abilityDef) ->
    let! actorStats = rparams.derivedStats |> AMap.find actorId
    let! targetStats = rparams.derivedStats |> AMap.find targetId
    let! gameTime = rparams.gameTime

    // NEW: Build AbilityContext here
    let abilityContext = {
        InvokerStats = actorStats
        TargetStats = targetStats
        AbilityResult = ValueNone  // Will be populated after damage calc
        InvokerEffects = actorComponents.Effects
        TargetEffects = targetComponents.Effects
        GameTime = gameTime
    }

    // Process OnAbilityInvoke effects with context
    // Calculate damage with effect modifiers
    // Update context with damage result
    // Continue with enhanced pipeline...
```

## Implementation Steps

### Step 0.5: Passive Skills & Ability Requirements System ✅ (Implemented)

Redesign ability system with separate passive/active definitions:

```fsharp
[<Struct>]
type PassiveAbilityDefinition = {
  Id: int<AbilityId>
  Name: string
  Effects: IndexList<int<EffectId>>  // Auto-applied permanent effects
  Requirements: IndexList<AbilityRequirement>
}

[<Struct>]
type ActiveAbilityDefinition = {
  Id: int<AbilityId>
  Name: string
  Cooldown: int64<Tick>
  Cost: ResourceCost voption
  Targeting: TargetType
  FormulaId: int<FormulaId> voption
  Effects: IndexList<int<EffectId>>
  Requirements: IndexList<AbilityRequirement>
}

[<Struct>]
type AbilityKind =
  | Passive of PassiveAbilityDefinition
  | Active of ActiveAbilityDefinition

[<Struct>]
type AbilityRequirement =
  | StatRequirement of Stat * int
  | AbilityRequirement of int<AbilityId>
  | FormulaRequirement of int<FormulaId>

[<Struct>]
type Duration =
  | Instant | Timed of int64<Tick> | Loop of int64<Tick> * int64<Tick>
  | Permanent  // NEW: For passive skill effects
```

**Integration with Enhanced Effects:**

- **Passive abilities**: Create `Permanent` duration effects when learned
- **Active abilities**: Use existing resolution pipeline with effect hooks
- **Requirements**: Validated during `OnAbilityInvoke` hook
- **Permanent effects**: Skip tick processing, never expire

**Example - Gun Carrier System:**

```fsharp
// Passive ability definition
Passive {
  Id = 50<AbilityId>
  Name = "Gun Carrier"
  Effects = IndexList.ofList [51<EffectId>] // Creates permanent effect
  Requirements = IndexList.empty
}

// Gun ability with requirement
Active {
  Id = 100<AbilityId>
  Name = "Pistol Shot"
  Requirements = IndexList.ofList [
    AbilityRequirement(50<AbilityId>) // Must have Gun Carrier
  ]
  // ... other active ability fields
}

// Permanent effect for Gun Carrier
{
  Id = 51<EffectId>
  Name = "Gun Proficiency"
  Duration = Permanent  // Never expires
  Kind = EffectKind.Buff
  // ... other fields
}
```

### Step 1: Effect Hook System ✅ (Implemented in Domain.fs and Resolution.fs)

Add effect hooks to intercept different resolution phases:

```fsharp
[<Struct>]
type EffectHook =
  | OnAbilityInvoke     // Before ability executes
  | OnDamageReceived    // When taking damage
  | OnResourceChange    // When HP/MP changes
  | OnAbilityComplete   // After ability resolves
  | OnTick              // Periodic processing (existing DoT/HoT)
```

### Step 2: Dynamic Effect Modifiers ✅ (Implemented in Domain.fs, partial support in Resolution.fs)

Replace static modifiers with formula-based system:

```fsharp
[<Struct>]
type EffectModifier =
  | StaticMod of StatModifier           // Current system (backward compatibility)
  | DynamicMod of int<FormulaId>        // Formula-based calculation
  | AbilityDamageMod of float           // % modifier to ability damage
  | ResourceConversion of ResourceType * ResourceType * float
```

### Step 3: Ability Resolution Context ✅ (AbilityContext type and context pipeline present)

Create context for effects to access during resolution:

```fsharp
[<Struct>]
type AbilityContext = {
  InvokerStats: DerivedStats
  TargetStats: DerivedStats
  AbilityResult: DamageResult voption
  InvokerEffects: ActiveEffect alist
  TargetEffects: ActiveEffect alist
  GameTime: int64<Tick>
}
```


### Step 5: Enhanced Effect Processing Pipeline ⏳

Modify resolution to process effects at different hooks:

1. **Pre-Ability Processing**:

   - Process `OnAbilityInvoke` effects
   - Apply HP costs for damage amplification
   - Calculate damage bonuses

2. **Ability Execution**:

   - Apply base damage with effect modifiers
   - Process formula-based enhancements

3. **Damage Reception**:

   - Process `OnDamageReceived` effects
   - Handle damage reflection

4. **Post-Ability Processing**:
   - Process `OnAbilityComplete` effects
   - Apply resource conversion mechanics

### Step 6: Effect Definition Extensions ✅ (EffectDefinition extended in Domain.fs)

Extend `EffectDefinition` to support new capabilities:

```fsharp
[<Struct>]
type EffectDefinition = {
  Id: int<EffectId>
  Name: string
  Kind: EffectKind
  Stacking: StackingRule
  Duration: Duration
  Modifiers: IndexList<EffectModifier>
  Hooks: IndexList<EffectHook>          // NEW: When effect activates
  FormulaId: int<FormulaId> voption     // NEW: Dynamic calculations
}
```

## Implementation Priority

### Phase 4.5: Enhanced Effects Framework (Before Phase 5)

**Status**: 🎯 **REQUIRED BEFORE PHASE 5**

1. **Step 0.5**: Passive skills and ability requirements
2. **Step 1-2**: Core effect hooks and dynamic modifiers
3. **Step 3**: Ability context system
5. **Step 5**: Enhanced processing pipeline
6. **Step 6**: Effect definition extensions

### Example Implementations

#### HP-Cost Damage Boost Effect

```fsharp
{
  Id = 200<EffectId>
  Name = "Sacrificial Power"
  Kind = EffectKind.Buff
  Duration = Timed(30000L<Tick>)
  Stacking = StackingRule.RefreshDuration
  Modifiers = IndexList.ofList [
    AbilityDamageMod(0.10)  // 10% damage increase
  ]
  Hooks = IndexList.ofList [OnAbilityInvoke]
  FormulaId = ValueSome 100<FormulaId>  // HP cost calculation
}
```

## Integration Points

### Resolution.fs Changes

- Add `AbilityKind` pattern matching in `validateAction`
- Add ability requirement validation (only for `Active` abilities)
- Skip tick processing for `Permanent` duration effects
- Extend `resolveAbility` to process effect hooks
- Implement pre/post ability effect processing

### Effects.fs Changes

- Add hook-based effect processing
- Add dynamic modifier calculations

### Domain.fs Changes

- Replace `AbilityDefinition` with `AbilityKind` discriminated union
- Add `PassiveAbilityDefinition` and `ActiveAbilityDefinition`
- Add `AbilityRequirement` types
- Add `Permanent` to `Duration` type
- Extend effect and resource types
- Update ability context typese ability context types

## Testing Strategy

### Unit Tests

- Passive skill effect creation
- Ability requirement validation
- Effect hook processing
- Resource conversion accuracy
- Dynamic modifier calculations

### Integration Tests

- Gun Carrier passive skill system
- Ability requirement blocking/allowing
- HP-cost damage amplification mechanics
- Magic barrier absorption + regeneration
- Resource conversion mechanics
- Complex effect interactions

### Property Tests

- Resource conversions maintain balance
- Effect stacking with dynamic modifiers

## Success Criteria

1. ✅ All 4 effect categories implemented and tested
2. ✅ Backward compatibility with existing effects maintained
3. ✅ Performance impact minimal (adaptive collections)
4. ✅ Formula-based effects work with existing formula system
6. ✅ Effect hooks process at correct resolution phases

## Dependencies

- **Requires**: Current Phase 0-4 completion
- **Blocks**: Phase 5 (Save/Load) - enhanced effects must be serializable
- **Enables**: Advanced gameplay mechanics and content creation

---

**Next Action**: Begin Step 1 implementation after Phase 4 completion confirmation.
