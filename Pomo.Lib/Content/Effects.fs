namespace Pomo.Lib.Content

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives
open Pomo.Lib.Domain.Effects

module EffectStore =
  let definitions: Map<EffectId, EffectDefinition> =
    Map.ofList [
      (EffectId 1,
       {
         Id = EffectId 1
         Name = "Minor Strength Buff"
         Kind = EffectKind.Buff
         Duration = Timed(30000L<ticks>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = [ StatModifier.Additive(Stat.Strength, 5) ]
       })
      (EffectId 2,
       {
         Id = EffectId 2
         Name = "Minor Armor Debuff"
         Kind = EffectKind.Debuff
         Duration = Timed(20000L<ticks>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = [ StatModifier.Additive(Stat.Armor, -5) ]
       })
      // Phase 3 Effect Kinds for testing
      (EffectId 100,
       {
         Id = EffectId 100
         Name = "Stun"
         Kind = EffectKind.Stun
         Duration = Timed(5000L<ticks>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (EffectId 101,
       {
         Id = EffectId 101
         Name = "Silence"
         Kind = EffectKind.Silence
         Duration = Timed(8000L<ticks>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (EffectId 102,
       {
         Id = EffectId 102
         Name = "Shield"
         Kind = EffectKind.Shield 10
         Duration = Timed(15000L<ticks>)
         Stacking = StackingRule.AddStack(5) // Max 5 stacks
         Modifiers = []
       })
      (EffectId 103,
       {
         Id = EffectId 103
         Name = "Taunt"
         Kind = EffectKind.Taunt
         Duration = Timed(3000L<ticks>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (EffectId 104,
       {
         Id = EffectId 104
         Name = "No-Stack Debuff"
         Kind = EffectKind.Debuff
         Duration = Timed(10000L<ticks>)
         Stacking = StackingRule.NoStack
         Modifiers = []
       })
      (EffectId 105,
       {
         Id = EffectId 105
         Name = "Poison"
         Kind = EffectKind.DamageOverTime 5
         Duration = Loop(2000L<ticks>, 8000L<ticks>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (EffectId 106,
       {
         Id = EffectId 106
         Name = "Regeneration"
         Kind = EffectKind.HealOverTime 5
         Duration = Loop(2000L<ticks>, 8000L<ticks>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
    ]
