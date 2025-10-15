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

  let inline private isNpc(entity: EntityComponents) = not(isPlayer entity)

  let inline private inSameParty
    struct (actorId: Guid<EntityId>, targetId: Guid<EntityId>)
    (parties: amap<Guid<PartyId>, Party>)
    =
    parties
    |> AMap.exists(fun _ party ->
      party.Members.Contains(actorId) && party.Members.Contains(targetId))

  let canUseAbility
    (scenarioState: ScenarioState)
    (parties: amap<Guid<PartyId>, Party>)
    (actorId: Guid<EntityId>)
    (target: EntityComponents)
    (targetId: Guid<EntityId>)
    (abilityDef: ActiveAbilityDefinition)
    =
    adaptive {
      let intent = abilityDef.Intent
      let engagementMode = scenarioState.scenario.EngagementMode
      let combatType = scenarioState.scenario.CombatType

      match intent with
      | AbilityIntent.Support -> return true
      | AbilityIntent.Neutral
      | AbilityIntent.Offensive ->
        match engagementMode with
        | EngagementMode.Peaceful -> return false
        | EngagementMode.AlwaysOn ->
          let! actorOpt = scenarioState.entities |> AMap.tryFind actorId

          match actorOpt with
          | None -> return false
          | Some actor ->
            let actorIsPlayer = isPlayer actor
            let targetIsPlayer = isPlayer target
            let actorIsNpc = isNpc actor
            let targetIsNpc = isNpc target

            match combatType with
            | ScenarioCombatType.PvE ->
              // Players: can attack NPCs, not other players
              // NPCs: can attack players, not other NPCs (unless override enabled)
              let npcVsNpcAllowed = false // Set to true for special maps/events

              if actorIsPlayer then
                return targetIsNpc
              elif actorIsNpc then
                if targetIsPlayer then return true
                elif targetIsNpc then return npcVsNpcAllowed
                else return false
              else
                return false
            | ScenarioCombatType.PvP ->
              // Players: can attack other players (not in same party)
              // NPCs: cannot attack anyone
              if actorIsPlayer then
                let! isInParty = inSameParty struct (actorId, targetId) parties
                return targetIsPlayer && not isInParty
              else
                return false
            | ScenarioCombatType.PvH ->
              // Players: can attack anyone not in same party
              // NPCs: can attack any entity (player or NPC)
              if actorIsPlayer then
                let! isInParty = inSameParty struct (actorId, targetId) parties
                return not isInParty
              elif actorIsNpc then
                return true
              else
                return false
        | EngagementMode.Structured ->
          // In structured combat, offensive actions are only allowed if both actor and target
          // are participants in the same active battle instance.
          let! inSameBattleInstance =
            scenarioState.battleInstances
            |> AMap.exists(fun _ instance ->
              instance.Participants.Contains(actorId)
              && instance.Participants.Contains(targetId))

          return inSameBattleInstance
    }
