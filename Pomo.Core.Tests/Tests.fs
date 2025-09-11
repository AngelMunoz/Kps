namespace Pomo.Core.Tests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Primitives
open Pomo.Core.Domain.Attributes
open Pomo.Core.Domain.Components
open Pomo.Core.Gameplay
open Pomo.Core.Rules
open Pomo.Core.Content

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
    (id: EntityId)
    (baseStats: BaseAttributes)
    hp
    mp
    stamina
    (abilities: Abilities.AbilityId list)
    : Components.All =
    let emptySeq: seq<Abilities.AbilityId * int64<ticks>> = Seq.empty
    let cooldowns: cmap<Abilities.AbilityId, int64<ticks>> = cmap emptySeq

    transact(fun _ ->
      for a in abilities do
        cooldowns.Add(a, 0L<ticks>) |> ignore)

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

  let addEntity (state: GameState) (id: EntityId) (all: Components.All) =
    transact(fun _ -> state.entities.Add(id, all) |> ignore)

  let derivedOf (state: GameState) (id: EntityId) =
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
    let id = EntityId 1
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
    let state = GameState.create()
    let attackerId = EntityId 1
    let targetId = EntityId 2
    let melee = Abilities.AbilityId 1
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

    let derivedA = TestHelpers.derivedOf state attackerId
    let derivedB = TestHelpers.derivedOf state targetId
    let expectedDamage = max 0 (derivedA.AttackPower - derivedB.Armor)
    let targetAfter = state.entities.[targetId]
    Assert.Equal(80 - expectedDamage, targetAfter.Resources.HP)
    let events = state.gameEvents |> AList.force

    Assert.Contains(
      events,
      Predicate(fun ev ->
        match ev with
        | DamageApplied e when e.target = targetId && e.amount = expectedDamage ->
          true
        | _ -> false)
    )

  [<Fact>]
  member _.``Spell casting applies damage, costs MP, and can kill target``() =
    let state = GameState.create()
    let casterId = EntityId 10
    let victimId = EntityId 11
    let spell = Abilities.AbilityId 2

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

    let events = state.gameEvents |> AList.force
    let derivedCaster = TestHelpers.derivedOf state casterId
    let expectedDamage = derivedCaster.SpellPower

    Assert.Contains(
      events,
      Predicate(fun ev ->
        match ev with
        | DamageApplied e when e.target = victimId && e.amount = expectedDamage ->
          true
        | _ -> false)
    )

    Assert.Contains(
      events,
      Predicate(fun ev ->
        match ev with
        | ResourceChanged rc when
          rc.target = casterId && rc.resource.Contains("MP")
          ->
          true
        | _ -> false)
    )

    let victimAfter = state.entities.[victimId]

    if expectedDamage >= 30 then
      Assert.Equal(0, victimAfter.Resources.HP)
      Assert.Equal(Status.Dead, victimAfter.Resources.Status)

      Assert.Contains(
        events,
        Predicate(fun ev ->
          match ev with
          | EntityDied d when d.entityId = victimId -> true
          | _ -> false)
      )

  [<Fact>]
  member _.``Melee ability stamina cost reduces stamina and emits ResourceChanged``
    ()
    =
    let state = GameState.create()
    let attackerId = EntityId 100
    let targetId = EntityId 200
    let melee = Abilities.AbilityId 1
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
    let events = state.gameEvents |> AList.force

    Assert.Contains(
      events,
      Predicate(fun ev ->
        match ev with
        | ResourceChanged rc when
          rc.target = attackerId && rc.resource.Contains("Stamina")
          ->
          true
        | _ -> false)
    )

  [<Fact(Skip = "Cooldown update currently maps existing entries only; enable after implementation change")>]
  member _.``Cooldown prevents immediate reuse (pending)``() = Assert.True(true)
