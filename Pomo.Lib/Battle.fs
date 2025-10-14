namespace Pomo.Lib.Battle

open System
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Scenario
open FSharp.Data.Adaptive

module Engagement =

  let inline private isPlayer(entity: EntityComponents) =
    entity.Factions.Contains(Classification.Faction.Player)

  let inline private inSameParty
    struct (actorId: Guid<EntityId>, targetId: Guid<EntityId>)
    (parties: amap<Guid<PartyId>, Party>)
    =
    parties
    |> AMap.exists(fun _ party ->
      party.Members.Contains(actorId) && party.Members.Contains(targetId))

  let canUseAbility
    (scenario: Scenario)
    (parties: amap<Guid<PartyId>, Party>)
    (actorId: Guid<EntityId>)
    (target: EntityComponents)
    (targetId: Guid<EntityId>)
    (abilityDef: ActiveAbilityDefinition)
    =
    adaptive {
      let intent = abilityDef.Intent
      let engagementMode = scenario.EngagementMode
      let combatType = scenario.CombatType

      match intent with
      | AbilityIntent.Support -> return true
      | AbilityIntent.Neutral
      | AbilityIntent.Offensive ->
        match engagementMode with
        | EngagementMode.Peaceful -> return false
        | EngagementMode.AlwaysOn ->
          match combatType with
          | ScenarioCombatType.PvE -> return not(isPlayer target)
          | ScenarioCombatType.PvP ->
            let! isInParty = inSameParty struct (actorId, targetId) parties
            return isPlayer target && not isInParty
          | ScenarioCombatType.PvH ->
            let! isInParty = inSameParty struct (actorId, targetId) parties
            return not isInParty
        | EngagementMode.Structured -> return true
    }
