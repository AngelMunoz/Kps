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
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Battle

module private EngagementTestHelpers =
  open Pomo.Lib.Domain.Services

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
      Destination = ValueNone
      Path = []
    }
    Effects = HashMap.empty
    Abilities = HashSet.empty
    AbilityCooldowns = HashMap.empty
    EquippedItems = HashMap.empty
    Inventory = HashMap.empty
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
      StartTick = TimeSpan.Zero
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
      Cooldown = TimeSpan.Zero
      Cost = ValueNone
      Targeting = TargetType.SingleEnemy
      Range = 1000f
      FormulaId = ValueNone
      Effects = [||]
      Requirements = [||]
      CastingTime = ValueNone
      PreActivationVisualEffectIds = Array.empty
      ProjectileIds = Array.empty
      AoeIds = Array.empty
      ImpactIds = Array.empty
    }

    let supportAbility = {
      Id = 2<AbilityId>
      Name = "Heal"
      Intent = AbilityIntent.Support
      Cooldown = TimeSpan.Zero
      Cost = ValueNone
      Targeting = TargetType.SingleAlly
      Range = 1000f
      FormulaId = ValueNone
      Effects = [||]
      Requirements = [||]
      CastingTime = ValueNone
      PreActivationVisualEffectIds = Array.empty
      ProjectileIds = Array.empty
      AoeIds = Array.empty
      ImpactIds = Array.empty
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
