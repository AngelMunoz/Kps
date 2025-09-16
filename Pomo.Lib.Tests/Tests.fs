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
    let! agi = Gen.choose(1, 100)
    let! intl = Gen.choose(1, 100)
    let! vit = Gen.choose(1, 100)
    let! wil = Gen.choose(1, 100)
    let! luck = Gen.choose(1, 100)

    return {
      Strength = str
      Agility = agi
      Intellect = intl
      Vitality = vit
      Willpower = wil
      Luck = luck
    }
  }

  type CustomArbs() =
    static member BaseAttributes() = baseAttributesGen |> Arb.fromGen


// --------------------------------------------------
// Helpers
// --------------------------------------------------
module private TestHelpers =
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
        Family = Classification.Family.Strength
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
    let state = GameState.create()
    let id = 1<EntityId>
    let entity = TestHelpers.makeEntity id baseAttrs 100 100 100 []
    TestHelpers.addEntity state id entity
    let derived = TestHelpers.derivedOf state id
    let expectedAttack = baseAttrs.Strength * 2
    let expectedSpell = baseAttrs.Intellect * 2
    let expectedMaxHp = baseAttrs.Vitality * 10
    let expectedMaxMp = baseAttrs.Willpower * 10
    let expectedCrit = float baseAttrs.Luck / 100.0

    expectedAttack = derived.AttackPower
    && expectedSpell = derived.SpellPower
    && expectedMaxHp = derived.MaxHP
    && expectedMaxMp = derived.MaxMP
    && expectedCrit = derived.CritChance

// --------------------------------------------------
// Phase 2 Action Resolution Tests
// --------------------------------------------------
type ``Action Resolution``() =
  let baseA = {
    Strength = 10
    Agility = 5
    Intellect = 5
    Vitality = 10
    Willpower = 5
    Luck = 5
  }

  let baseB = {
    Strength = 4
    Agility = 3
    Intellect = 3
    Vitality = 8
    Willpower = 3
    Luck = 2
  }

  [<Fact>]
  member _.``Melee attack applies expected damage and emits DamageApplied``() =
    let state = GameState.create'(fun () -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    Resolution.apply
      state
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let targetAfter = state.entities.[targetId]
    Assert.Equal(63, targetAfter.Resources.HP)

    let damageEventExists =
      state.gameEvents
      |> AList.choose(fun ev ->
        match ev with
        | GameEvent.DamageApplied e when e.target = targetId -> Some e
        | _ -> None)
      |> AList.tryFirst
      |> AVal.force
      |> Option.get

    Assert.Equal(17, damageEventExists.amount)

  [<Fact>]
  member _.``Spell casting applies damage, costs MP, and can kill target``() =
    let state = GameState.create'(fun () -> 0.5)
    let casterId = 10<EntityId>
    let victimId = 11<EntityId>
    let spell = 2<AbilityId>

    let casterBase = {
      Strength = 2
      Agility = 2
      Intellect = 20
      Vitality = 5
      Willpower = 10
      Luck = 5
    }

    let victimBase = {
      Strength = 1
      Agility = 1
      Intellect = 1
      Vitality = 5
      Willpower = 1
      Luck = 1
    }

    let caster = TestHelpers.makeEntity casterId casterBase 100 100 50 [ spell ]
    let victim = TestHelpers.makeEntity victimId victimBase 30 10 20 []
    TestHelpers.addEntity state casterId caster
    TestHelpers.addEntity state victimId victim

    Resolution.apply
      state
      (CastSpell {
        actor = casterId
        target = victimId
        abilityId = spell
      })

    let damageAppliedCorrectly =
      state.gameEvents
      |> AList.choose(fun ev ->
        match ev with
        | GameEvent.DamageApplied e when e.target = victimId -> Some e
        | _ -> None)
      |> AList.tryFirst
      |> AVal.force
      |> Option.get

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
    let state = GameState.create()
    let attackerId = 100<EntityId>
    let targetId = 200<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 40 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 40 10 20 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target
    let before = attacker.Resources.Stamina
    let cost = (AbilityStore.definitions.[melee].Cost |> Option.get).Amount

    Resolution.apply
      state
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

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
    let state = GameState.create()
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    // First attack, should succeed and apply cooldown
    Resolution.apply
      state
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let eventsAfterFirst = state.gameEvents |> AList.force
    Assert.Equal(2, eventsAfterFirst.Count) // DamageApplied + ResourceChanged

    // Second attack, should be ignored due to cooldown
    Resolution.apply
      state
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

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
    let state = GameState.create()
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    Resolution.apply
      state
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let attackerAfter = state.entities.[attackerId]
    let cooldowns = AMap.force attackerAfter.AbilityCooldowns
    let cooldown = cooldowns[melee]
    let expectedCooldown = AbilityStore.definitions[melee].Cooldown
    Assert.True(cooldown > 0L<Tick>)
    Assert.Equal(expectedCooldown, cooldown)

  [<Fact>]
  member _.``Ability is usable again after cooldown expires``() =
    let state = GameState.create()
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let attacker = TestHelpers.makeEntity attackerId baseA 100 50 100 [ melee ]
    let target = TestHelpers.makeEntity targetId baseB 80 30 50 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    // First attack
    Resolution.apply
      state
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let cooldown = AbilityStore.definitions[melee].Cooldown
    // Advance time past the cooldown
    GameState.tick state (cooldown + 1L<Tick>)

    // Second attack, should succeed now
    Resolution.apply
      state
      (MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      })

    let damageEvents =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied e -> Some e
        | _ -> None)
      |> AList.force

    Assert.Equal(2, damageEvents.Count)
