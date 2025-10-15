namespace Pomo.Lib.Tests

open Xunit
open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Scenario
open Pomo.Lib.BattleManager
open Pomo.Lib.Tests.TestHelpers

module Tuple =
  let inline fstV struct (a, _) = a
  let inline sndV struct (_, b) = b


module private BattleManagerTestHelpers =
  let createScenarioState engagementMode =
    let scenarioId = %Guid.NewGuid()

    let scenarioState =
      {
        Id = scenarioId
        Name = "Test Scenario"
        BoundsWidth = 2000f
        BoundsHeight = 2000f
      }
      |> ScenarioState.create(fun sc -> {
        sc with
            scenario = {
              sc.scenario with
                  EngagementMode = engagementMode
            }
      })

    scenarioState

type ``Battle Instance Lifecycle``() =

  [<Fact>]
  member _.``Create battle instance``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState EngagementMode.Structured

    let actorId = %Guid.NewGuid()
    let targetId = %Guid.NewGuid()

    BattleInstanceLifecycle.create actorId targetId scenarioState

    let instances = scenarioState.battleInstances.Value
    Assert.Equal(1, instances.Count)
    let instance = instances |> HashMap.toArrayV |> Array.head |> Tuple.sndV
    Assert.True(instance.Participants.Contains actorId)
    Assert.True(instance.Participants.Contains targetId)

  [<Fact>]
  member _.``Join battle instance``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState EngagementMode.Structured

    let actorId = %Guid.NewGuid()
    let targetId = %Guid.NewGuid()
    let thirdPersonId = %Guid.NewGuid()

    BattleInstanceLifecycle.create actorId targetId scenarioState

    let instanceId =
      scenarioState.battleInstances.Value
      |> HashMap.toArrayV
      |> Array.head
      |> Tuple.fstV

    BattleInstanceLifecycle.join thirdPersonId instanceId scenarioState

    let instance = scenarioState.battleInstances.Value.[instanceId]
    Assert.Equal(3, instance.Participants.Count)
    Assert.True(instance.Participants.Contains thirdPersonId)

  [<Fact>]

  member _.``Leave battle instance and dissolve``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState EngagementMode.Structured

    let actorId = %Guid.NewGuid()
    let targetId = %Guid.NewGuid()

    BattleInstanceLifecycle.create actorId targetId scenarioState

    BattleInstanceLifecycle.leave actorId scenarioState
    let instances = scenarioState.battleInstances.Value
    Assert.True(instances.IsEmpty)
