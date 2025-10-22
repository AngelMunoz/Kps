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
open Pomo.Lib.Rules
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

  let createAICharacter
    (state: GameState)
    (archetypeId: int<AiArchetypeId>)
    (position: Position)
    : Guid<EntityId> =
    let archetype = state.services.aiArchetypeStore.find archetypeId
    let kit = archetype.characterKit

    let entityId = Guid.NewGuid() |> UMX.tag

    let components =
      kit
      |> GameState.createEntity(fun stats -> {
        stats with
            Position = position
            Factions = HashSet.ofList [ Enemy; AIControlled ]
      })

    let spawnData: Rules.SpawnEntityData = {
      components = components.additions |> HashMap.toValueArray |> Array.head
      archetypeId = ValueSome archetypeId
    }

    let cmd = Rules.AddEntitiesWithAI(HashMap.single entityId spawnData)
    let change = CommandHandler.evaluate state cmd |> AVal.force
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
      CharacterBuilder.createAICharacter
        state
        2<AiArchetypeId>
        { X = 300f; Y = 400f }

    {
      PlayerId = playerId
      EnemyId = enemyId
      NavigationGrid = grid
    }
