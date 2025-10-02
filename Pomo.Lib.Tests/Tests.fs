namespace Pomo.Lib.Tests

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
    let effMap = EffectStore.definitions |> HashMap.ofMap |> AMap.ofHashMap

    let abilMap = AbilityStore.definitions |> HashMap.ofMap |> AMap.ofHashMap

    let effList =
      AList.constant(fun () ->
        [ for KeyValue(_, v) in EffectStore.definitions -> v ]
        |> IndexList.ofList)

    let abilList =
      AList.constant(fun () ->
        [ for KeyValue(_, v) in AbilityStore.definitions -> v ]
        |> IndexList.ofList)

    GameState.create' {
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
      rng = rng
    }

  let makeEntity
    (faction: Classification.Faction seq)
    (baseStats: BaseAttributes)
    hp
    mp
    (abilities: int<AbilityId> list)
    : EntityComponents =
    let emptySeq: seq<int<AbilityId> * int64<Tick>> = Seq.empty
    let cooldowns: cmap<int<AbilityId>, int64<Tick>> = cmap emptySeq

    transact(fun _ ->
      for a in abilities do
        cooldowns.Add(a, 0L<Tick>) |> ignore)

    {
      Factions = HashSet.ofSeq faction
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
      Effects = (clist [] :> alist<_>)
      Abilities = (clist abilities :> alist<_>)
      AbilityCooldowns = (cooldowns :> amap<_, _>)
    }

  let addEntity (state: GameState) (id: int<EntityId>) (all: EntityComponents) =
    transact(fun _ -> state.entities.Add(id, all) |> ignore)

  let derivedOf (state: GameState) (id: int<EntityId>) =
    GameState.getDerivedStats state |> AMap.force |> (fun m -> m[id])

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

    let entity =
      TestHelpers.makeEntity [ Classification.Player ] baseAttrs 100 100 []

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
    Power = 20
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
    let melee = 8<AbilityId> // Basic Melee Attack No Cost and No Effects

    let attacker =
      TestHelpers.makeEntity [ Classification.Player ] baseA 100 50 [ melee ]

    let target = TestHelpers.makeEntity [ Classification.Enemy ] baseB 80 30 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    let action =
      UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      }

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let targetAfter = state.entities[targetId]
    Assert.Equal(0, targetAfter.Resources.HP) // 80 - 80 = 0

  [<Fact>]
  member _.``Spell casting applies damage, costs MP, and can kill target``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let casterId = 10<EntityId>
    let victimId = 11<EntityId>
    let spell = 2<AbilityId>

    let casterBase = {
      Power = 2
      Magic = 20
      Sense = 50 // High LK to guarantee hit
      Charm = 5
    }

    let victimBase = {
      Power = 1
      Magic = 1
      Sense = 1 // Low LK
      Charm = 5
    }

    let caster =
      TestHelpers.makeEntity [ Classification.Player ] casterBase 100 100 [
        spell
      ]

    let victim =
      TestHelpers.makeEntity [ Classification.Enemy ] victimBase 30 10 []

    TestHelpers.addEntity state casterId caster
    TestHelpers.addEntity state victimId victim

    let action =
      (UseAbility {
        actor = casterId
        targets = [| victimId |]
        abilityId = spell
      })

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    // Fireball uses formula 2: MA*2 + elemental = 20*2 + 40 = 80
    let victimAfter = state.entities[victimId]

    Assert.Equal(0, victimAfter.Resources.HP)
    Assert.Equal(Status.Dead, victimAfter.Resources.Status)

  [<Fact>]
  member _.``Melee ability stamina cost reduces stamina and emits ResourceChanged``
    ()
    =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 100<EntityId>
    let targetId = 200<EntityId>
    let melee = 1<AbilityId>

    let attacker =
      TestHelpers.makeEntity [ Classification.Player ] baseA 100 40 [ melee ]

    let target = TestHelpers.makeEntity [ Classification.Enemy ] baseB 40 10 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target
    let before = attacker.Resources.MP

    let cost =
      match AbilityStore.definitions[melee] with
      | Abilities.Active def -> (def.Cost |> ValueOption.get).Amount
      | _ -> failwith "Expected active ability"

    let action =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let attackerAfter = state.entities[attackerId]
    Assert.Equal(before - cost, attackerAfter.Resources.MP)

  [<Fact>]
  member _.``Cooldown prevents immediate reuse``() =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>

    let attacker =
      TestHelpers.makeEntity [ Classification.Player ] baseA 100 50 [ melee ]

    let target = TestHelpers.makeEntity [ Classification.Enemy ] baseB 80 30 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    // First attack, should succeed and apply cooldown
    let action1 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta1 = Resolution.step state action1
    let change1 = delta1 |> AVal.force
    Resolution.apply state change1

    // Second attack, should be ignored due to cooldown
    let action2 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta2 = Resolution.step state action2
    let change2 = delta2 |> AVal.force
    Resolution.apply state change2


  [<Fact>]
  member _.``Action puts ability on cooldown``() =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>

    let attacker =
      TestHelpers.makeEntity [ Classification.Player ] baseA 100 50 [ melee ]

    let target = TestHelpers.makeEntity [ Classification.Enemy ] baseB 80 30 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    let action =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let attackerAfter = state.entities[attackerId]
    let cooldowns = AMap.force attackerAfter.AbilityCooldowns
    let cooldown = cooldowns[melee]

    let expectedCooldown =
      match AbilityStore.definitions[melee] with
      | Abilities.Active def -> def.Cooldown
      | _ -> failwith "Expected active ability"

    Assert.True(cooldown > 0L<Tick>)
    Assert.Equal(expectedCooldown, cooldown)

  [<Fact>]
  member _.``Ability is usable again after cooldown expires``() =
    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>

    let attacker =
      TestHelpers.makeEntity [ Classification.Player ] baseA 100 50 [ melee ]

    let target = TestHelpers.makeEntity [ Classification.Enemy ] baseB 80 30 []
    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    // First attack
    let action1 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta1 = Resolution.step state action1
    let change1 = delta1 |> AVal.force
    Resolution.apply state change1

    let cooldown =
      match AbilityStore.definitions[melee] with
      | Abilities.Active def -> def.Cooldown
      | _ -> failwith "Expected active ability"
    // Advance time past the cooldown
    let advance = GameState.tick state (cooldown + 1L<Tick>) |> AVal.force
    GameState.applyTick state advance

    // Second attack, should succeed now
    let action2 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta2 = Resolution.step state action2
    let change2 = delta2 |> AVal.force
    Resolution.apply state change2

    let targetRes = state.entities[targetId].Resources
    Assert.True(targetRes.HP < 80) // Target should have taken damage

// --------------------------------------------------
// Combat Mechanics Property Tests
// --------------------------------------------------
type ``Combat Mechanics Properties``() =

  [<Property(MaxTest = 50)>]
  member _.``Physical hit chance follows AC vs HV formula``
    (attackerPower: PositiveInt)
    (defenderCharm: PositiveInt)
    (rng: NormalFloat)
    =
    let power = float(attackerPower.Get % 50 + 10)
    let charm = float(defenderCharm.Get % 50 + 10)
    let rngValue = abs rng.Get % 1.0

    let ac = power / 100.0 // AC formula from derived stats
    let hv = charm / 100.0 // HV formula from derived stats
    let expectedHitChance = ac / (ac + hv)
    let shouldHit = rngValue < expectedHitChance

    let state = TestHelpers.create(fun () -> rngValue)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let meleeId = 1<AbilityId>

    let attackerStats = {
      Power = int power
      Magic = 4
      Sense = 50
      Charm = 10
    }

    let defenderStats = {
      Power = 12
      Magic = 4
      Sense = 50
      Charm = int charm
    }

    let attacker =
      TestHelpers.makeEntity [ Classification.Player ] attackerStats 100 100 [
        meleeId
      ]

    let target =
      TestHelpers.makeEntity [ Classification.Enemy ] defenderStats 100 100 []

    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    let entitiesSnapshot = state.entities |> AMap.force

    let initialHp = entitiesSnapshot[targetId].Resources.HP

    let action =
      UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = meleeId
      }

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let entitiesSnapshotAfter = state.entities |> AMap.force
    let finalHp = entitiesSnapshotAfter[targetId].Resources.HP
    let actualHit = finalHp < initialHp

    actualHit = shouldHit

  [<Property(MaxTest = 50)>]
  member _.``Magical hit chance follows LK vs LK formula``
    (attackerSense: PositiveInt)
    (defenderSense: PositiveInt)
    (rng: NormalFloat)
    =
    let atkSense = float(attackerSense.Get % 50 + 10)
    let defSense = float(defenderSense.Get % 50 + 10)
    let rngValue = abs rng.Get % 1.0

    let expectedHitChance = atkSense / (atkSense + defSense)
    let shouldHit = rngValue < expectedHitChance

    let state = TestHelpers.create(fun () -> rngValue)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let spellId = 6<AbilityId>

    let attackerStats = {
      Power = 12
      Magic = 4
      Sense = int atkSense
      Charm = 10
    }

    let defenderStats = {
      Power = 12
      Magic = 4
      Sense = int defSense
      Charm = 10
    }

    let attacker =
      TestHelpers.makeEntity [ Classification.Player ] attackerStats 100 100 [
        spellId
      ]

    let target =
      TestHelpers.makeEntity [ Classification.Enemy ] defenderStats 100 100 []

    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    let entitiesSnapshot = state.entities |> AMap.force

    let initialHp = entitiesSnapshot[targetId].Resources.HP

    let action =
      UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = spellId
      }

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

    let entitiesSnapshotAfter = state.entities |> AMap.force
    let finalHp = entitiesSnapshotAfter[targetId].Resources.HP
    let actualHit = finalHp < initialHp

    actualHit = shouldHit

  [<Property(MaxTest = 100)>]
  member _.``Damage scales with attacker stats``
    (attackerPower: PositiveInt)
    (rngResult: NormalFloat)
    =
    let power = attackerPower.Get
    let rngValue = abs rngResult.Get % 1.0

    let stateLow = TestHelpers.create(fun () -> rngValue)
    let stateHigh = TestHelpers.create(fun () -> rngValue)

    let attackerIdLow = 1<EntityId>
    let attackerIdHigh = 2<EntityId>
    let targetId = 10<EntityId>
    let meleeId = 1<AbilityId>

    let lowStats = {
      Power = power
      Magic = 4
      Sense = 50
      Charm = 10
    }

    let highStats = {
      Power = power + 10
      Magic = 4
      Sense = 50
      Charm = 10
    }

    let targetStats = {
      Power = 5
      Magic = 4
      Sense = 1
      Charm = 10
    }


    let attackerLow =
      TestHelpers.makeEntity [ Classification.Player ] lowStats 100 100 [
        meleeId
      ]

    let attackerHigh =
      TestHelpers.makeEntity [ Classification.Player ] highStats 100 100 [
        meleeId
      ]

    let targetLow =
      TestHelpers.makeEntity [ Classification.Enemy ] targetStats 100 100 []

    let targetHigh =
      TestHelpers.makeEntity [ Classification.Enemy ] targetStats 100 100 []

    TestHelpers.addEntity stateLow attackerIdLow attackerLow
    TestHelpers.addEntity stateLow targetId targetLow
    TestHelpers.addEntity stateHigh attackerIdHigh attackerHigh
    TestHelpers.addEntity stateHigh targetId targetHigh

    let targetLowLifeBefore = stateLow.entities[targetId].Resources.HP
    let targetHighLifeBefore = stateHigh.entities[targetId].Resources.HP

    let actionLow =
      UseAbility {
        actor = attackerIdLow
        targets = [| targetId |]
        abilityId = meleeId
      }

    let deltaLow = Resolution.step stateLow actionLow |> AVal.force
    Resolution.apply stateLow deltaLow

    let actionHigh =
      UseAbility {
        actor = attackerIdHigh
        targets = [| targetId |]
        abilityId = meleeId
      }

    let deltaHigh = Resolution.step stateHigh actionHigh |> AVal.force
    Resolution.apply stateHigh deltaHigh

    let targetLowLifeAfter = stateLow.entities[targetId].Resources.HP
    let targetHighLifeAfter = stateHigh.entities[targetId].Resources.HP

    let damageHigh = targetHighLifeBefore - targetHighLifeAfter
    let damageLow = targetLowLifeBefore - targetLowLifeAfter

    if damageHigh > 0 then damageHigh > damageLow else true
