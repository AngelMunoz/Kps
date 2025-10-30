namespace Pomo.Lib.Tests

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.Scenario

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
    DerivedStats.byGameState state |> AVal.force |> AMap.force

  let getDerivedStat (state: GameState) (id: Guid<EntityId>) =
    let derivedStats = getDerivedStats state
    derivedStats |> HashMap.find id

  let createTestEntity() = {
    Identity = {
      Family = Classification.Power
      Stage = Classification.First
    }
    BaseStats = {
      Power = 10
      Magic = 5
      Sense = 5
      Charm = 8
    }
    Resources = {
      HP = 80
      MP = 25
      Status = Attributes.Alive
    }
    Position = { X = 0f; Y = 0f }
    Movement = {
      Destination = ValueNone
      Path = []
    }
    AbilityCooldowns = HashMap.empty
    Effects = HashMap.empty
    Factions = HashSet.ofList [ Classification.Player ]
    Abilities = HashSet.empty
    EquippedItems = HashMap.empty
    Inventory = HashMap.empty
    PartyId = ValueNone
  }

[<AutoOpen>]
module Tuple =
  let inline fstV struct (a, _) = a
  let inline sndV struct (_, b) = b
