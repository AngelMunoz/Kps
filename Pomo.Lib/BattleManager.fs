namespace Pomo.Lib.BattleManager

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.Scenario

module BattleInstanceLifecycle =

  let create
    (actorId: Guid<EntityId>)
    (targetId: Guid<EntityId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      let newGuid = %Guid.NewGuid()
      let! gameTime = scenarioState.gameTime

      let battleInstance = {
        Id = newGuid
        Participants = HashSet.ofList [ actorId; targetId ]
        StartTick = gameTime
      }

      return [| ScenarioChange.AddBattleInstance battleInstance |]
    }

  let join
    (actorId: Guid<EntityId>)
    (battleInstanceId: Guid<BattleInstanceId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      let! found =
        scenarioState.battleInstances |> AMap.tryFind battleInstanceId

      match found with
      | Some existing ->
        let updated = {
          existing with
              Participants = existing.Participants |> HashSet.add actorId
        }

        return [| ScenarioChange.UpdateBattleInstance updated |]
      | None ->
        let newGuid = %Guid.NewGuid()
        let! gameTime = scenarioState.gameTime

        let battleInstance = {
          Id = newGuid
          Participants = HashSet.single actorId
          StartTick = gameTime
        }

        return [| ScenarioChange.AddBattleInstance battleInstance |]
    }

  let leave
    (actorId: Guid<EntityId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      let! instanceMap = scenarioState.battleInstances |> AMap.toAVal

      let found =
        instanceMap
        |> HashMap.toArrayV
        |> Array.tryFind(fun struct (k, v) -> v.Participants.Contains actorId)

      match found with
      | Some struct (battleInstanceId, existing) ->
        let updatedParticipants =
          existing.Participants |> HashSet.remove actorId

        if updatedParticipants.Count < 2 then
          return [| ScenarioChange.RemoveBattleInstance battleInstanceId |]
        else
          let updated = {
            existing with
                Participants = updatedParticipants
          }

          return [| ScenarioChange.UpdateBattleInstance updated |]
      | None -> return [||]
    }

module PartyDuel =
  let private canDuel(scenario: Scenario) =
    match scenario.EngagementMode with
    | Structured ->
      match scenario.CombatType with
      | PvP
      | PvH -> true
      | _ -> false
    | _ -> false

  let request
    (requester: Guid<PartyId>)
    (target: Guid<PartyId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      if canDuel scenarioState.scenario then
        return [| ScenarioChange.AddPendingPartyDuel(requester, target) |]
      else
        return Array.empty
    }

  let accept
    (accepter: Guid<PartyId>)
    (requester: Guid<PartyId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      if canDuel scenarioState.scenario then
        let! pendingDuels =
          scenarioState.pendingPartyDuels |> AMap.tryFind requester

        match pendingDuels with
        | Some target when target = accepter ->
          let! requesterParty = scenarioState.parties |> AMap.tryFind requester
          let! accepterParty = scenarioState.parties |> AMap.tryFind accepter

          match requesterParty, accepterParty with
          | Some r, Some a ->
            let allParticipants = HashSet.union r.Members a.Members
            let newGuid = %Guid.NewGuid()
            let! gameTime = scenarioState.gameTime

            let battleInstance = {
              Id = newGuid
              Participants = allParticipants
              StartTick = gameTime
            }

            return [|
              ScenarioChange.RemovePendingPartyDuel requester
              ScenarioChange.AddBattleInstance battleInstance
            |]
          | _ -> return Array.empty
        | _ -> return Array.empty
      else
        return Array.empty
    }

  let cancel
    (canceller: Guid<PartyId>)
    (otherParty: Guid<PartyId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      if canDuel scenarioState.scenario then
        let! found = scenarioState.pendingPartyDuels |> AMap.tryFind canceller
        let isRequester = found.IsSome

        let! isTarget =
          scenarioState.pendingPartyDuels |> AMap.tryFind otherParty

        let isTarget =
          isTarget
          |> Option.map(fun t -> t = canceller)
          |> Option.defaultValue false

        if isRequester then
          return [| ScenarioChange.RemovePendingPartyDuel canceller |]
        else if isTarget then
          return [| ScenarioChange.RemovePendingPartyDuel otherParty |]
        else
          return Array.empty
      else
        return Array.empty
    }


module Duel =
  let private canDuel(scenario: Scenario) =
    match scenario.EngagementMode with
    | Structured ->
      match scenario.CombatType with
      | PvP
      | PvH -> true
      | _ -> false
    | _ -> false

  let request
    (requester: Guid<EntityId>)
    (target: Guid<EntityId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      if canDuel scenarioState.scenario then
        return [| ScenarioChange.AddPendingDuel(requester, target) |]
      else
        return Array.empty
    }

  let accept
    (accepter: Guid<EntityId>)
    (requester: Guid<EntityId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      if canDuel scenarioState.scenario then
        let! pendingDuels = scenarioState.pendingDuels |> AMap.tryFind requester

        match pendingDuels with
        | Some target when target = accepter ->
          let newGuid = %Guid.NewGuid()
          let! gameTime = scenarioState.gameTime

          let battleInstance = {
            Id = newGuid
            Participants = HashSet.ofList [ accepter; requester ]
            StartTick = gameTime
          }

          return [|
            ScenarioChange.RemovePendingDuel requester
            ScenarioChange.AddBattleInstance battleInstance
          |]
        | _ -> return Array.empty
      else
        return Array.empty
    }

  let cancel
    (canceller: Guid<EntityId>)
    (otherPlayer: Guid<EntityId>)
    (scenarioState: ScenarioState)
    : aval<ScenarioChange[]> =
    adaptive {
      if canDuel scenarioState.scenario then
        let! found = scenarioState.pendingDuels |> AMap.tryFind canceller
        // Can be cancelled by either party
        let isRequester = found.IsSome

        let! isTarget = scenarioState.pendingDuels |> AMap.tryFind otherPlayer

        let isTarget =
          isTarget
          |> Option.map(fun t -> t = canceller)
          |> Option.defaultValue false

        if isRequester then
          return [| ScenarioChange.RemovePendingDuel canceller |]
        else if isTarget then
          return [| ScenarioChange.RemovePendingDuel otherPlayer |]
        else
          return Array.empty
      else
        return Array.empty
    }
