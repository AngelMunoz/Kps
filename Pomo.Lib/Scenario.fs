namespace Pomo.Lib.Scenario

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components



[<Struct>]
type ScenarioTransition = {
  FromPosition: Position
  ToScenarioId: Guid<ScenarioId>
  ToPosition: Position
  RequiresCondition: (unit -> bool) voption
}

type Scenario = {
  Id: Guid<ScenarioId>
  Name: string
  BoundsWidth: float32
  BoundsHeight: float32
  BattleEnabled: bool
  CombatType: ScenarioCombatType
  EngagementMode: EngagementMode
  TerrainObjects: TerrainObject IndexList
  VisualLayers: VisualLayer[]
  Transitions: ScenarioTransition[]
}

[<Struct>]
type ScenarioState = {
  scenario: Scenario
  entities: cmap<Guid<EntityId>, EntityComponents>
  gameTime: cval<int64<Tick>>
  battleContext: BattleContext voption
  battleInstances: BattleInstance[]
}

type GameStateScenarios = {
  scenarios: cmap<Guid<ScenarioId>, ScenarioState>
  activeScenarioId: Guid<ScenarioId> cval
}

module ScenarioState =
  let create id name boundsWidth boundsHeight : ScenarioState = {
    scenario = {
      Id = id
      Name = name
      BoundsWidth = boundsWidth
      BoundsHeight = boundsHeight
      BattleEnabled = false
      CombatType = PvE
      EngagementMode = Peaceful
      TerrainObjects = IndexList.empty
      VisualLayers = Array.empty
      Transitions = Array.empty
    }
    entities = cmap()
    gameTime = cval 0L<Tick>
    battleContext = ValueNone
    battleInstances = Array.empty
  }

module ScenarioManager =
  open Pomo.Lib.Domain.State

  let createScenarioState(scenario: Scenario) : ScenarioState = {
    scenario = scenario
    entities = cmap()
    gameTime = cval 0L<Tick>
    battleContext = ValueNone
    battleInstances = Array.empty
  }

  let getScenarioState
    (scenarioId: Guid<ScenarioId>)
    (gameState: GameStateScenarios)
    =
    gameState.scenarios |> AMap.tryFind scenarioId

  let addEntityToScenario
    (scenarioId: Guid<ScenarioId>)
    (entityId: Guid<EntityId>)
    (entityComponents: EntityComponents)
    : StateChange =
    {
      updates = HashMap.empty
      additions = HashMap.single entityId entityComponents
      removals = [||]
      gameTime = ValueNone
    }

  let removeEntityFromScenario
    (scenarioId: Guid<ScenarioId>)
    (entityId: Guid<EntityId>)
    : StateChange =
    {
      updates = HashMap.empty
      additions = HashMap.empty
      removals = [| entityId |]
      gameTime = ValueNone
    }

  let listScenarios(gameState: GameStateScenarios) =
    gameState.scenarios |> AMap.toAVal |> AVal.map(HashMap.toArrayV)
