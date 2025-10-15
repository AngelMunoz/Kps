namespace Pomo.Lib.Tests

open Xunit
open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.State
open Pomo.Lib.Scenario
open Pomo.Lib.BattleManager


module private BattleManagerTestHelpers =
  let createScenarioState engagementMode combatType =
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
                  CombatType = combatType
            }
      })

    scenarioState

type ``Battle Instance Lifecycle``() =

  [<Fact>]
  member _.``Create battle instance``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    let actorId = %Guid.NewGuid()
    let targetId = %Guid.NewGuid()

    let changes =
      BattleInstanceLifecycle.create actorId targetId scenarioState
      |> AVal.force

    Assert.Equal(1, changes.Length)

    match changes.[0] with
    | ScenarioChange.AddBattleInstance inst ->
      Assert.True(inst.Participants.Contains actorId)
      Assert.True(inst.Participants.Contains targetId)
    | _ -> Assert.Fail("Expected AddBattleInstance")

  [<Fact>]
  member _.``Join battle instance``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    let actorId = %Guid.NewGuid()
    let targetId = %Guid.NewGuid()
    let thirdPersonId = %Guid.NewGuid()
    let battleInstanceId = %Guid.NewGuid()

    transact(fun _ ->
      scenarioState.battleInstances.Add(
        battleInstanceId,
        {
          Id = battleInstanceId
          Participants = HashSet.ofList [ actorId; targetId ]
          StartTick = 0L<Tick>
        }
      )
      |> ignore)

    let changes =
      BattleInstanceLifecycle.join thirdPersonId battleInstanceId scenarioState
      |> AVal.force

    Assert.Equal(1, changes.Length)

    match changes.[0] with
    | ScenarioChange.UpdateBattleInstance inst ->
      Assert.Equal(3, inst.Participants.Count)
      Assert.True(inst.Participants.Contains thirdPersonId)
    | _ -> Assert.Fail("Expected UpdateBattleInstance")

  [<Fact>]
  member _.``Join non-existent battle instance creates new one``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    let actorId = %Guid.NewGuid()
    let battleInstanceId = %Guid.NewGuid()

    let changes =
      BattleInstanceLifecycle.join actorId battleInstanceId scenarioState
      |> AVal.force

    Assert.Equal(1, changes.Length)

    match changes.[0] with
    | ScenarioChange.AddBattleInstance inst ->
      Assert.Equal(1, inst.Participants.Count)
      Assert.True(inst.Participants.Contains actorId)
    | _ -> Assert.Fail("Expected AddBattleInstance")

  [<Fact>]
  member _.``Leave battle instance and update``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    let actorId = %Guid.NewGuid()
    let targetId = %Guid.NewGuid()
    let thirdPersonId = %Guid.NewGuid()
    let battleInstanceId = %Guid.NewGuid()

    transact(fun _ ->
      scenarioState.battleInstances.Add(
        battleInstanceId,
        {
          Id = battleInstanceId
          Participants = HashSet.ofList [ actorId; targetId; thirdPersonId ]
          StartTick = 0L<Tick>
        }
      )
      |> ignore)

    let changes =
      BattleInstanceLifecycle.leave actorId scenarioState |> AVal.force

    Assert.Equal(1, changes.Length)

    match changes.[0] with
    | ScenarioChange.UpdateBattleInstance inst ->
      Assert.Equal(2, inst.Participants.Count)
      Assert.False(inst.Participants.Contains actorId)
    | _ -> Assert.Fail("Expected UpdateBattleInstance")

  [<Fact>]

  member _.``Leave battle instance and dissolve``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    let actorId = %Guid.NewGuid()
    let targetId = %Guid.NewGuid()
    let battleInstanceId = %Guid.NewGuid()

    transact(fun _ ->
      scenarioState.battleInstances.Add(
        battleInstanceId,
        {
          Id = battleInstanceId
          Participants = HashSet.ofList [ actorId; targetId ]
          StartTick = 0L<Tick>
        }
      )
      |> ignore)

    let changes =
      BattleInstanceLifecycle.leave actorId scenarioState |> AVal.force

    Assert.Equal(1, changes.Length)

    Assert.Equal(
      ScenarioChange.RemoveBattleInstance battleInstanceId,
      changes.[0]
    )

type ``Duel Tests``() =
  let requesterId = %Guid.NewGuid()
  let accepterId = %Guid.NewGuid()

  [<Fact>]
  member _.``Request duel in structured PvP scenario``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    let changes =
      Duel.request requesterId accepterId scenarioState |> AVal.force

    Assert.Equal(1, changes.Length)

    Assert.Equal(
      ScenarioChange.AddPendingDuel(requesterId, accepterId),
      changes[0]
    )

  [<Fact>]
  member _.``Request duel in peaceful scenario fails``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Peaceful
        ScenarioCombatType.PvP

    let changes =
      Duel.request requesterId accepterId scenarioState |> AVal.force

    Assert.Empty(changes)

  [<Fact>]
  member _.``Request duel in always-on scenario fails``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.AlwaysOn
        ScenarioCombatType.PvP

    let changes =
      Duel.request requesterId accepterId scenarioState |> AVal.force

    Assert.Empty(changes)

  [<Fact>]
  member _.``Request duel in structured PvE scenario fails``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvE

    let changes =
      Duel.request requesterId accepterId scenarioState |> AVal.force

    Assert.Empty(changes)

  [<Fact>]
  member _.``Accept duel in structured PvP scenario creates battle instance``
    ()
    =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    transact(fun _ ->
      scenarioState.pendingDuels.Add(requesterId, accepterId) |> ignore)

    let changes = Duel.accept accepterId requesterId scenarioState |> AVal.force
    Assert.Equal(2, changes.Length)
    Assert.Equal(ScenarioChange.RemovePendingDuel(requesterId), changes[0])

    match changes[1] with
    | ScenarioChange.AddBattleInstance _ -> Assert.True(true)
    | _ -> Assert.Fail("Expected AddBattleInstance")

  [<Fact>]
  member _.``Accept duel in peaceful scenario does not create battle instance``
    ()
    =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Peaceful
        ScenarioCombatType.PvP

    let changes = Duel.accept accepterId requesterId scenarioState |> AVal.force
    Assert.Empty(changes)

  [<Fact>]
  member _.``Cancel duel by requester``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    transact(fun _ ->
      scenarioState.pendingDuels.Add(requesterId, accepterId) |> ignore)

    let changes = Duel.cancel requesterId accepterId scenarioState |> AVal.force
    Assert.Equal(1, changes.Length)
    Assert.Equal(ScenarioChange.RemovePendingDuel(requesterId), changes[0])

  [<Fact>]
  member _.``Cancel duel by accepter``() =
    let scenarioState =
      BattleManagerTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    transact(fun _ ->
      scenarioState.pendingDuels.Add(requesterId, accepterId) |> ignore)

    let changes = Duel.cancel accepterId requesterId scenarioState |> AVal.force
    Assert.Equal(1, changes.Length)
    Assert.Equal(ScenarioChange.RemovePendingDuel(requesterId), changes[0])
