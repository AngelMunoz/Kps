namespace Pomo.Lib.Tests

open Xunit
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain
open Pomo.Lib.Scenario
open Pomo.Lib.ScenarioTransitions
open Pomo.Lib.Tests.TestHelpers
open System

type ``Scenario Transition Tests``() =

  let createTestEntity(pos: Position) = {
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
      Status = Pomo.Lib.Domain.Attributes.Alive
    }
    Position = pos
    Movement = {
      Speed = 100f
      Destination = ValueNone
      Path = []
    }
    Factions = HashSet.ofList [ Player ]
    Abilities = HashSet.empty
    Equipment = HashMap.empty
    PartyId = ValueNone
  }

  [<Fact>]
  member _.``Transition detection works for nearby entities``() =
    let transitions = [|
      {
        FromPosition = { X = 100f; Y = 100f }
        ToScenarioId = %Guid.NewGuid()
        ToPosition = { X = 50f; Y = 50f }
      }
    |]

    let entityPos = { X = 105f; Y = 105f } // Within range
    let result = TransitionDetection.checkProximity entityPos transitions

    match result with
    | ValueSome trigger ->
      Assert.Equal(100f, trigger.Position.X)
      Assert.Equal(100f, trigger.Position.Y)
      Assert.Equal(32f, trigger.Range)
    | ValueNone -> Assert.True(false, "Expected to find transition trigger")

  [<Fact>]
  member _.``Transition detection fails for distant entities``() =
    let transitions = [|
      {
        FromPosition = { X = 100f; Y = 100f }
        ToScenarioId = %Guid.NewGuid()
        ToPosition = { X = 50f; Y = 50f }
      }
    |]

    let entityPos = { X = 200f; Y = 200f } // Too far away
    let result = TransitionDetection.checkProximity entityPos transitions

    Assert.Equal(ValueNone, result)

  [<Fact>]
  member _.``Multiple transitions can be checked``() =
    let transitions = [|
      {
        FromPosition = { X = 100f; Y = 100f }
        ToScenarioId = %Guid.NewGuid()
        ToPosition = { X = 50f; Y = 50f }
      }
      {
        FromPosition = { X = 200f; Y = 200f }
        ToScenarioId = %Guid.NewGuid()
        ToPosition = { X = 150f; Y = 150f }
      }
    |]

    let entityPos1 = { X = 105f; Y = 105f } // Near first transition
    let result1 = TransitionDetection.checkProximity entityPos1 transitions

    match result1 with
    | ValueSome trigger ->
      Assert.Equal({ X = 100f; Y = 100f }, trigger.Position)
      Assert.Equal({ X = 50f; Y = 50f }, trigger.ToPosition)
    | ValueNone -> Assert.True(false, "Should have found first transition")

    let entityPos2 = { X = 205f; Y = 205f } // Near second transition
    let result2 = TransitionDetection.checkProximity entityPos2 transitions

    match result2 with
    | ValueSome trigger ->
      Assert.Equal({ X = 200f; Y = 200f }, trigger.Position)
      Assert.Equal({ X = 150f; Y = 150f }, trigger.ToPosition)
    | ValueNone -> Assert.True(false, "Should have found second transition")

  [<Fact>]
  member _.``Entity state preservation works correctly``() =
    let entity = {
      createTestEntity({ X = 100f; Y = 100f }) with
          Movement = {
            Speed = 100f
            Destination = ValueSome { X = 200f; Y = 200f }
            Path = [ { X = 150f; Y = 150f }; { X = 200f; Y = 200f } ]
          }
    }

    let preserved = TransitionExecution.preserveEntityState entity

    // Check that position and other stats are preserved
    Assert.Equal(100f, preserved.Position.X)
    Assert.Equal(100f, preserved.Position.Y)
    Assert.Equal(100, preserved.Resources.HP)
    Assert.Equal(100f, preserved.Movement.Speed)

    // Check that movement state is reset
    Assert.Equal(ValueNone, preserved.Movement.Destination)
    Assert.Empty(preserved.Movement.Path)

  [<Fact>]
  member _.``Entity migration works correctly``() =
    let entityId = %Guid.NewGuid()
    let entity = createTestEntity({ X = 100f; Y = 100f })
    let newPosition = { X = 50f; Y = 50f }
    let fromScenarioId = %Guid.NewGuid()
    let toScenarioId = %Guid.NewGuid()

    let struct (struct (targetId, updatedEntity),
                struct (sourceId, entityToRemove)) =
      TransitionExecution.migrateEntity
        entityId
        entity
        newPosition
        fromScenarioId
        toScenarioId

    Assert.Equal(toScenarioId, targetId)
    Assert.Equal(fromScenarioId, sourceId)
    Assert.Equal(entityId, entityToRemove)
    Assert.Equal(50f, updatedEntity.Position.X)
    Assert.Equal(50f, updatedEntity.Position.Y)

  [<Fact>]
  member _.``Visual transition effects work correctly``() =
    let effect = VisualTransitionEffects.createFadeEffect 2.0f

    Assert.Equal("fade", effect.EffectType)
    Assert.Equal(2.0f, effect.Duration)
    Assert.Equal(0.0f, effect.Progress)
    Assert.True(effect.IsActive)

    // Update effect halfway through
    let halfwayEffect = VisualTransitionEffects.updateEffect 1.0f effect
    Assert.Equal(0.5f, halfwayEffect.Progress)
    Assert.True(halfwayEffect.IsActive)

    // Update effect to completion
    let completedEffect =
      VisualTransitionEffects.updateEffect 1.0f halfwayEffect

    Assert.Equal(1.0f, completedEffect.Progress)
    Assert.False(completedEffect.IsActive)

  [<Fact>]
  member _.``Fade alpha calculation works correctly``() =
    let effect = VisualTransitionEffects.createFadeEffect 2.0f

    // At start (0% progress) - should be fully opaque
    let startAlpha = VisualTransitionEffects.getFadeAlpha effect
    Assert.Equal(1.0f, startAlpha)

    // At 25% progress (fade out phase) - should be 50% alpha
    let fadeOutEffect = { effect with Progress = 0.25f }
    let fadeOutAlpha = VisualTransitionEffects.getFadeAlpha fadeOutEffect
    Assert.Equal(0.5f, fadeOutAlpha)

    // At 50% progress (minimum alpha) - should be 0% alpha
    let minEffect = { effect with Progress = 0.5f }
    let minAlpha = VisualTransitionEffects.getFadeAlpha minEffect
    Assert.Equal(0.0f, minAlpha)

    // At 75% progress (fade in phase) - should be 50% alpha
    let fadeInEffect = { effect with Progress = 0.75f }
    let fadeInAlpha = VisualTransitionEffects.getFadeAlpha fadeInEffect
    Assert.Equal(0.5f, fadeInAlpha)

    // At 100% progress (complete) - should be fully opaque
    let endEffect = { effect with Progress = 1.0f }
    let endAlpha = VisualTransitionEffects.getFadeAlpha endEffect
    Assert.Equal(1.0f, endAlpha)

  [<Fact>]
  member _.``Connected scenarios are properly linked``() =
    let (town, wilderness, dungeon) =
      ScenarioDefinitions.createConnectedScenarios()

    Assert.Equal("Peaceful Town", town.Name)
    Assert.Equal("Dark Wilderness", wilderness.Name)
    Assert.Equal("Ancient Dungeon", dungeon.Name)

    // Check that town has battle disabled
    Assert.False(town.BattleEnabled)

    // Check that wilderness and dungeon have battle enabled
    Assert.True(wilderness.BattleEnabled)
    Assert.True(dungeon.BattleEnabled)

    // Check different combat types
    Assert.Equal(PvE, town.CombatType)
    Assert.Equal(PvE, wilderness.CombatType)
    Assert.Equal(PvH, dungeon.CombatType)

  [<Fact>]
  member _.``Town scenario has correct layout``() =
    let townId = %Guid.NewGuid()
    let town = ScenarioDefinitions.createTownScenario townId

    Assert.Equal(800f, town.BoundsWidth)
    Assert.Equal(600f, town.BoundsHeight)
    Assert.Equal(2, town.TerrainObjects.Count) // House + fountain
    Assert.Single(town.VisualLayers) |> ignore // Background
    Assert.Single(town.Transitions) |> ignore // Exit to wilderness

    // Check that we have a building and fountain
    let terrainArray = town.TerrainObjects |> IndexList.toArray

    let blockedObjects =
      terrainArray |> Array.filter(fun obj -> obj.TerrainType = Blocked)

    let waterObjects =
      terrainArray |> Array.filter(fun obj -> obj.TerrainType = Water)

    Assert.Single(blockedObjects) |> ignore // House
    Assert.Single(waterObjects) // Fountain

  [<Fact>]
  member _.``Wilderness scenario has varied terrain``() =
    let wildernessId = %Guid.NewGuid()
    let townId = %Guid.NewGuid()

    let wilderness =
      ScenarioDefinitions.createWildernessScenario wildernessId townId

    Assert.Equal(1000f, wilderness.BoundsWidth)
    Assert.Equal(800f, wilderness.BoundsHeight)
    Assert.Equal(3, wilderness.TerrainObjects.Count) // Forest + swamp + hazard
    Assert.Equal(2, wilderness.Transitions.Length) // To town + to dungeon

    // Check terrain variety
    let terrainArray = wilderness.TerrainObjects |> IndexList.toArray

    let blockedCount =
      terrainArray
      |> Array.filter(fun obj -> obj.TerrainType = Blocked)
      |> Array.length

    let waterCount =
      terrainArray
      |> Array.filter(fun obj -> obj.TerrainType = Water)
      |> Array.length

    let hazardCount =
      terrainArray
      |> Array.filter(fun obj -> obj.TerrainType = Hazard)
      |> Array.length

    Assert.Equal(1, blockedCount) // Forest
    Assert.Equal(1, waterCount) // Swamp
    Assert.Equal(1, hazardCount) // Poisonous plants

  [<Fact>]
  member _.``Dungeon scenario has appropriate features``() =
    let dungeonId = %Guid.NewGuid()
    let wildernessId = %Guid.NewGuid()

    let dungeon =
      ScenarioDefinitions.createDungeonScenario dungeonId wildernessId

    Assert.Equal(600f, dungeon.BoundsWidth)
    Assert.Equal(600f, dungeon.BoundsHeight)
    Assert.Equal(3, dungeon.TerrainObjects.Count) // Wall + lava + water trap
    Assert.Single(dungeon.Transitions) |> ignore // Back to wilderness
    Assert.Equal(PvH, dungeon.CombatType) // More dangerous combat

    // Check terrain types for dungeon features
    let terrainArray = dungeon.TerrainObjects |> IndexList.toArray

    let hazardCount =
      terrainArray
      |> Array.filter(fun obj -> obj.TerrainType = Hazard)
      |> Array.length

    Assert.Equal(1, hazardCount) // Lava pit
