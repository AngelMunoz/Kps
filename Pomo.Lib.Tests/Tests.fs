namespace Pomo.Lib.Tests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Components
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Rules
open Pomo.Lib.Content

// --------------------------------------------------
// Generators
// --------------------------------------------------
module private Generators =
  open FsCheck

  let baseAttributesGen = gen {
    let! str = Gen.choose(1, 100)
    let! mag = Gen.choose(1, 100)
    let! sen = Gen.choose(1, 100)
    let! charm = Gen.choose(1, 100)

    return {
      Power = str
      Magic = mag
      Sense = sen
      Charm = charm
    }
  }

  type CustomArbs() =
    static member BaseAttributes() = baseAttributesGen |> Arb.fromGen


// --------------------------------------------------
// Helpers
// --------------------------------------------------
module private TestHelpers =


  let create(rng: unit -> float) =
    let effMap =
      Pomo.Lib.Content.EffectStore.definitions
      |> HashMap.ofMap
      |> AMap.ofHashMap

    let abilMap =
      Pomo.Lib.Content.AbilityStore.definitions
      |> HashMap.ofMap
      |> AMap.ofHashMap

    let effList =
      AList.constant(fun () ->
        [ for KeyValue(_, v) in Pomo.Lib.Content.EffectStore.definitions -> v ]
        |> IndexList.ofList)

    let abilList =
      AList.constant(fun () ->
        [
          for KeyValue(_, v) in Pomo.Lib.Content.AbilityStore.definitions -> v
        ]
        |> IndexList.ofList)

    GameState.create' {
      effectStore =
        { new Services.IEffectStore with
            member _.tryFind effectId =
              Pomo.Lib.Content.EffectStore.definitions
              |> Map.tryFind effectId
              |> ValueOption.ofOption

            member _.find effectId =
              Pomo.Lib.Content.EffectStore.definitions |> Map.find effectId

            member _.asAMap = effMap

            member _.asAList = effList

            member _.asList =
              [
                for KeyValue(_, v) in Pomo.Lib.Content.EffectStore.definitions ->
                  v
              ]
              |> FSharp.Data.Adaptive.IndexList.ofList
        }
      abilityStore =
        { new Services.IAbilityStore with
            member _.tryFind abilityId =
              Pomo.Lib.Content.AbilityStore.definitions
              |> Map.tryFind abilityId
              |> ValueOption.ofOption

            member _.find abilityId =
              Pomo.Lib.Content.AbilityStore.definitions |> Map.find abilityId

            member _.asAMap = abilMap

            member _.asAList = abilList

            member _.asList =
              [
                for KeyValue(_, v) in Pomo.Lib.Content.AbilityStore.definitions ->
                  v
              ]
              |> FSharp.Data.Adaptive.IndexList.ofList
        }
      rng = rng
    }


  let makeEntity
    (id: int<EntityId>)
    (baseStats: BaseAttributes)
    hp
    mp
    stamina
    (abilities: int<AbilityId> list)
    : Components.All =
    let emptySeq: seq<int<AbilityId> * int64<Tick>> = Seq.empty
    let cooldowns: cmap<int<AbilityId>, int64<Tick>> = cmap emptySeq

    transact(fun _ ->
      for a in abilities do
        cooldowns.Add(a, 0L<Tick>) |> ignore)

    {
      Identity = {
        Family = Classification.Family.Power
        Stage = Classification.Stage.First
      }
      BaseStats = baseStats
      Resources = {
        HP = hp
        MP = mp
        Stamina = stamina
        Status = Status.Alive
      }
      Effects = (clist [] :> alist<_>)
      Abilities = (clist abilities :> alist<_>)
      AbilityCooldowns = (cooldowns :> amap<_, _>)
    }

  let addEntity (state: GameState) (id: int<EntityId>) (all: Components.All) =
    transact(fun _ -> state.entities.Add(id, all) |> ignore)

  let derivedOf (state: GameState) (id: int<EntityId>) =
    GameState.getDerivedStats state |> AMap.force |> (fun m -> m.[id])

// --------------------------------------------------
// Property Tests (Derived Stats)
// --------------------------------------------------
type ``Derived Stats``() =
  [<Property(MaxTest = 50)>]
  member _.``Derived stats formula matches implementation``
    (baseAttrs: BaseAttributes)
    =
    let state = TestHelpers.create(fun _ -> 0.5)
    let id = 1<EntityId>
    let entity = TestHelpers.makeEntity id baseAttrs 100 100 100 []
    TestHelpers.addEntity state id entity
    let derived = TestHelpers.derivedOf state id
    let expectedAttack = baseAttrs.Power * 2
    let expectedMagicAttack = baseAttrs.Magic * 2
    let expectedHealthPoints = baseAttrs.Charm * 10
    let expectedMagicPotential = baseAttrs.Magic * 5
    let expectedAccuracy = float baseAttrs.Power / 100.0

    expectedAttack = derived.AP
    && expectedMagicAttack = derived.MA
    && expectedHealthPoints = derived.HP
    && expectedMagicPotential = derived.MP
    && expectedAccuracy = derived.AC

// --------------------------------------------------
// Phase 2 Action Resolution Tests
// --------------------------------------------------
type ``Action Resolution``() =
  let baseA = {
    Power = 10
    Magic = 5
    Sense = 10
    Charm = 10
  }

  let baseB = {
    Power = 4
    Magic = 3
    Sense = 8
    Charm = 8
  }

  [<Fact>]
  member _.``Melee attack applies expected damage and emits DamageApplied``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    let action =
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let targetAfter = state.entities.[targetId]
    Assert.Equal(64, targetAfter.Resources.HP)

    let damageEventExists =
      state.gameEvents
      |> AList.choose(fun ev ->
        match ev with
        | GameEvent.DamageApplied e when e.target = targetId -> Some e
        | _ -> None)
      |> AList.tryFirst
      |> AVal.force
      |> Option.toValueOption
      |> ValueOption.get

    Assert.Equal(16, damageEventExists.amount)

  [<Fact>]
  member _.``Spell casting applies damage, costs MP, and can kill target``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let casterId = 10<EntityId>
    let victimId = 11<EntityId>
    let spell = 2<AbilityId>

    let casterBase = {
      Power = 2
      Magic = 20
      Sense = 5
      Charm = 5
    }

    let victimBase = {
      Power = 1
      Magic = 1
      Sense = 5
      Charm = 5
    }

    let caster = TestHelpers.makeEntity casterId casterBase 100 100 50 [ spell ]
    let victim = TestHelpers.makeEntity victimId victimBase 30 10 20 []
    TestHelpers.addEntity state casterId caster
    TestHelpers.addEntity state victimId victim

    let action =
      (CastSpell {
        actor = casterId
        target = victimId
        abilityId = spell
      })

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let damageAppliedCorrectly =
      state.gameEvents
      |> AList.choose(fun ev ->
        match ev with
        | GameEvent.DamageApplied e when e.target = victimId -> Some e
        | _ -> None)
      |> AList.tryFirst
      |> AVal.force
      |> Option.toValueOption
      |> ValueOption.get

    Assert.Equal(40, damageAppliedCorrectly.amount)

    let mpChanged =
      state.gameEvents
      |> AList.exists(fun ev ->
        match ev with
        | GameEvent.ResourceChanged rc when
          rc.target = casterId && rc.resource.Contains("MP")
          ->
          true
        | _ -> false)
      |> AVal.force

    Assert.True(mpChanged)

    let victimAfter = state.entities.[victimId]

    Assert.Equal(0, victimAfter.Resources.HP)
    Assert.Equal(Status.Dead, victimAfter.Resources.Status)

    let victimDied =
      state.gameEvents
      |> AList.exists(fun ev ->
        match ev with
        | GameEvent.EntityDied d when d.entityId = victimId -> true
        | _ -> false)
      |> AVal.force

    Assert.True(victimDied)

  [<Fact>]
  member _.``Melee ability stamina cost reduces stamina and emits ResourceChanged``
    ()
    =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 100<EntityId>
    let targetId = 200<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 40 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 40 10 20 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target
    let before = attacker.Resources.Stamina
    let cost = (AbilityStore.definitions.[melee].Cost |> ValueOption.get).Amount

    let action =
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let attackerAfter = state.entities.[attackerId]
    Assert.Equal(before - cost, attackerAfter.Resources.Stamina)

    let staminaChanged =
      state.gameEvents
      |> AList.exists(fun ev ->
        match ev with
        | GameEvent.ResourceChanged rc when
          rc.target = attackerId && rc.resource.Contains("Stamina")
          ->
          true
        | _ -> false)

    Assert.True(AVal.force staminaChanged)

  [<Fact>]
  member _.``Cooldown prevents immediate reuse``() =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    // First attack, should succeed and apply cooldown
    let action1 =
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let delta1 = Resolution.step state action1
    let change1 = delta1 |> AVal.force
    Resolution.apply state change1

    let eventsAfterFirst = state.gameEvents |> AList.force
    Assert.Equal(2, eventsAfterFirst.Count) // DamageApplied + ResourceChanged

    // Second attack, should be ignored due to cooldown
    let action2 =
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let delta2 = Resolution.step state action2
    let change2 = delta2 |> AVal.force
    Resolution.apply state change2

    let eventsAfterSecond = state.gameEvents |> AList.force
    Assert.Equal(2, eventsAfterSecond.Count) // No new events

    // No new DamageApplied event should be added
    let damageEvents =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied e -> Some e
        | _ -> None)

    Assert.Single(AList.force damageEvents) |> ignore

  [<Fact>]
  member _.``Action puts ability on cooldown``() =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    let action =
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let attackerAfter = state.entities.[attackerId]
    let cooldowns = AMap.force attackerAfter.AbilityCooldowns
    let cooldown = cooldowns[melee]
    let expectedCooldown = AbilityStore.definitions[melee].Cooldown
    Assert.True(cooldown > 0L<Tick>)
    Assert.Equal(expectedCooldown, cooldown)

  [<Fact>]
  member _.``Ability is usable again after cooldown expires``() =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    // First attack
    let action1 =
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let delta1 = Resolution.step state action1
    let change1 = delta1 |> AVal.force
    Resolution.apply state change1

    let cooldown = AbilityStore.definitions[melee].Cooldown
    // Advance time past the cooldown
    let advance = GameState.tick state (cooldown + 1L<Tick>) |> AVal.force
    GameState.applyTick state advance

    // Second attack, should succeed now
    let action2 =
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let delta2 = Resolution.step state action2
    let change2 = delta2 |> AVal.force
    Resolution.apply state change2

    let damageEvents =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied e -> Some e
        | _ -> None)
      |> AList.force

    Assert.Equal(2, damageEvents.Count)
