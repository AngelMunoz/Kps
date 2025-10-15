namespace Pomo.Lib.BattleManager

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Scenario

module BattleInstanceLifecycle =

  let create
    (actorId: Guid<EntityId>)
    (targetId: Guid<EntityId>)
    (scenarioState: ScenarioState)
    =
    transact(fun _ ->
      let newGuid = %Guid.NewGuid()

      scenarioState.battleInstances.Add(
        newGuid,
        {
          Id = newGuid
          Participants = HashSet.ofList [ actorId; targetId ]
          StartTick = scenarioState.gameTime.Value
        }
      )
      |> ignore)

  let join
    (actorId: Guid<EntityId>)
    (battleInstanceId: Guid<BattleInstanceId>)
    (scenarioState: ScenarioState)
    =
    let found =
      scenarioState.battleInstances
      |> AMap.force
      |> HashMap.tryFindV battleInstanceId


    match found with
    | ValueSome existing ->
      transact(fun _ ->

        scenarioState.battleInstances[battleInstanceId] <-
          {
            existing with
                Participants = existing.Participants |> HashSet.add actorId
          })
    | ValueNone ->
      transact(fun _ ->
        let newGuid = %Guid.NewGuid()

        scenarioState.battleInstances.Add(
          newGuid,
          {
            Id = newGuid
            Participants = HashSet.single actorId
            StartTick = scenarioState.gameTime.Value
          }
        )
        |> ignore)

  let leave (actorId: Guid<EntityId>) (scenarioState: ScenarioState) =
    let instanceMap = scenarioState.battleInstances |> AMap.force

    let found =
      instanceMap
      |> HashMap.toArrayV
      |> Array.tryFind(fun struct (k, v) -> v.Participants.Contains actorId)

    match found with
    | Some struct (battleInstanceId, existing) ->
      transact(fun _ ->
        let updatedParticipants = existing.Participants |> HashSet.remove actorId
        if updatedParticipants.Count < 2 then
            scenarioState.battleInstances.Remove(battleInstanceId) |> ignore
        else
            scenarioState.battleInstances[battleInstanceId] <-
              {
                existing with
                    Participants = updatedParticipants
              })
    | None -> ()
