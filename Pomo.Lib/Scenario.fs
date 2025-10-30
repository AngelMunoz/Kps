namespace Pomo.Lib.Scenario

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Scenario

module ScenarioState =

  let create
    (configure: ScenarioState -> ScenarioState)
    (scenarioParams: CreateScenarioParams)
    : ScenarioState =
    let baseScenario = {
      scenario = {
        Id = scenarioParams.Id
        Name = scenarioParams.Name
        BoundsWidth = scenarioParams.BoundsWidth
        BoundsHeight = scenarioParams.BoundsHeight
        BattleEnabled = false
        CombatType = PvE
        EngagementMode = Peaceful
        TerrainObjects = IndexList.empty
        VisualLayers = Array.empty
        Transitions = Array.empty
      }
      entities = cmap()
      gameTime = cval TimeSpan.Zero
      battleContext = ValueNone
      battleInstances = cmap()
      pendingDuels = cmap()
      pendingPartyDuels = cmap()
      parties = cmap()
      floatingTexts = cmap()
      projectiles = cmap()
      aoes = cmap()
      impacts = cmap()
      pendingResolutions = cmap()
      aiControllers = cmap()
      activeZones = cmap()
    }

    configure baseScenario

module ScenarioManager =
  open Pomo.Lib.Domain.State

  let createScenarioState
    (configure: ScenarioState -> ScenarioState)
    (scenario: Scenario)
    : ScenarioState =
    configure {
      scenario = scenario
      entities = cmap()
      gameTime = cval TimeSpan.Zero
      battleContext = ValueNone
      battleInstances = cmap()
      pendingDuels = cmap()
      pendingPartyDuels = cmap()
      parties = cmap()
      floatingTexts = cmap()
      projectiles = cmap()
      aoes = cmap()
      impacts = cmap()
      pendingResolutions = cmap()
      aiControllers = cmap()
      activeZones = cmap()
    }

  let getScenarioState
    (scenarioId: Guid<ScenarioId>)
    (gameState: GameStateScenarios)
    =
    gameState.scenarios |> AMap.tryFind scenarioId

  let addEntityToScenario
    (entityId: Guid<EntityId>)
    (entityComponents: EntityComponents)
    : StateChange =

    {
      StateChange.empty with
          additions = HashMap.single entityId entityComponents
    }

  let removeEntityFromScenario
    (scenarioId: Guid<ScenarioId>)
    (entityId: Guid<EntityId>)
    : StateChange =
    {
      StateChange.empty with
          removals = [| entityId |]
    }

  let listScenarios(gameState: GameStateScenarios) =
    gameState.scenarios |> AMap.toAVal |> AVal.map(HashMap.toArrayV)
