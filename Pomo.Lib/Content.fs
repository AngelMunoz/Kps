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
        Modifiers = [ StatModifier.Additive(Stat.Strength, 5) ]
      }
      2<EffectId>,
      {
        Id = 2<EffectId>
        Name = "Minor Armor Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(20000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [ StatModifier.Additive(Stat.DefensePotential, -5) ]
      }
      // Phase 3 Effect Kinds for testing
      100<EffectId>,
      {
        Id = 100<EffectId>
        Name = "Stun"
        Kind = EffectKind.Stun
        Duration = Timed(5000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = []
      }
      101<EffectId>,
      {
        Id = 101<EffectId>
        Name = "Silence"
        Kind = EffectKind.Silence
        Duration = Timed(8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = []
      }
      102<EffectId>,
      {
        Id = 102<EffectId>
        Name = "Shield"
        Kind = EffectKind.Shield 10
        Duration = Timed(15000L<Tick>)
        Stacking = StackingRule.AddStack(5) // Max 5 stacks
        Modifiers = []
      }
      103<EffectId>,
      {
        Id = 103<EffectId>
        Name = "Taunt"
        Kind = EffectKind.Taunt
        Duration = Timed(3000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = []
      }
      104<EffectId>,
      {
        Id = 104<EffectId>
        Name = "No-Stack Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(10000L<Tick>)
        Stacking = StackingRule.NoStack
        Modifiers = []
      }
      105<EffectId>,
      {
        Id = 105<EffectId>
        Name = "Poison"
        Kind = EffectKind.DamageOverTime 5
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = []
      }
      106<EffectId>,
      {
        Id = 106<EffectId>
        Name = "Regeneration"
        Kind = EffectKind.HealOverTime 5
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = []
      }
    ]


module AbilityStore =

  let definitions: Map<int<AbilityId>, AbilityDefinition> =
    Map.ofList [
      1<AbilityId>,
      {
        Id = 1<AbilityId>
        Name = "Melee Attack"
        Cost =
          Some {
            Type = ResourceType.Stamina
            Amount = 10
          }
        Cooldown = 2000L<Tick> // 2 seconds
        Effects = []
      }
      2<AbilityId>,
      {
        Id = 2<AbilityId>
        Name = "Fireball"
        Cost = Some { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<Tick> // 5 seconds
        Effects = [ 2<EffectId> ]
      }
      3<AbilityId>,
      {
        Id = 3<AbilityId>
        Name = "No-Stack Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Effects = [ 104<EffectId> ]
      }
      4<AbilityId>,
      {
        Id = 4<AbilityId>
        Name = "Buff Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Effects = [ 1<EffectId> ] // RefreshDuration effect
      }
      5<AbilityId>,
      {
        Id = 5<AbilityId>
        Name = "Shield Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 15 }
        Cooldown = 1000L<Tick>
        Effects = [ 102<EffectId> ] // AddStack effect
      }
      6<AbilityId>,
      {
        Id = 6<AbilityId>
        Name = "Poison Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Effects = [ 105<EffectId> ] // DoT effect
      }
      7<AbilityId>,
      {
        Id = 7<AbilityId>
        Name = "Regen Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Effects = [ 106<EffectId> ] // HoT effect
      }
    ]
