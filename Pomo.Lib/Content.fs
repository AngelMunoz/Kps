namespace Pomo.Lib.Content

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Effects

module EffectStore =
  let definitions: Map<int<EffectId>, EffectDefinition> =
    Map.ofList [
      1<EffectId>,
      {
        Id = 1<EffectId>
        Name = "Minor Strength Buff"
        Kind = EffectKind.Buff
        Duration = Timed(30000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Additive(Power, 5))
        |]
        FormulaId = ValueNone
      }
      2<EffectId>,
      {
        Id = 2<EffectId>
        Name = "Minor Armor Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(20000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Additive(DP, -5))
        |]
        FormulaId = ValueNone
      }
      // Phase 3 Effect Kinds for testing
      100<EffectId>,
      {
        Id = 100<EffectId>
        Name = "Stun"
        Kind = EffectKind.Stun
        Duration = Timed(5000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      101<EffectId>,
      {
        Id = 101<EffectId>
        Name = "Silence"
        Kind = EffectKind.Silence
        Duration = Timed(8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      103<EffectId>,
      {
        Id = 103<EffectId>
        Name = "Taunt"
        Kind = EffectKind.Taunt
        Duration = Timed(3000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      104<EffectId>,
      {
        Id = 104<EffectId>
        Name = "No-Stack Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(10000L<Tick>)
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Subtractive(DX, 2))
        |]
        FormulaId = ValueNone
      }
      105<EffectId>,
      {
        Id = 105<EffectId>
        Name = "Poison"
        Kind = EffectKind.DamageOverTime
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Subtractive(HP, 5))
        |]
        FormulaId = ValueNone
      }
      106<EffectId>,
      {
        Id = 106<EffectId>
        Name = "Regeneration"
        Kind = EffectKind.HealOverTime
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.StaticMod(StatModifier.Additive(HP, 5)) |]
        FormulaId = ValueNone
      }
      107<EffectId>,
      {
        Id = 107<EffectId>
        Name = "MP Boost"
        Kind = EffectKind.Buff
        Duration = Permanent
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Multiplicative(MP, 1.2))
        |]
        FormulaId = ValueNone
      }
      108<EffectId>,
      {
        Id = 108<EffectId>
        Name = "AP Boost"
        Kind = EffectKind.Buff
        Duration = Permanent
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Multiplicative(AP, 1.15))
        |]
        FormulaId = ValueNone
      }
      // Enhanced Effects for Testing (not wired into the linear pipeline yet; kept for future use)
      200<EffectId>,
      {
        Id = 200<EffectId>
        Name = "Sacrificial Power HP Cost"
        Kind = EffectKind.Buff
        Duration = Timed(30000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.ResourceConversion(
            ResourceType.HP,
            ResourceType.HP,
            -0.10
          )
        |]
        FormulaId = ValueNone
      }
      202<EffectId>,
      {
        Id = 202<EffectId>
        Name = "Sacrificial Power Damage Boost"
        Kind = EffectKind.Buff
        Duration = Timed(30000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.AbilityDamageMod(0.50) |]
        FormulaId = ValueNone
      }
      201<EffectId>,
      {
        Id = 201<EffectId>
        Name = "MP to HP Conversion"
        Kind = EffectKind.Buff
        Duration = Instant
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.ResourceConversion(
            ResourceType.MP,
            ResourceType.HP,
            0.5
          )
        |]
        FormulaId = ValueNone
      }
    ]

module FormulaStore =
  let definitions: Map<int<FormulaId>, FormulaDefinition> =
    Map.ofList [
      1<FormulaId>,
      {
        Id = 1<FormulaId>
        Name = "Physical + Neutral Damage"
        Calculate =
          fun ctx -> {
            BaseDamage = ctx.InvokerStats.AP * 2
            ElementalDamage = 0
            Element = Attributes.Neutral
            DamageType = DamageType.Physical
          }
      }
      2<FormulaId>,
      {
        Id = 2<FormulaId>
        Name = "Fire + Magical Damage"
        Calculate =
          fun ctx ->
            let fireAttr =
              ctx.InvokerElementalAttributes.TryFindV Attributes.Fire
              |> ValueOption.defaultValue 0.0

            let fireDamage = fireAttr * (ctx.InvokerStats.MA * 2 |> float)

            {
              BaseDamage = ctx.InvokerStats.MA * 2
              ElementalDamage = fireDamage |> int
              Element = Attributes.Fire
              DamageType = DamageType.Magical
            }
      }
      3<FormulaId>,
      {
        Id = 3<FormulaId>
        Name = "Magic + Neutral Damage"
        Calculate =
          fun ctx -> {
            BaseDamage = ctx.InvokerStats.MA * 2
            ElementalDamage = 0
            Element = Attributes.Neutral
            DamageType = DamageType.Magical
          }
      }
      4<FormulaId>,
      {
        Id = 4<FormulaId>
        Name = "Fire + Physical Damage"
        Calculate =
          fun ctx ->
            let fireAttr =
              ctx.InvokerElementalAttributes.TryFindV Attributes.Fire
              |> ValueOption.defaultValue 0.0

            let elementalDamage = fireAttr * (ctx.InvokerStats.AP * 2 |> float)

            {
              BaseDamage = ctx.InvokerStats.AP * 2
              ElementalDamage = elementalDamage |> int
              Element = Attributes.Fire
              DamageType = DamageType.Physical
            }
      }
      // Enhanced Effects Formulas
      100<FormulaId>,
      {
        Id = 100<FormulaId>
        Name = "HP Cost Calculation"
        Calculate =
          fun ctx -> {
            BaseDamage = ctx.InvokerStats.HP / 10
            ElementalDamage = 0
            Element = Attributes.Neutral
            DamageType = DamageType.Physical
          }
      }
    ]

module AbilityStore =

  let activeDefinitions: Map<int<AbilityId>, ActiveAbilityDefinition> =
    Map.ofList [
      1<AbilityId>,
      {
        Id = 1<AbilityId>
        Name = "Melee Attack"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 2000L<Tick> // 2 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
      }
      2<AbilityId>,
      {
        Id = 2<AbilityId>
        Name = "Fireball"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<Tick> // 5 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 2<FormulaId>
        Effects = [| 2<EffectId> |]
        Requirements = Array.empty
      }
      3<AbilityId>,
      {
        Id = 3<AbilityId>
        Name = "No-Stack Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueNone
        Effects = [| 104<EffectId> |]
        Requirements = Array.empty
      }
      4<AbilityId>,
      {
        Id = 4<AbilityId>
        Name = "Buff Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.Self
        FormulaId = ValueNone
        Effects = [| 1<EffectId> |] // RefreshDuration effect
        Requirements = Array.empty
      }
      5<AbilityId>,
      {
        Id = 5<AbilityId>
        Name = "Shield Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 15 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleAlly
        FormulaId = ValueNone
        Effects = [| 102<EffectId> |] // AddStack effect
        Requirements = Array.empty
      }
      6<AbilityId>,
      {
        Id = 6<AbilityId>
        Name = "Poison Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 3<FormulaId>
        Effects = [| 105<EffectId> |] // DoT effect
        Requirements = Array.empty
      }
      7<AbilityId>,
      {
        Id = 7<AbilityId>
        Name = "Regen Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleAlly
        FormulaId = ValueNone
        Effects = [| 106<EffectId> |] // HoT effect
        Requirements = Array.empty
      }
      8<AbilityId>,
      {
        Id = 8<AbilityId>
        Name = "Basic Melee Attack No Cost"
        Cost = ValueNone
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
      }
      9<AbilityId>,
      {
        Id = 9<AbilityId>
        Name = "Silence Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 25 }
        Cooldown = 10000L<Tick> // 10 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueNone
        Effects = [| 101<EffectId> |] // Silence effect
        Requirements = Array.empty
      }
      // Enhanced Effects Test Abilities
      100<AbilityId>,
      {
        Id = 100<AbilityId>
        Name = "Sacrificial Strike"
        Cost = ValueNone
        Cooldown = 3000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 1<FormulaId>
        Effects = [| 200<EffectId>; 202<EffectId> |] // Applies HP cost + damage boost to self
        Requirements = Array.empty
      }
      101<AbilityId>,
      {
        Id = 101<AbilityId>
        Name = "MP Conversion"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<Tick>
        Targeting = TargetType.Self
        FormulaId = ValueNone
        Effects = [| 201<EffectId> |] // Converts MP to HP
        Requirements = Array.empty
      }
    ]

  let passiveDefinitions: Map<int<AbilityId>, PassiveAbilityDefinition> =
    Map.ofList [
      1001<AbilityId>,
      {
        Id = 1001<AbilityId>
        Name = "Total Concentration"
        Requirements = Array.empty
        Effects = [| 108<EffectId> |]
      }
      1002<AbilityId>,
      {
        Id = 1002<AbilityId>
        Name = "Mana Flow"
        Requirements = Array.empty
        Effects = [| 107<EffectId> |]
      }
    ]

  // Unified ability store that returns AbilityKind
  let definitions: Map<int<AbilityId>, AbilityKind> =
    let actives = activeDefinitions |> Map.map(fun _ def -> Active def)

    let passives = passiveDefinitions |> Map.map(fun _ def -> Passive def)

    Map.fold (fun acc k v -> Map.add k v acc) actives passives
