namespace Pomo.Lib.Tests

open Xunit
open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.State
open Pomo.Lib.Scenario
open Pomo.Lib.BattleManager
open Pomo.Lib.Tests.TestHelpers

module private PartyDuelTestHelpers =
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

type ``Party Duel Tests``() =
  let requesterId = %Guid.NewGuid()
  let accepterId = %Guid.NewGuid()
  let requesterPartyId = %Guid.NewGuid()
  let accepterPartyId = %Guid.NewGuid()

  let requesterParty = {
    Id = requesterPartyId
    Members = HashSet.ofList [ requesterId ]
    Name = "Requester Party"
  }

  let accepterParty = {
    Id = accepterPartyId
    Members = HashSet.ofList [ accepterId ]
    Name = "Accepter Party"
  }

  [<Fact>]
  member _.``Request party duel in structured PvP scenario``() =
    let scenarioState =
      PartyDuelTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    let changes =
      PartyDuel.request requesterPartyId accepterPartyId scenarioState |> AVal.force

    Assert.Equal(1, changes.Length)

    Assert.Equal(
      ScenarioChange.AddPendingPartyDuel(requesterPartyId, accepterPartyId),
      changes[0]
    )

  [<Fact>]
  member _.``Accept party duel in structured PvP scenario creates battle instance``() =
    let scenarioState =
      PartyDuelTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    transact (fun _ ->
      scenarioState.pendingPartyDuels.Add(requesterPartyId, accepterPartyId) |> ignore
      scenarioState.parties.Add(requesterPartyId, requesterParty) |> ignore
      scenarioState.parties.Add(accepterPartyId, accepterParty) |> ignore
    )

    let changes = PartyDuel.accept accepterPartyId requesterPartyId scenarioState |> AVal.force
    Assert.Equal(2, changes.Length)
    Assert.Equal(ScenarioChange.RemovePendingPartyDuel(requesterPartyId), changes[0])

    match changes.[1] with
    | ScenarioChange.AddBattleInstance inst ->
        Assert.True(inst.Participants.Contains requesterId)
        Assert.True(inst.Participants.Contains accepterId)
    | _ -> Assert.Fail("Expected AddBattleInstance")

  [<Fact>]
  member _.``Cancel party duel by requester``() =
    let scenarioState =
      PartyDuelTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    transact (fun _ ->
      scenarioState.pendingPartyDuels.Add(requesterPartyId, accepterPartyId) |> ignore
    )

    let changes = PartyDuel.cancel requesterPartyId accepterPartyId scenarioState |> AVal.force
    Assert.Equal(1, changes.Length)
    Assert.Equal(ScenarioChange.RemovePendingPartyDuel(requesterPartyId), changes[0])

  [<Fact>]
  member _.``Cancel party duel by accepter``() =
    let scenarioState =
      PartyDuelTestHelpers.createScenarioState
        EngagementMode.Structured
        ScenarioCombatType.PvP

    transact (fun _ ->
      scenarioState.pendingPartyDuels.Add(requesterPartyId, accepterPartyId) |> ignore
    )

    let changes = PartyDuel.cancel accepterPartyId requesterPartyId scenarioState |> AVal.force
    Assert.Equal(1, changes.Length)
    Assert.Equal(ScenarioChange.RemovePendingPartyDuel(requesterPartyId), changes[0])
