namespace Pomo.Lib.Tests

open Xunit
open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.State
open Pomo.Lib.Content
open Pomo.Lib.Rules
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Tests.TestHelpers
open Pomo.Lib.Scenario
open Pomo.Lib.Content
open Pomo.Lib.Gameplay
open Pomo.Lib.Operations

module private Phase3Helpers =
  open Pomo.Lib.Domain.Scenario
  open Pomo.Lib.Domain.Services

  let create(rng: unit -> float) =
    let initialScenarioId = %Guid.NewGuid()

    let initialScenarioState =
      ScenarioState.create {
        Scenario.empty with
            Id = initialScenarioId
            Name = "Test Scenario"
            BoundsWidth = 2000f
            BoundsHeight = 2000f
            EngagementMode = AlwaysOn
            BattleEnabled = true
      }

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
        rng = rng
      }
      (initialScenarioId, scenarios)

  let baseStats = {
    Power = 12
    Magic = 4
    Sense = 50
    Charm = 10
  }

  let makeEntity
    (id: Guid<EntityId>)
    (baseStats: BaseAttributes)
    hp
    mp
    (abilities: int<AbilityId> list)
    (effects: (int<EffectId> * int * TimeSpan) list)
    (factions: Classification.Faction seq)
    =
    let cooldowns: HashMap<int<AbilityId>, TimeSpan> =
      abilities
      |> List.fold
        (fun acc a -> acc |> HashMap.add a TimeSpan.Zero)
        HashMap.empty

    let activeEffects: HashMap<int<EffectId>, ActiveEffect> =
      effects
      |> List.fold
        (fun acc (effectId, stacks, remaining) ->
          let eff = {
            EffectId = effectId
            SourceId = id
            RemainingTicks = remaining
            NextTickIn = TimeSpan.Zero
            Stacks = stacks
            Definition = EffectStore.definitions[effectId]
          }

          acc |> HashMap.add effectId eff)
        HashMap.empty

    {
      Factions = HashSet.ofSeq factions
      Identity = {
        Family = Classification.Family.Power
        Stage = Classification.Stage.First
      }
      BaseStats = baseStats
      Resources = {
        HP = hp
        MP = mp
        Status = Status.Alive
      }
      Movement = {
        Speed = 0f
        Destination = ValueNone
        Path = []
      }
      Position = { X = 0f; Y = 0f }
      Effects = activeEffects
      Abilities = HashSet.ofList abilities
      AbilityCooldowns = cooldowns
      EquippedItems = HashMap.empty
      Inventory = HashMap.empty
      PartyId = ValueNone
    }

  let addEntity
    (state: State.GameState)
    (id: Guid<EntityId>)
    (all: EntityComponents)
    =
    let scenario = getActiveScenario state
    transact(fun _ -> scenario.entities.Add(id, all) |> ignore)

  let derivedOf (state: State.GameState) (id: Guid<EntityId>) =
    getDerivedStat state id

open Phase3Helpers

type ``Phase3 - Stun``() =
  [<Fact>]
  member _.``T2 Stun prevents all actions``() =
    let state = create(fun () -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 1<AbilityId>
    let stunEffectId = 100<EffectId>

    let attacker =
      makeEntity attackerId baseStats 100 30 [ melee ] [
        (stunEffectId, 1, TimeSpan.FromSeconds(10.0))
      ] [ Classification.Player ]

    let target =
      makeEntity targetId baseStats 100 30 [] [] [ Classification.Enemy ]

    addEntity state attackerId attacker
    addEntity state targetId target

    let initialTargetHp = (getEntity state targetId).Resources.HP
    let initialAttackerMp = (getEntity state attackerId).Resources.MP

    let action = {
      actor = attackerId
      target = EntityTargets [| targetId |]
      abilityId = melee
    }

    let delta = CommandHandler.evaluate state (UseAbility action)
    let change = delta |> AVal.force
    GameState.apply state change

    let finalTargetHp = (getEntity state targetId).Resources.HP
    Assert.Equal<int>(initialTargetHp, finalTargetHp)

    let finalAttackerMp = (getEntity state attackerId).Resources.MP
    Assert.Equal<int>(initialAttackerMp, finalAttackerMp)

    let cooldown =
      (getEntity state attackerId).AbilityCooldowns |> HashMap.find melee

    Assert.Equal(TimeSpan.Zero, cooldown)

type ``Phase3 - Silence``() =
  [<Fact>]
  member _.``T3 Silence blocks MP abilities``() =
    let state = create(fun () -> 0.0)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 10<AbilityId>
    let silence = 9<AbilityId>
    let meleeWithCost = 11<AbilityId>

    let attacker =
      makeEntity attackerId baseStats 100 30 [ silence ] [] [
        Classification.Player
      ]

    let target =
      makeEntity targetId baseStats 100 30 [ melee; meleeWithCost ] [] [
        Classification.Enemy
      ]

    addEntity state attackerId attacker
    addEntity state targetId target

    let initialTargetHp = (getEntity state targetId).Resources.HP
    let initialAttackerMp = (getEntity state attackerId).Resources.MP

    let spellAction = {
      actor = attackerId
      target = EntityTargets [| targetId |]
      abilityId = silence
    }

    let spellDelta = CommandHandler.evaluate state (UseAbility spellAction)
    let spellChange = spellDelta |> AVal.force
    GameState.apply state spellChange

    let targetHpAfterSpell = (getEntity state targetId).Resources.HP
    Assert.Equal<int>(initialTargetHp, targetHpAfterSpell)

    let attackerMpAfterSpell = (getEntity state attackerId).Resources.MP

    let silenceSpellCost =
      match AbilityStore.definitions[silence] with
      | Abilities.Active def -> def.Cost.Value.Amount
      | _ -> failwith "Expected active ability"

    let expectedMpAfterSpell = initialAttackerMp - silenceSpellCost

    Assert.Equal(expectedMpAfterSpell, attackerMpAfterSpell)

    let targetEffects = (getEntity state targetId).Effects

    let hasSilenceEffect =
      targetEffects |> HashMap.exists(fun _ e -> e.EffectId = 101<EffectId>)

    Assert.True(hasSilenceEffect, "Target should have silence effect")

    let advance = GameState.tick state (TimeSpan.FromSeconds(0.1)) |> AVal.force
    GameState.apply state advance

    let initialHpAttackerBeforeTargetMeele =
      (getEntity state attackerId).Resources.HP

    let targetMpBeforeAttack = (getEntity state targetId).Resources.MP

    let targetEffectsBeforeMpAttack = (getEntity state targetId).Effects

    let hasSilenceBeforeMpAttack =
      targetEffectsBeforeMpAttack
      |> HashMap.exists(fun _ e -> e.EffectId = 101<EffectId>)

    Assert.True(
      hasSilenceBeforeMpAttack,
      "Target should still have silence effect before MP attack"
    )

    let meleeAction = {
      actor = targetId
      target = EntityTargets [| attackerId |]
      abilityId = meleeWithCost
    }

    let meleeDelta = CommandHandler.evaluate state (UseAbility meleeAction)
    let meleeChange = meleeDelta |> AVal.force
    GameState.apply state meleeChange

    let attackerHpAfterTargetMelee = (getEntity state attackerId).Resources.HP
    let targetMpAfterAttack = (getEntity state targetId).Resources.MP

    Assert.Equal<int>(
      attackerHpAfterTargetMelee,
      initialHpAttackerBeforeTargetMeele
    )

    Assert.Equal(targetMpBeforeAttack, targetMpAfterAttack)

    let meleeWithNoCostAction = {
      actor = targetId
      target = EntityTargets [| attackerId |]
      abilityId = melee
    }

    let meleeWithCostDelta =
      CommandHandler.evaluate state (UseAbility meleeWithNoCostAction)

    let meleeWithCostChange = meleeWithCostDelta |> AVal.force
    GameState.apply state meleeWithCostChange

    let attackerHpAfterTargetMeleeWithNoCost =
      (getEntity state attackerId).Resources.HP

    Assert.Equal<int>(70, attackerHpAfterTargetMeleeWithNoCost)

type ``Phase3 - Taunt``() =
  [<Fact>]
  member _.``T4 Taunt redirection forces target to taunter``() =
    let state = create(fun () -> 0.1)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let intendedTargetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let taunterId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 11<AbilityId>
    let tauntEffectId = 103<EffectId>

    let tauntBaseStats = {
      Power = 10
      Magic = 1
      Sense = 1
      Charm = 0
    }

    let attacker =
      makeEntity attackerId tauntBaseStats 100 30 [ melee ] [] [
        Classification.Player
      ]

    let intendedTarget =
      makeEntity intendedTargetId tauntBaseStats 100 30 [] [] [
        Classification.Enemy
      ]

    let taunter =
      makeEntity taunterId tauntBaseStats 100 30 [] [] [ Classification.Ally ]

    addEntity state attackerId attacker
    addEntity state intendedTargetId intendedTarget
    addEntity state taunterId taunter

    let tauntEffect = {
      EffectId = tauntEffectId
      SourceId = taunterId
      RemainingTicks = TimeSpan.FromSeconds(10.0)
      NextTickIn = TimeSpan.Zero
      Stacks = 1
      Definition = EffectStore.definitions[tauntEffectId]
    }

    let attacker = getEntity state attackerId

    setEntity state attackerId {
      attacker with
          Effects = HashMap.single tauntEffectId tauntEffect
    }

    let initialIntendedTargetHp =
      (getEntity state intendedTargetId).Resources.HP

    let initialTaunterHp = (getEntity state taunterId).Resources.HP

    let action = {
      actor = attackerId
      target = EntityTargets [| intendedTargetId |]
      abilityId = melee
    }

    let delta = CommandHandler.evaluate state (UseAbility action)
    let change = delta |> AVal.force
    GameState.apply state change

    let finalIntendedTargetHp = (getEntity state intendedTargetId).Resources.HP
    Assert.Equal<int>(initialIntendedTargetHp, finalIntendedTargetHp)

    let finalTaunterHp = (getEntity state taunterId).Resources.HP

    Assert.True(
      finalTaunterHp < initialTaunterHp,
      "Taunter should have taken damage"
    )

type ``Phase3 - Effect Stacking``() =
  [<Fact>]
  member _.``T5 NoStack ignores second application``() =
    let state = create(fun () -> 0.5)
    let casterId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let spellId = 3<AbilityId>
    let noStackEffectId = 104<EffectId>

    let caster =
      makeEntity casterId baseStats 100 100 [ spellId ] [] [
        Classification.Player
      ]

    let target =
      makeEntity targetId baseStats 100 100 [] [] [ Classification.Enemy ]

    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        CommandHandler.evaluate
          state
          (UseAbility {
            actor = casterId
            target = EntityTargets [| targetId |]
            abilityId = spellId
          })

      let change = delta |> AVal.force
      GameState.apply state change

    applySpell()
    let effectsAfterFirst = (getEntity state targetId).Effects
    let firstEffectOpt = effectsAfterFirst |> HashMap.tryFind noStackEffectId

    Assert.True(
      firstEffectOpt.IsSome,
      "Effect should be applied after first application"
    )

    let firstEffect = firstEffectOpt.Value
    let advance = GameState.tick state (TimeSpan.FromSeconds(1.0)) |> AVal.force
    GameState.apply state advance
    applySpell()
    let effectsAfterSecond = (getEntity state targetId).Effects
    Assert.Equal(1, (effectsAfterFirst |> HashMap.toSeq |> Seq.length))
    Assert.Equal(1, (effectsAfterSecond |> HashMap.toSeq |> Seq.length))
    let secondEffectOpt = effectsAfterSecond |> HashMap.tryFind noStackEffectId

    Assert.True(
      secondEffectOpt.IsSome,
      "Effect should still exist after second application"
    )

    let secondEffect = secondEffectOpt.Value
    let remaining = firstEffect.RemainingTicks - TimeSpan.FromSeconds(1.0)
    let secondRemaining = secondEffect.RemainingTicks
    Assert.Equal(remaining, secondRemaining)
    Assert.Equal(1, secondEffect.Stacks)

  [<Fact>]
  member _.``T6 RefreshDuration resets timer, stack count unchanged``() =
    let state = create(fun () -> 0.5)
    let casterId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let spellId = 4<AbilityId>
    let refreshEffectId = 1<EffectId>

    let caster =
      makeEntity casterId baseStats 100 100 [ spellId ] [] [
        Classification.Player
      ]

    let target =
      makeEntity targetId baseStats 100 100 [] [] [ Classification.Enemy ]

    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        CommandHandler.evaluate
          state
          (UseAbility {
            actor = casterId
            target = EntityTargets [| targetId |]
            abilityId = spellId
          })

      let change = delta |> AVal.force
      GameState.apply state change

    applySpell()
    let effectsAfterFirst = (getEntity state targetId).Effects

    let firstEffect =
      effectsAfterFirst
      |> HashMap.tryFindV refreshEffectId
      |> ValueOption.toOption

    match firstEffect with
    | None -> ()
    | Some firstEffect ->

    let advance = GameState.tick state (TimeSpan.FromSeconds(1.0)) |> AVal.force
    GameState.apply state advance

    applySpell()
    let effectsAfterSecond = (getEntity state targetId).Effects

    let secondEffect =
      effectsAfterSecond
      |> HashMap.tryFindV refreshEffectId
      |> ValueOption.toOption

    match secondEffect with
    | None -> ()
    | Some secondEffect ->
      let effectDef = EffectStore.definitions[refreshEffectId]

      let expectedDuration =
        match effectDef.Duration with
        | Timed d -> d
        | _ -> failwith "Expected timed duration"

      Assert.Equal(1, (effectsAfterFirst |> HashMap.toSeq |> Seq.length))
      Assert.Equal(1, (effectsAfterSecond |> HashMap.toSeq |> Seq.length))
      Assert.Equal(1, secondEffect.Stacks)

      Assert.True(
        secondEffect.RemainingTicks > firstEffect.RemainingTicks
                                      - TimeSpan.FromSeconds(1.0),
        "Duration should be refreshed"
      )

      Assert.Equal(expectedDuration, secondEffect.RemainingTicks)

  [<Fact>]
  member _.``T8 DoT ticking applies periodic damage and expires``() =
    let state = create(fun () -> 0.5)
    let casterId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let spellId = 6<AbilityId>
    let _ = 105<EffectId>

    let caster =
      makeEntity casterId baseStats 100 100 [ spellId ] [] [
        Classification.Player
      ]

    let target =
      makeEntity targetId baseStats 100 100 [] [] [ Classification.Enemy ]

    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        CommandHandler.evaluate
          state
          (UseAbility {
            actor = casterId
            target = EntityTargets [| targetId |]
            abilityId = spellId
          })

      let change = delta |> AVal.force
      GameState.apply state change

    let hp() = (getEntity state targetId).Resources.HP
    let _ = hp()

    applySpell()
    let hpAfterApply = hp()

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick1 = hp()

    Assert.True(
      hpAfterTick1 < hpAfterApply,
      "DoT should reduce HP on first tick"
    )

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick2 = hp()
    Assert.True(hpAfterTick2 < hpAfterTick1, "DoT should continue reducing HP")

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick3 = hp()
    Assert.True(hpAfterTick3 < hpAfterTick2, "DoT should continue reducing HP")

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick4 = hp()
    Assert.True(hpAfterTick4 < hpAfterTick3, "DoT should continue reducing HP")

    let effects = (getEntity state targetId).Effects
    Assert.True(HashMap.isEmpty effects)

  [<Fact>]
  member _.``T9 HoT ticking applies periodic healing and expires``() =
    let state = create(fun () -> 0.5)
    let casterId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let spellId = 7<AbilityId>
    let _ = 106<EffectId>

    let caster =
      makeEntity casterId baseStats 100 100 [ spellId ] [] [
        Classification.Player
      ]

    let target =
      makeEntity targetId baseStats 50 100 [] [] [ Classification.Enemy ]

    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        CommandHandler.evaluate
          state
          (UseAbility {
            actor = casterId
            target = EntityTargets [| targetId |]
            abilityId = spellId
          })

      let change = delta |> AVal.force
      GameState.apply state change

    let hp() = (getEntity state targetId).Resources.HP
    let initialHp = hp()

    applySpell()
    let hpAfterApply = hp()
    Assert.Equal<int>(initialHp, hpAfterApply)

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick1 = hp()
    Assert.Equal(initialHp + 5, hpAfterTick1)

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick2 = hp()
    Assert.Equal(initialHp + 10, hpAfterTick2)

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick3 = hp()
    Assert.Equal(initialHp + 15, hpAfterTick3)

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.0)) |> AVal.force
    GameState.apply state advance
    let hpAfterTick4 = hp()
    Assert.Equal(initialHp + 20, hpAfterTick4)

    let effects = (getEntity state targetId).Effects
    Assert.True(HashMap.isEmpty effects)

type ``Phase3 - Cooldown Management``() =

  [<Fact>]

  member _.``T13 Cooldown is applied for ability not initially in the cooldown map``
    ()
    =
    let state = create(fun () -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 11<AbilityId>



    let attacker =
      let baseEntity =
        makeEntity attackerId baseStats 100 100 [ melee ] [] [
          Classification.Player
        ]

      {
        baseEntity with
            AbilityCooldowns = HashMap.empty
      }



    let target =
      makeEntity targetId baseStats 100 100 [] [] [ Classification.Enemy ]

    addEntity state attackerId attacker
    addEntity state targetId target



    let meleeDelta =
      CommandHandler.evaluate
        state
        (UseAbility {
          actor = attackerId
          target = EntityTargets [| targetId |]
          abilityId = melee
        })



    let meleeChange = meleeDelta |> AVal.force
    GameState.apply state meleeChange

    let readyAfterMelee =
      Operations.GameState.getReadyAbilities attackerId state

    Assert.False(readyAfterMelee |> HashSet.contains melee)

  [<Fact>]
  member _.``T12 Cooldown-ready abilities set includes ability after cooldown elapses``
    ()
    =
    let state = create(fun () -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 11<AbilityId>
    let spell = 2<AbilityId>

    let attacker =
      makeEntity attackerId baseStats 100 100 [ melee; spell ] [] [
        Classification.Player
      ]

    let target =
      makeEntity targetId baseStats 100 100 [] [] [ Classification.Enemy ]

    addEntity state attackerId attacker
    addEntity state targetId target

    let readyAbilitiesInitial =
      Operations.GameState.getReadyAbilities attackerId state

    Assert.True(readyAbilitiesInitial |> HashSet.contains melee)
    Assert.True(readyAbilitiesInitial |> HashSet.contains spell)

    let meleeDelta =
      CommandHandler.evaluate
        state
        (UseAbility {
          actor = attackerId
          target = EntityTargets [| targetId |]
          abilityId = melee
        })

    let meleeChange = meleeDelta |> AVal.force
    GameState.apply state meleeChange

    let readyAfterMelee =
      Operations.GameState.getReadyAbilities attackerId state

    Assert.False(readyAfterMelee |> HashSet.contains melee)
    Assert.True(readyAfterMelee |> HashSet.contains spell)

    let advance = GameState.tick state (TimeSpan.FromSeconds(2.1)) |> AVal.force
    GameState.apply state advance

    let readyAfterCooldown =
      Operations.GameState.getReadyAbilities attackerId state

    Assert.True(readyAfterCooldown |> HashSet.contains melee)
    Assert.True(readyAfterCooldown |> HashSet.contains spell)

    let spellDelta =
      CommandHandler.evaluate
        state
        (UseAbility {
          actor = attackerId
          target = EntityTargets [| targetId |]
          abilityId = spell
        })

    let spellChange = spellDelta |> AVal.force
    GameState.apply state spellChange

    let readyAfterSpell =
      Operations.GameState.getReadyAbilities attackerId state

    Assert.True(readyAfterSpell |> HashSet.contains melee)
    Assert.False(readyAfterSpell |> HashSet.contains spell)

    let advance = GameState.tick state (TimeSpan.FromSeconds(5.1)) |> AVal.force
    GameState.apply state advance

    let readyAfterBothCooldowns =
      Operations.GameState.getReadyAbilities attackerId state

    Assert.True(readyAfterBothCooldowns |> HashSet.contains melee)
    Assert.True(readyAfterBothCooldowns |> HashSet.contains spell)
