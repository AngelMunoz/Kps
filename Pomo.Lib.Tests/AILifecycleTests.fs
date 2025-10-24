module Pomo.Lib.Tests.AILifecycleTests

open System
open Xunit
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.AI
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.State
open Pomo.Lib.EnemyAI
open Pomo.Lib.Rules
open Pomo.Lib.Operations
open Pomo.Lib.Content
open Pomo.Lib.Scenario
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Domain.Services

let createGameState rng =

  let initialScenarioId = %Guid.NewGuid()

  let initialScenarioState =
    {
      Id = initialScenarioId
      Name = "Test Scenario"
      BoundsWidth = 2000f
      BoundsHeight = 2000f
    }
    |> ScenarioState.create(fun sc -> {
      sc with
          scenario.EngagementMode = EngagementMode.AlwaysOn
    })

  let scenarios = cmap [ (initialScenarioId, initialScenarioState) ]

  GameState.create'
    {
      effectStore =
        { new IEffectStore with
            member _.tryFind effectId =
              EffectStore.definitions |> HashMap.tryFindV effectId

            member _.find effectId =
              EffectStore.definitions |> HashMap.find effectId
        }
      abilityStore =
        { new IAbilityStore with
            member _.tryFind abilityId =
              AbilityStore.definitions |> HashMap.tryFindV abilityId

            member _.find abilityId =
              AbilityStore.definitions |> HashMap.find abilityId
        }
      formulaStore =
        { new IFormulaStore with
            member _.tryFind formulaId =
              FormulaStore.definitions |> HashMap.tryFindV formulaId

            member _.find formulaId =
              FormulaStore.definitions |> HashMap.find formulaId
        }
      projectileStore =
        { new IProjectileStore with
            member _.tryFind projectileId =
              ProjectileStore.definitions |> HashMap.tryFindV projectileId

            member _.find projectileId =
              ProjectileStore.definitions |> HashMap.find projectileId
        }
      aoeStore =
        { new IAoeStore with
            member _.tryFind aoeId =
              AoeStore.definitions |> HashMap.tryFindV aoeId

            member _.find aoeId =
              AoeStore.definitions |> HashMap.find aoeId
        }
      impactStore =
        { new IImpactStore with
            member _.tryFind impactId =
              ImpactStore.definitions |> HashMap.tryFindV impactId

            member _.find impactId =
              ImpactStore.definitions |> HashMap.find impactId
        }
      audioStore =
        { new IAudioStore with
            member _.tryFind clipId =
              AudioStore.definitions |> HashMap.tryFindV clipId

            member _.find clipId =
              AudioStore.definitions |> HashMap.find clipId

            member _.findByTrigger trigger =
              AudioStore.triggerMap
              |> HashMap.tryFindV trigger
              |> ValueOption.defaultValue Array.empty

            member _.findMusicForScenario scenarioId =
              let scenario =
                scenarios.Value
                |> HashMap.tryFindV scenarioId
                |> ValueOption.map _.scenario

              scenario
              |> ValueOption.bind(fun s ->
                AudioStore.scenarioMusicMap |> HashMap.tryFindV s.Name)
        }
      aiArchetypeStore =
        { new IAIArchetypeStore with
            member _.tryFind archetypeId =
              AIArchetypeStore.definitions |> HashMap.tryFindV archetypeId

            member _.find archetypeId =
              AIArchetypeStore.definitions |> HashMap.find archetypeId
        }
      itemStore =
        { new IItemStore with
            member _.tryFind itemId =
              ItemStore.definitions |> HashMap.tryFindV itemId

            member _.find itemId =
              ItemStore.definitions |> HashMap.find itemId
        }
      rng = fun () -> System.Random().NextDouble()
    }
    (initialScenarioId, scenarios)


[<Fact>]
let ``createController initializes with default state``() =
  let entityId = Guid.NewGuid() |> UMX.tag
  let archetypeId = 1<AiArchetypeId>
  let currentTime = TimeSpan.FromSeconds(10.0)

  let controller =
    AILifecycle.createController
      entityId
      archetypeId
      Position.zero
      ValueNone
      currentTime

  Assert.Equal(entityId, controller.controlledEntityId)
  Assert.Equal(archetypeId, controller.archetypeId)
  Assert.Equal(Idle, controller.currentState)
  Assert.Equal(ValueNone, controller.currentTarget)
  Assert.Equal(currentTime, controller.lastDecisionTime)
  Assert.True(HashMap.isEmpty controller.memories)
  Assert.Equal(0, controller.waypointIndex)
  Assert.Equal(currentTime, controller.stateEnterTime)

[<Fact>]
let ``cleanupDeadControllers removes controllers for dead entities``() =
  let entityId1 = Guid.NewGuid() |> UMX.tag
  let entityId2 = Guid.NewGuid() |> UMX.tag
  let entityId3 = Guid.NewGuid() |> UMX.tag

  let aliveEntity = {
    TestHelpers.createTestEntity() with
        Resources = { HP = 50; MP = 20; Status = Alive }
  }

  let deadEntity = {
    TestHelpers.createTestEntity() with
        Resources = { HP = 0; MP = 0; Status = Dead }
  }

  let entities =
    cmap [
      entityId1, aliveEntity
      entityId2, deadEntity
      entityId3, aliveEntity
    ]

  let controllers =
    cmap [
      entityId1,
      AILifecycle.createController
        entityId1
        1<AiArchetypeId>
        Position.zero
        ValueNone
        TimeSpan.Zero
      entityId2,
      AILifecycle.createController
        entityId2
        1<AiArchetypeId>
        Position.zero
        ValueNone
        TimeSpan.Zero
      entityId3,
      AILifecycle.createController
        entityId3
        1<AiArchetypeId>
        Position.zero
        ValueNone
        TimeSpan.Zero
    ]

  AILifecycle.cleanupDeadControllers entities controllers

  let remaining = controllers |> AMap.force

  Assert.Equal(2, HashMap.count remaining)
  Assert.True(HashMap.containsKey entityId1 remaining)
  Assert.False(HashMap.containsKey entityId2 remaining)
  Assert.True(HashMap.containsKey entityId3 remaining)

[<Fact>]
let ``AddEntitiesWithAI creates controllers for entities with archetype``() =
  let state = createGameState(fun _ -> 0.5)
  let entityId = Guid.NewGuid() |> UMX.tag
  let archetypeId = 1<AiArchetypeId>

  let components = {
    TestHelpers.createTestEntity() with
        Position = { X = 100f; Y = 100f }
  }

  let spawnData = {
    SpawnEntityData.components = components
    archetypeId = ValueSome archetypeId
  }

  let cmd = AddEntitiesWithAI(HashMap.single entityId spawnData)
  let change = CommandHandler.evaluate state cmd |> AVal.force

  Assert.Equal(1, HashMap.count change.additions)
  Assert.Equal(1, HashMap.count change.aiControllers)
  Assert.True(HashMap.containsKey entityId change.additions)
  Assert.True(HashMap.containsKey entityId change.aiControllers)

  let controller = HashMap.find entityId change.aiControllers
  Assert.Equal(entityId, controller.controlledEntityId)
  Assert.Equal(archetypeId, controller.archetypeId)

[<Fact>]
let ``AddEntitiesWithAI without archetype creates entity without controller``
  ()
  =
  let state = createGameState(fun _ -> 0.5)
  let entityId = Guid.NewGuid() |> UMX.tag

  let components = {
    TestHelpers.createTestEntity() with
        Position = { X = 100f; Y = 100f }
  }

  let spawnData = {
    SpawnEntityData.components = components
    archetypeId = ValueNone
  }

  let cmd = AddEntitiesWithAI(HashMap.single entityId spawnData)
  let change = CommandHandler.evaluate state cmd |> AVal.force

  Assert.Equal(1, HashMap.count change.additions)
  Assert.Equal(0, HashMap.count change.aiControllers)
  Assert.True(HashMap.containsKey entityId change.additions)

[<Fact>]
let ``AddEntitiesWithAI with invalid archetype creates entity without controller``
  ()
  =
  let state = createGameState(fun _ -> 0.5)
  let entityId = Guid.NewGuid() |> UMX.tag
  let invalidArchetypeId = 999<AiArchetypeId>

  let components = {
    TestHelpers.createTestEntity() with
        Position = { X = 100f; Y = 100f }
  }

  let spawnData = {
    SpawnEntityData.components = components
    archetypeId = ValueSome invalidArchetypeId
  }

  let cmd = AddEntitiesWithAI(HashMap.single entityId spawnData)
  let change = CommandHandler.evaluate state cmd |> AVal.force

  Assert.Equal(1, HashMap.count change.additions)
  Assert.Equal(0, HashMap.count change.aiControllers)

[<Fact>]
let ``RemoveEntities removes both entity and controller``() =
  let state = createGameState(fun _ -> 0.5)
  let entityId = Guid.NewGuid() |> UMX.tag

  let components = TestHelpers.createTestEntity()

  let controller =
    AILifecycle.createController
      entityId
      1<AiArchetypeId>
      Position.zero
      ValueNone
      TimeSpan.Zero

  transact(fun _ ->
    let activeId = state.activeScenarioId.Value
    let scenario = state.scenarios[activeId]
    scenario.entities.Add(entityId, components) |> ignore
    scenario.aiControllers.Add(entityId, controller) |> ignore)

  let cmd = RemoveEntities [ entityId ]
  let change = CommandHandler.evaluate state cmd |> AVal.force
  GameState.apply state change

  let activeId = state.activeScenarioId |> AVal.force
  let scenario = state.scenarios[activeId]
  let entities = scenario.entities |> AMap.force
  let controllers = scenario.aiControllers |> AMap.force

  Assert.False(HashMap.containsKey entityId entities)
  Assert.False(HashMap.containsKey entityId controllers)

[<Fact>]
let ``Dead entities have controllers removed on apply``() =
  let state = createGameState(fun _ -> 0.5)
  let entityId = Guid.NewGuid() |> UMX.tag

  let aliveEntity = {
    TestHelpers.createTestEntity() with
        Resources = { HP = 50; MP = 20; Status = Alive }
  }

  let controller =
    AILifecycle.createController
      entityId
      1<AiArchetypeId>
      Position.zero
      ValueNone
      TimeSpan.Zero

  transact(fun _ ->
    let activeId = state.activeScenarioId.Value
    let scenario = state.scenarios[activeId]
    scenario.entities.Add(entityId, aliveEntity) |> ignore
    scenario.aiControllers.Add(entityId, controller) |> ignore)

  let deadEntity = {
    aliveEntity with
        Resources = { HP = 0; MP = 0; Status = Dead }
  }

  let change = {
    StateChange.empty with
        updates = HashMap.single entityId deadEntity
  }

  GameState.apply state change

  let activeId = state.activeScenarioId |> AVal.force
  let scenario = state.scenarios[activeId]
  let controllers = scenario.aiControllers |> AMap.force

  Assert.False(HashMap.containsKey entityId controllers)
