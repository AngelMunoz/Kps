namespace Pomo.Lib.Content

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Effects

module EffectStore =
  let definitions: Map<int<EffectId>, EffectDefinition> =
    Map.ofList [
      (1<EffectId>,
       {
         Id = 1<EffectId>
         Name = "Minor Strength Buff"
         Kind = EffectKind.Buff
         Duration = Timed(30000L<Tick>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = [ StatModifier.Additive(Stat.Strength, 5) ]
       })
      (2<EffectId>,
       {
         Id = 2<EffectId>
         Name = "Minor Armor Debuff"
         Kind = EffectKind.Debuff
         Duration = Timed(20000L<Tick>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = [ StatModifier.Additive(Stat.Armor, -5) ]
       })
      // Phase 3 Effect Kinds for testing
      (100<EffectId>,
       {
         Id = 100<EffectId>
         Name = "Stun"
         Kind = EffectKind.Stun
         Duration = Timed(5000L<Tick>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (101<EffectId>,
       {
         Id = 101<EffectId>
         Name = "Silence"
         Kind = EffectKind.Silence
         Duration = Timed(8000L<Tick>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (102<EffectId>,
       {
         Id = 102<EffectId>
         Name = "Shield"
         Kind = EffectKind.Shield 10
         Duration = Timed(15000L<Tick>)
         Stacking = StackingRule.AddStack(5) // Max 5 stacks
         Modifiers = []
       })
      (103<EffectId>,
       {
         Id = 103<EffectId>
         Name = "Taunt"
         Kind = EffectKind.Taunt
         Duration = Timed(3000L<Tick>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (104<EffectId>,
       {
         Id = 104<EffectId>
         Name = "No-Stack Debuff"
         Kind = EffectKind.Debuff
         Duration = Timed(10000L<Tick>)
         Stacking = StackingRule.NoStack
         Modifiers = []
       })
      (105<EffectId>,
       {
         Id = 105<EffectId>
         Name = "Poison"
         Kind = EffectKind.DamageOverTime 5
         Duration = Loop(2000L<Tick>, 8000L<Tick>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
      (106<EffectId>,
       {
         Id = 106<EffectId>
         Name = "Regeneration"
         Kind = EffectKind.HealOverTime 5
         Duration = Loop(2000L<Tick>, 8000L<Tick>)
         Stacking = StackingRule.RefreshDuration
         Modifiers = []
       })
    ]
