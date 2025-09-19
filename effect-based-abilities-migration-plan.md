# Effect-Based Abilities Migration Plan

## Overview

Migrate from hardcoded damage calculations in `resolveMeleeAttack`/`resolveCastSpell` to a unified effect-based system where damage is just another effect type.

## Current Problems

- Duplicate damage calculation logic between melee/spell resolvers
- Hardcoded damage types (physical vs magical)
- Difficult to add new ability types
- No data-driven approach for ability effects

## Proposed Solution

### 1. New Type Definitions

```fsharp
[<Measure>]
type FormulaId

type CalculationContext = {
  actorStats: Attributes.DerivedStats
  targetStats: Attributes.DerivedStats
  rng: System.Random
  gameTime: int64<Tick>
}

type IFormulaStore =
  abstract calculate: formulaId: int<FormulaId> -> context: CalculationContext -> float

type DamageType = Physical | Magical

type DamageDefinition = {
  formulaId: int<FormulaId>
  damageType: DamageType
  element: Attributes.Element
}

type StatusEffectDefinition = {
  effectId: int<EffectId>
  duration: int64<Tick>
}

type ShieldDefinition = {
  formulaId: int<FormulaId>
  duration: int64<Tick>
}

type EffectAction =
  | Damage of DamageDefinition
  | Heal of formulaId: int<FormulaId>
  | ApplyStatusEffect of StatusEffectDefinition
  | RemoveEffectsByCategory of Effects.EffectKind
  | RemoveSpecificEffect of int<EffectId>
  | AddShield of ShieldDefinition

type TargetType = 
  | Self 
  | Enemy 
  | Ally 
  | AllEnemies 
  | AllAllies 
  | SelfAndAllies
  | Everyone

type AbilityEffect = {
  action: EffectAction
  target: TargetType
}

// Replace existing AbilityDefinition in Domain.fs
type AbilityDefinition = {
  Id: int<AbilityId>
  Name: string
  Cooldown: int64<Tick>
  Cost: Abilities.ResourceCost option
  Effects: HashSet<AbilityEffect>
}
```

### 2. Formula Store Implementation

```fsharp
let createFormulaStore () =
  { new IFormulaStore with
      member _.calculate formulaId context =
        match formulaId with
        | 1<FormulaId> -> // Basic physical attack power
          float context.actorStats.AttackPower
        | 2<FormulaId> -> // Basic magical attack power
          float context.actorStats.MagicAttack
        | 3<FormulaId> -> // Dark slash (magical with +5 base)
          float (context.actorStats.MagicAttack + 5)
        | 4<FormulaId> -> // Fixed healing
          50.0
        | _ -> 0.0 }
```

### 3. Unified Resolver

Replace `resolveMeleeAttack` and `resolveCastSpell` with single `resolveAbility`:

```fsharp
let resolveAbility(abilityId: int<AbilityId>) : ResolverFn =
  fun (rparams, ractors) -> adaptive {
    let! validationResult = validateAction rparams ractors abilityId

    match validationResult with
    | None -> return emptyStateChange
    | Some(actorComponents, (targetId, targetComponents), costOpt, abilityDef) ->

      let calcContext = {
        actorStats = actorStats
        targetStats = targetStats
        rng = rparams.services.rng
        gameTime = gameTime
      }

      // Process all effects
      let events, updatedEntities =
        abilityDef.effects
        |> HashSet.fold (processAbilityEffect calcContext) ([], entities)

      return { entities = updatedEntities; events = events; gameTime = ValueNone }
  }
```

## Migration Steps

### Phase 1: Infrastructure Setup

1. Add `FormulaId` measure type and `IFormulaStore` interface
2. Add `EffectAction` and `AbilityEffect` types
3. Replace `AbilityDefinition.Effects` field with `HashSet<AbilityEffect>`
4. Add formula store to `EngineServices`
5. Implement basic formulas (base damage values - defense and hit/miss calculated in resolver)

### Phase 2: Effect Processing

1. Create `processAbilityEffect` function in Resolution.fs
2. Handle `Damage` action type with damage type (Physical vs Magical), defense calculation, hit/miss, and elemental resistance
3. Handle `ApplyStatusEffect` action type
4. Handle effect removal actions

### Phase 3: Ability Migration

1. Update all existing abilities to use effect-based definitions:

   ```fsharp
   let meleeAttack = {
     Id = 1<AbilityId>
     Name = "Melee Attack"
     Cooldown = 0L<Tick>
     Cost = None
     Effects = HashSet.ofList [{ 
       action = Damage { formulaId = 1<FormulaId>; damageType = Physical; element = Attributes.Element.Neutral }
       target = Enemy 
     }]
   }

   let magicMissile = {
     Id = 2<AbilityId>
     Name = "Magic Missile"
     Cooldown = 5L<Tick>
     Cost = Some { Type = MP; Amount = 10 }
     Effects = HashSet.ofList [{ 
       action = Damage { formulaId = 2<FormulaId>; damageType = Magical; element = Attributes.Element.Neutral }
       target = Enemy 
     }]
   }
   ```

2. Fix all references to old `Effects: int<EffectId> list` field
3. Update all ability creation code to use `HashSet.ofList`

### Phase 4: Resolver Unification

1. Replace `resolveMeleeAttack` with `resolveAbility`
2. Replace `resolveCastSpell` with `resolveAbility`
3. Update command matching in `step` function
4. Remove duplicate damage calculation code

### Phase 5: Advanced Effects

1. Add support for multi-effect abilities:
   ```fsharp
   let flameStrike = {
     effects = [
       { action = Damage { formulaId = 2<FormulaId>; damageType = Magical; element = Attributes.Element.Fire }; target = Enemy }           // Fire magic damage
       { action = ApplyStatusEffect { effectId = 10<EffectId>; duration = 10L<Tick> }; target = Enemy }   // Burn DoT
     ]
   }
   ```
2. Add cleanse abilities:
   ```fsharp
   let cleanse = {
     effects = [{ action = RemoveEffectsByCategory Effects.EffectKind.Debuff; target = Ally }]
   }
   ```

## Benefits After Migration

### Immediate Benefits

- Single resolver function instead of multiple
- Data-driven ability definitions
- Easy to add new ability types
- Consistent effect processing

### Long-term Benefits

- Complex multi-effect abilities (damage + debuff)
- Conditional effects based on context
- Easy balancing through formula tweaks
- Serializable ability definitions for modding

## Example Abilities After Migration

```fsharp
// Simple melee attack (physical, neutral)
let basicAttack = {
  Effects = HashSet.ofList [{ 
    action = Damage { formulaId = 1<FormulaId>; damageType = Physical; element = Attributes.Element.Neutral }
    target = Enemy 
  }]
}

// Magic Missile (magical, neutral)
let magicMissile = {
  Effects = HashSet.ofList [{ 
    action = Damage { formulaId = 2<FormulaId>; damageType = Magical; element = Attributes.Element.Neutral }
    target = Enemy 
  }]
}

// Dark Slash (magical, dark elemental)
let darkSlash = {
  Effects = HashSet.ofList [{ 
    action = Damage { formulaId = 3<FormulaId>; damageType = Magical; element = Attributes.Element.Dark }
    target = Enemy 
  }]
}

// AOE Fireball (hits all enemies)
let fireball = {
  Effects = HashSet.ofList [{ 
    action = Damage { formulaId = 2<FormulaId>; damageType = Magical; element = Attributes.Element.Fire }
    target = AllEnemies 
  }]
}

// Group Heal (heals self and allies)
let groupHeal = {
  Effects = HashSet.ofList [{ 
    action = Heal 4<FormulaId>
    target = SelfAndAllies 
  }]
}

// Vampiric Strike (damages enemy, heals self)
let vampiricStrike = {
  Effects = HashSet.ofList [
    { action = Damage { formulaId = 2<FormulaId>; damageType = Magical; element = Attributes.Element.Dark }; target = Enemy }
    { action = Heal 4<FormulaId>; target = Self }
  ]
}

// Mass Cleanse (removes debuffs from self and allies)
let massCleanse = {
  Effects = HashSet.ofList [{
    action = RemoveEffectsByCategory Effects.EffectKind.Debuff
    target = SelfAndAllies 
  }]
}
```

## Damage Calculation Flow

The damage system separates concerns between formula calculation and combat resolution:

```fsharp
// In processAbilityEffect (Resolution.fs)
match effect.action with
| Damage damageDefinition ->
  // 1. Get base damage from formula
  let baseDamage = formulaStore.calculate damageDefinition.formulaId context
  
  // 2. Apply damage type specific calculations
  let (finalDamage, hit) = 
    match damageDefinition.damageType with
    | Physical -> 
      let hit = Combat.calculateHit context.actorStats.Accuracy context.targetStats.Hevasion context.rng
      let damage = Combat.applyPhysicalDefense baseDamage context.targetStats.DefensePotential
      (damage, hit)
    | Magical ->
      let hit = Combat.calculateHit context.actorStats.MagicAttack context.targetStats.Luck context.rng  
      let damage = Combat.applyMagicalDefense baseDamage context.targetStats.MagicDefense
      (damage, hit)
  
  // 3. Apply elemental resistance
  let resistance = context.targetStats.Resistances |> Map.tryFind damageDefinition.element |> Option.defaultValue 0.0
  let elementalDamage = finalDamage * (1.0 - resistance)
  
  // 4. Apply damage if hit
  if hit then applyDamage elementalDamage else miss
```

## Technical Considerations

### Serialization

- All types are serializable (no functions in data)
- Formula logic stays in code, only IDs in data
- Ability definitions can be stored in JSON/XML

### Performance

- Minimal overhead (2-3 effects per ability max)
- Formula lookup by ID is O(1)
- No significant performance impact

### Breaking Changes

- `AbilityDefinition.Effects` changes from `int<EffectId> list` to `HashSet<AbilityEffect>`
- All ability definitions must be updated
- All code referencing ability effects must be updated

## Risk Mitigation

- Update all ability definitions in one commit
- Test each ability type individually
- Validate damage calculations match current behavior
- Add comprehensive unit tests for effect processing
- Fix all compilation errors as part of the refactoring
