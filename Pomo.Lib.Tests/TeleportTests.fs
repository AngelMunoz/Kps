namespace Pomo.Lib.Tests

open Xunit
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Gameplay
open Pomo.Lib.Scenario
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.State


module TeleportTests =
  open Pomo.Lib.Domain.CharacterKits

  [<Fact>]
  let ``teleport moves entity to target scenario with new position``() =
    let servicesState = GameState.create()

    // Create second scenario
    let secondId = %System.Guid.NewGuid()

    let secondScenarioState =
      {
        Id = secondId
        Name = "Second"
        BoundsWidth = 1000f
        BoundsHeight = 1000f
      }
      |> ScenarioState.create id

    // Add second scenario to state
    transact(fun _ ->
      servicesState.scenarios.Add(secondId, secondScenarioState) |> ignore)

    // Create entity via operations helper
    let kit: CharacterKit = {
      Profession = {
        Family = Classification.Power
        Stage = Classification.First
      }
      Name = "Test"
      BaseStats = {
        Power = 1
        Magic = 1
        Sense = 1
        Charm = 1
      }
      StarterAbilities = HashSet.empty
    }

    let createChange = Pomo.Lib.Operations.GameState.createEntity id kit
    GameState.apply servicesState createChange

    let entityId =
      createChange.additions |> HashMap.toArrayV |> Array.head |> fstV

    // Issue teleport command
    let tp: TeleportChange = {
      EntityId = entityId
      ToScenarioId = secondId
      ToPosition = { X = 123f; Y = 456f }
    }

    let command = Teleport tp

    let teleportChange =
      Pomo.Lib.Rules.Resolution.evaluate servicesState command |> AVal.force

    GameState.apply servicesState teleportChange

    // Assert entity now in second scenario with updated position
    let secondScenarioEntities =
      servicesState.scenarios[secondId].entities |> AMap.force

    Assert.True(secondScenarioEntities.ContainsKey entityId)
    let comps = secondScenarioEntities[entityId]
    Assert.Equal(123f, comps.Position.X)
    Assert.Equal(456f, comps.Position.Y)

    // Ensure entity removed from original scenario
    let firstScenarioEntities =
      servicesState.scenarios[servicesState.activeScenarioId.Value].entities
      |> AMap.force

    Assert.False(firstScenarioEntities.ContainsKey entityId)

  [<Fact>]
  let ``teleport ignored when target scenario missing``() =
    let servicesState = GameState.create()

    // Create entity
    let kit = {
      Profession = {
        Family = Classification.Power
        Stage = Classification.First
      }
      Name = "Test"
      BaseStats = {
        Power = 1
        Magic = 1
        Sense = 1
        Charm = 1
      }
      StarterAbilities = HashSet.empty
    }

    let createChange = Pomo.Lib.Operations.GameState.createEntity id kit
    GameState.apply servicesState createChange

    let entityId =
      createChange.additions |> HashMap.toArrayV |> Array.head |> fstV

    let missingScenarioId = %System.Guid.NewGuid()

    let tp: TeleportChange = {
      EntityId = entityId
      ToScenarioId = missingScenarioId
      ToPosition = { X = 10f; Y = 20f }
    }

    let command = Teleport tp

    let teleportChange =
      Pomo.Lib.Rules.Resolution.evaluate servicesState command |> AVal.force

    GameState.apply servicesState teleportChange

    // Entity should remain in original scenario
    let firstScenarioEntities =
      servicesState.scenarios[servicesState.activeScenarioId.Value].entities
      |> AMap.force

    Assert.True(firstScenarioEntities.ContainsKey entityId)
    let comps = firstScenarioEntities[entityId]
    // Position unchanged (still default 0,0)
    Assert.Equal(0f, comps.Position.X)
    Assert.Equal(0f, comps.Position.Y)
