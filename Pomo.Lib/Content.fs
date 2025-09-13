namespace Pomo.Lib.Content

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Effects

module AbilityStore =

  let definitions: Map<AbilityId, AbilityDefinition> =
    Map.ofList [
      AbilityId 1,
      {
        Id = AbilityId 1
        Name = "Melee Attack"
        Cost =
          Some {
            Type = ResourceType.Stamina
            Amount = 10
          }
        Cooldown = 2000L<ticks> // 2 seconds
        Effects = []
      }
      AbilityId 2,
      {
        Id = AbilityId 2
        Name = "Fireball"
        Cost = Some { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<ticks> // 5 seconds
        Effects = [ EffectId 2 ]
      }
      AbilityId 3,
      {
        Id = AbilityId 3
        Name = "No-Stack Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<ticks>
        Effects = [ EffectId 104 ]
      }
      AbilityId 4,
      {
        Id = AbilityId 4
        Name = "Buff Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<ticks>
        Effects = [ EffectId 1 ] // RefreshDuration effect
      }
      AbilityId 5,
      {
        Id = AbilityId 5
        Name = "Shield Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 15 }
        Cooldown = 1000L<ticks>
        Effects = [ EffectId 102 ] // AddStack effect
      }
      AbilityId 6,
      {
        Id = AbilityId 6
        Name = "Poison Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<ticks>
        Effects = [ EffectId 105 ] // DoT effect
      }
      AbilityId 7,
      {
        Id = AbilityId 7
        Name = "Regen Spell"
        Cost = Some { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<ticks>
        Effects = [ EffectId 106 ] // HoT effect
      }
    ]
