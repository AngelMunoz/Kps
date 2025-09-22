namespace Pomo.Lib.Content

open FSharp.Data.Adaptive
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
        Modifiers = IndexList.ofList [ StatModifier.Additive(Stat.Power, 5) ]
      }
      2<EffectId>,
      {
        Id = 2<EffectId>
        Name = "Minor Armor Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(20000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = IndexList.ofList [ StatModifier.Additive(Stat.DP, -5) ]
      }
      // Phase 3 Effect Kinds for testing
      100<EffectId>,
      {
        Id = 100<EffectId>
        Name = "Stun"
        Kind = EffectKind.Stun
        Duration = Timed(5000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = IndexList.empty
      }
      101<EffectId>,
      {
        Id = 101<EffectId>
        Name = "Silence"
        Kind = EffectKind.Silence
        Duration = Timed(8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = IndexList.empty
      }
      102<EffectId>,
      {
        Id = 102<EffectId>
        Name = "Shield"
        Kind = EffectKind.Shield 10
        Duration = Timed(15000L<Tick>)
        Stacking = StackingRule.AddStack(5) // Max 5 stacks
        Modifiers = IndexList.empty
      }
      103<EffectId>,
      {
        Id = 103<EffectId>
        Name = "Taunt"
        Kind = EffectKind.Taunt
        Duration = Timed(3000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = IndexList.empty
      }
      104<EffectId>,
      {
        Id = 104<EffectId>
        Name = "No-Stack Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(10000L<Tick>)
        Stacking = StackingRule.NoStack
        Modifiers = IndexList.empty
      }
      105<EffectId>,
      {
        Id = 105<EffectId>
        Name = "Poison"
        Kind = EffectKind.DamageOverTime 5
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = IndexList.empty
      }
      106<EffectId>,
      {
        Id = 106<EffectId>
        Name = "Regeneration"
        Kind = EffectKind.HealOverTime 5
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = IndexList.empty
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
    ]



module AbilityStore =

  let definitions: Map<int<AbilityId>, AbilityDefinition> =
    Map.ofList [
      1<AbilityId>,
      {
        Id = 1<AbilityId>
        Name = "Melee Attack"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 2000L<Tick> // 2 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 1<FormulaId>
        Effects = IndexList.empty
      }
      2<AbilityId>,
      {
        Id = 2<AbilityId>
        Name = "Fireball"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<Tick> // 5 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 2<FormulaId>
        Effects = IndexList.ofList [ 2<EffectId> ]
      }
      3<AbilityId>,
      {
        Id = 3<AbilityId>
        Name = "No-Stack Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueNone
        Effects = IndexList.ofList [ 104<EffectId> ]
      }
      4<AbilityId>,
      {
        Id = 4<AbilityId>
        Name = "Buff Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.Self
        FormulaId = ValueNone
        Effects = IndexList.ofList [ 1<EffectId> ] // RefreshDuration effect
      }
      5<AbilityId>,
      {
        Id = 5<AbilityId>
        Name = "Shield Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 15 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleAlly
        FormulaId = ValueNone
        Effects = IndexList.ofList [ 102<EffectId> ] // AddStack effect
      }
      6<AbilityId>,
      {
        Id = 6<AbilityId>
        Name = "Poison Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 3<FormulaId>
        Effects = IndexList.ofList [ 105<EffectId> ] // DoT effect
      }
      7<AbilityId>,
      {
        Id = 7<AbilityId>
        Name = "Regen Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleAlly
        FormulaId = ValueNone
        Effects = IndexList.ofList [ 106<EffectId> ] // HoT effect
      }
    ]
