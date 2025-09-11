namespace Pomo.Lib.Content

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Abilities

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
      }
      AbilityId 2,
      {
        Id = AbilityId 2
        Name = "Fireball"
        Cost = Some { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<ticks> // 5 seconds
      }
    ]
