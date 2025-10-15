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

type ScenarioState = {
  scenario: Scenario
  entities: cmap<Guid<EntityId>, EntityComponents>
  gameTime: cval<int64<Tick>>
  battleContext: BattleContext voption
  battleInstances: cmap<Guid<BattleInstanceId>, BattleInstance>
  pendingDuels: cmap<Guid<EntityId>, Guid<EntityId>>
  pendingPartyDuels: cmap<Guid<PartyId>, Guid<PartyId>>
  parties: cmap<Guid<PartyId>, Party>
}

type GameStateScenarios = {
  scenarios: cmap<Guid<ScenarioId>, ScenarioState>
  activeScenarioId: Guid<ScenarioId> cval
}

[<Struct>]
type CreateScenarioParams = {
  Id: Guid<ScenarioId>
  Name: string
  BoundsWidth: float32
  BoundsHeight: float32
}

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
      gameTime = cval 0L<Tick>
      battleContext = ValueNone
      battleInstances = cmap()
      pendingDuels = cmap()
      pendingPartyDuels = cmap()
      parties = cmap()
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
      gameTime = cval 0L<Tick>
      battleContext = ValueNone
      battleInstances = cmap()
      pendingDuels = cmap()
      pendingPartyDuels = cmap()
      parties = cmap()
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
      updates = HashMap.empty
      additions = HashMap.single entityId entityComponents
      removals = [||]
      gameTime = ValueNone
      scenarioChanges = Array.empty
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
      scenarioChanges = Array.empty
    }

  let listScenarios(gameState: GameStateScenarios) =
    gameState.scenarios |> AMap.toAVal |> AVal.map(HashMap.toArrayV)
