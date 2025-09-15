namespace Pomo.Lib.Content

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Effects

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
