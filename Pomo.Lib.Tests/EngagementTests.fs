namespace Pomo.Lib.Tests

open Xunit
open System
open FSharp.UMX
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Gameplay
open Pomo.Lib.Tests.TestHelpers
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Rules
open Pomo.Lib.Content
open Pomo.Lib.Scenario
open Pomo.Lib.Battle

module private EngagementTestHelpers =

  let create(engagementMode: EngagementMode) =
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
            scenario = {
              sc.scenario with
                  EngagementMode = engagementMode
            }
      })

    GameState.create'
      {
        effectStore =
          { new Services.IEffectStore with
              member _.tryFind effectId =
                EffectStore.definitions
                |> Map.tryFind effectId
                |> ValueOption.ofOption

              member _.find effectId =
                EffectStore.definitions |> Map.find effectId
          }
        abilityStore =
          { new Services.IAbilityStore with
              member _.tryFind abilityId =
                AbilityStore.definitions
                |> Map.tryFind abilityId
                |> ValueOption.ofOption

              member _.find abilityId =
                AbilityStore.definitions |> Map.find abilityId
          }
        formulaStore =
          { new Services.IFormulaStore with
              member _.tryFind formulaId =
                FormulaStore.definitions
                |> Map.tryFind formulaId
                |> ValueOption.ofOption

              member _.find formulaId =
                FormulaStore.definitions |> Map.find formulaId
          }
        rng = fun () -> 0.5
      }
      (initialScenarioId, cmap [ (initialScenarioId, initialScenarioState) ])

  let makeEntity(faction: Classification.Faction seq) : EntityComponents = {
    Factions = HashSet.ofSeq faction
    Identity = {
      Family = Classification.Family.Power
      Stage = Classification.Stage.First
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
    Effects = HashMap.empty
    Abilities = HashSet.empty
    AbilityCooldowns = HashMap.empty
    Equipment = HashMap.empty
    PartyId = ValueNone
  }

type ``Engagement Targeting Rules``() =
  [<Fact>]
  member _.``Structured mode blocks offensive ability if not in same battle instance``
    ()
    =
    let state = EngagementTestHelpers.create EngagementMode.Structured
    let actorId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>

    let actor = EngagementTestHelpers.makeEntity [ Classification.Player ]
    let target = EngagementTestHelpers.makeEntity [ Classification.Enemy ]

    addEntity state actorId actor
    addEntity state targetId target

    let scenario = getActiveScenario state

    let offensiveAbility =
      match state.services.abilityStore.find 1<AbilityId> with
      | Abilities.Active def -> def
      | _ -> failwith "Expected active ability"

    let canUse =
      Engagement.canUseAbility
        scenario
        state.parties
        actorId
        target
        targetId
        offensiveAbility
      |> AVal.force

    Assert.False(canUse)

  [<Fact>]
  member _.``Structured mode allows offensive ability if in same battle instance``
    ()
    =
    let state = EngagementTestHelpers.create EngagementMode.Structured
    let actorId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>

    let actor = EngagementTestHelpers.makeEntity [ Classification.Player ]
    let target = EngagementTestHelpers.makeEntity [ Classification.Enemy ]

    addEntity state actorId actor
    addEntity state targetId target
    let newId = %Guid.NewGuid()

    let battleInstance: BattleInstance = {
      Id = %Guid.NewGuid()
      Participants = HashSet.ofList [ actorId; targetId ]
      StartTick = 0L<Tick>
    }

    let scenario = getActiveScenario state

    transact(fun _ ->
      scenario.battleInstances.Add(newId, battleInstance) |> ignore)

    let offensiveAbility =
      match state.services.abilityStore.find 1<AbilityId> with
      | Abilities.Active def -> def
      | _ -> failwith "Expected active ability"

    let canUse =
      Engagement.canUseAbility
        scenario
        state.parties
        actorId
        target
        targetId
        offensiveAbility
      |> AVal.force

    Assert.True(canUse)

  [<Fact>]
  member _.``Peaceful zones block Offensive abilities while Support remains allowed``
    ()
    =
    let state = EngagementTestHelpers.create EngagementMode.Peaceful
    let actorId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>

    let actor = EngagementTestHelpers.makeEntity [ Classification.Player ]
    let target = EngagementTestHelpers.makeEntity [ Classification.Enemy ]

    addEntity state actorId actor
    addEntity state targetId target

    let scenario = getActiveScenario state

    let offensiveAbility = {
      Id = 1<AbilityId>
      Name = "Attack"
      Intent = AbilityIntent.Offensive
      Cooldown = 0L<Tick>
      Cost = ValueNone
      Targeting = TargetType.SingleEnemy
      FormulaId = ValueNone
      Effects = [||]
      Requirements = [||]
    }

    let supportAbility = {
      Id = 2<AbilityId>
      Name = "Heal"
      Intent = AbilityIntent.Support
      Cooldown = 0L<Tick>
      Cost = ValueNone
      Targeting = TargetType.SingleAlly
      FormulaId = ValueNone
      Effects = [||]
      Requirements = [||]
    }

    let canUseOffensive =
      Engagement.canUseAbility
        scenario
        state.parties
        actorId
        target
        targetId
        offensiveAbility
      |> AVal.force

    let canUseSupport =
      Engagement.canUseAbility
        scenario
        state.parties
        actorId
        target
        targetId
        supportAbility
      |> AVal.force

    Assert.False(canUseOffensive)
    Assert.True(canUseSupport)

  [<Fact>]
  member _.``Party friendly-fire blocked across all modes``() =
    let state = EngagementTestHelpers.create EngagementMode.AlwaysOn
    let partyId = %Guid.NewGuid()
    let actorId = Guid.NewGuid() |> UMX.tag<EntityId>
    let allyId = Guid.NewGuid() |> UMX.tag<EntityId>

    let actor = {
      EngagementTestHelpers.makeEntity [ Classification.Player ] with
          PartyId = ValueSome partyId
    }

    let ally = {
      EngagementTestHelpers.makeEntity [ Classification.Player ] with
          PartyId = ValueSome partyId
    }

    addEntity state actorId actor
    addEntity state allyId ally

    let party = {
      Id = partyId
      Members = HashSet.ofList [ actorId; allyId ]
      Name = "Test Party"
    }

    transact(fun _ -> state.parties.Add(partyId, party) |> ignore)

    let scenario = getActiveScenario state

    let offensiveAbility =
      match state.services.abilityStore.find 1<AbilityId> with
      | Abilities.Active def -> def
      | _ -> failwith "Expected active ability"

    let canAttackAlly =
      Engagement.canUseAbility
        scenario
        state.parties
        actorId
        ally
        allyId
        offensiveAbility
      |> AVal.force

    Assert.False(canAttackAlly)
