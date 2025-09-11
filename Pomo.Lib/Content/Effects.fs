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
    ]
