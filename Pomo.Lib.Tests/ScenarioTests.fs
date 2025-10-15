namespace Pomo.Lib.Tests

open Xunit
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Gameplay
open Pomo.Lib.Scenario
open Pomo.Lib.Tests.TestHelpers
open System

type ``Scenario Management Tests``() =

  [<Fact>]
  member _.``Can create scenario state``() =
    let scenarioId = %Guid.NewGuid()

    let scenarioState =
      ScenarioState.create id {
        Id = scenarioId
        Name = "Test Scenario"
        BoundsWidth = 1000f
        BoundsHeight = 1500f
      }

    Assert.Equal(scenarioId, scenarioState.scenario.Id)
    Assert.Equal("Test Scenario", scenarioState.scenario.Name)
    Assert.Equal(1000f, scenarioState.scenario.BoundsWidth)
    Assert.Equal(1500f, scenarioState.scenario.BoundsHeight)
    Assert.Equal(ScenarioCombatType.PvE, scenarioState.scenario.CombatType)
    Assert.False(scenarioState.scenario.BattleEnabled)
    Assert.Empty(scenarioState.scenario.TerrainObjects)
    Assert.Empty(scenarioState.scenario.VisualLayers)
    Assert.Empty(scenarioState.scenario.Transitions)

  [<Fact>]
  member _.``Can add entity to scenario``() =
    let scenarioId = %Guid.NewGuid()
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>

    let entity = {
      AbilityCooldowns = HashMap.empty
      Effects = HashMap.empty
      Identity = {
        Family = Family.Power
        Stage = Stage.First
      }
      BaseStats = {
        Power = 10
        Magic = 10
        Sense = 10
        Charm = 10
      }
      Resources = {
        HP = 100
        MP = 100
        Status = Status.Alive
      }
      Position = { X = 0f; Y = 0f }
      Movement = {
        Speed = 0f
        Destination = ValueNone
        Path = []
      }
      Factions = HashSet.ofList [ Player ]
      Abilities = HashSet.empty
      Equipment = HashMap.empty
      PartyId = ValueNone
    }

    let stateChange = ScenarioManager.addEntityToScenario entityId entity

    Assert.True(stateChange.additions.ContainsKey entityId)
    Assert.Equal(entity, stateChange.additions[entityId])
    Assert.Empty(stateChange.updates)
    Assert.Empty(stateChange.removals)

  [<Fact>]
  member _.``Can remove entity from scenario``() =
    let scenarioId = %Guid.NewGuid()
    let entityId = Guid.NewGuid() |> UMX.tag<EntityId>

    let stateChange =
      ScenarioManager.removeEntityFromScenario scenarioId entityId

    Assert.Contains(entityId, stateChange.removals)
    Assert.Empty(stateChange.additions)
    Assert.Empty(stateChange.updates)

  [<Fact>]
  member _.``Can retrieve scenario state from game state``() =
    let state = GameState.create()

    let scenarios = {
      scenarios = state.scenarios
      activeScenarioId = state.activeScenarioId
    }

    let activeScenarioId = AVal.force state.activeScenarioId

    let retrievedScenario =
      ScenarioManager.getScenarioState activeScenarioId scenarios

    let scenarioOption = AVal.force retrievedScenario
    Assert.True(scenarioOption.IsSome)
    let scenario = scenarioOption.Value
    Assert.Equal("Test Scenario", scenario.scenario.Name)
    Assert.Equal(2000f, scenario.scenario.BoundsWidth)
    Assert.Equal(2000f, scenario.scenario.BoundsHeight)

  [<Fact>]
  member _.``Can list scenarios from game state``() =
    let state = GameState.create()

    let scenarios = {
      scenarios = state.scenarios
      activeScenarioId = state.activeScenarioId
    }

    let scenarioList = ScenarioManager.listScenarios scenarios |> AVal.force

    Assert.NotEmpty(scenarioList)
    let struct (id, scenario) = scenarioList.[0]
    let name = scenario.scenario.Name
    Assert.Equal("Test Scenario", name)

  [<Fact>]
  member _.``Scenario has proper terrain object types``() =
    let terrainObject = {
      Id = %Guid.NewGuid()
      Position = { X = 100f; Y = 200f }
      CollisionGeometry = Circle({ X = 100f; Y = 200f }, 50f)
      TerrainType = TerrainType.Water
      DepthLayer = 0.5f
      SpriteId = ValueSome "water_tile"
    }

    Assert.Equal(TerrainType.Water, terrainObject.TerrainType)

    match terrainObject.CollisionGeometry with
    | Circle(center, radius) ->
      Assert.Equal(100f, center.X)
      Assert.Equal(200f, center.Y)
      Assert.Equal(50f, radius)
    | _ -> Assert.True(false, "Expected Circle collision geometry")

  [<Fact>]
  member _.``Visual layer has proper parallax settings``() =
    let visualLayer = {
      SpriteId = "background_mountains"
      Position = { X = 0f; Y = 0f }
      DepthLayer = 0.1f
      Parallax = 0.5f
    }

    Assert.Equal("background_mountains", visualLayer.SpriteId)
    Assert.Equal(0.1f, visualLayer.DepthLayer)
    Assert.Equal(0.5f, visualLayer.Parallax)
