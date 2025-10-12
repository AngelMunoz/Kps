namespace Pomo.Lib.Tests

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components

module TestHelpers =

  let getActiveScenario(state: GameState) =
    let activeId = state.activeScenarioId.Value
    state.scenarios[activeId]

  let getEntities(state: GameState) = (getActiveScenario state).entities

  let getGameTime(state: GameState) = (getActiveScenario state).gameTime

  let getEntity (state: GameState) (id: Guid<EntityId>) =
    let entities = getEntities state
    entities[id]

  let setEntity
    (state: GameState)
    (id: Guid<EntityId>)
    (components: EntityComponents)
    =
    let scenario = getActiveScenario state
    transact(fun _ -> scenario.entities[id] <- components)

  let addEntity
    (state: GameState)
    (id: Guid<EntityId>)
    (components: EntityComponents)
    =
    let scenario = getActiveScenario state
    transact(fun _ -> scenario.entities.Add(id, components) |> ignore)

  let getDerivedStats(state: GameState) =
    GameState.getDerivedStats state |> AVal.force |> AMap.force

  let getDerivedStat (state: GameState) (id: Guid<EntityId>) =
    let derivedStats = getDerivedStats state
    derivedStats[id]
