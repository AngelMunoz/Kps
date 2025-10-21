namespace Pomo.Core

open System
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.State
open Pomo.Lib.Content
open Pomo.Lib.Operations
open Pomo.Lib.Pathfinding
open Pomo.Lib.Gameplay
open FSharp.Data.Adaptive

module ScenarioLoader =

  let loadScenario(state: GameState, scenario: Scenario) =
    transact(fun _ ->

      let scenarioState = ScenarioState.create scenario
      state.scenarios[scenario.Id] <- scenarioState
      state.activeScenarioId.Value <- scenario.Id

      AudioSystem.updateScenarioMusic state.services.audioStore scenario.Id)

    Grid.createGrid scenario 32.0f 16.0f

module CharacterBuilder =

  let createCharacter
    (state: GameState)
    (profession: Profession)
    (faction: Faction)
    (position: Position)
    : Guid<EntityId> =
    let kit = CharacterKitStore.definitions[profession]

    let change =
      kit
      |> GameState.createEntity(fun stats -> {
        stats with
            Factions = HashSet.ofList [ faction ]
            Position = position
      })

    let entityId = change.additions |> HashMap.toKeySeq |> Seq.head
    GameState.apply state change
    entityId

module TestScenarioBuilder =

  type TestScenarioData = {
    PlayerId: Guid<EntityId>
    EnemyId: Guid<EntityId>
    NavigationGrid: PathfindingGrid
  }

  let createDefaultScenario(state: GameState) : TestScenarioData =
    let struct (town, wilderness, dungeon) =
      ScenarioDefinitions.createConnectedScenarios()

    let grid = ScenarioLoader.loadScenario(state, wilderness)

    let playerId =
      CharacterBuilder.createCharacter
        state
        { Family = Magic; Stage = Second }
        Player
        { X = 100f; Y = 400f }

    let enemyId =
      CharacterBuilder.createCharacter
        state
        { Family = Charm; Stage = First }
        Enemy
        { X = 300f; Y = 400f }

    {
      PlayerId = playerId
      EnemyId = enemyId
      NavigationGrid = grid
    }
