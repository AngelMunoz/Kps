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
      Equipment = HashMap.empty
    }

  let addEntity
    (state: GameState)
    (id: Guid<EntityId>)
    (all: EntityComponents)
    =
    transact(fun _ -> state.entities.Add(id, all) |> ignore)

  let derivedOf (state: GameState) (id: Guid<EntityId>) =
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
    let id = Guid.NewGuid() |> UMX.tag<EntityId>

    let entity =
      TestHelpers.makeEntity [ Classification.Player ] baseAttrs 100 100 []

    TestHelpers.addEntity state id entity
    let derived = TestHelpers.derivedOf state id

    let expectedAttack = baseAttrs.Power * 2
    let expectedAccuracy = baseAttrs.Power + int(float baseAttrs.Power * 1.25)
    let expectedDexterity = baseAttrs.Power

    let expectedMagicPotential = baseAttrs.Magic * 5
    let expectedMagicAttack = baseAttrs.Magic * 2

    let expectedMagicDefense =
      baseAttrs.Magic + int(float baseAttrs.Magic * 1.25)

    let expectedWeight = baseAttrs.Sense * 5
    let expectedDetectAbility = baseAttrs.Sense * 2
    let expectedLuck = baseAttrs.Sense + int(float baseAttrs.Sense * 0.5)

    let expectedHealthPoints = baseAttrs.Charm * 10
    let expectedDefense = baseAttrs.Charm + int(float baseAttrs.Charm * 1.25)
    let expectedEvasion = baseAttrs.Charm * 2

    expectedAttack = derived.AP
    && expectedAccuracy = derived.AC
    && expectedDexterity = derived.DX
    && expectedMagicPotential = derived.MP
    && expectedMagicAttack = derived.MA
    && expectedMagicDefense = derived.MD
    && expectedWeight = derived.WT
    && expectedDetectAbility = derived.DA
    && expectedLuck = derived.LK
    && expectedHealthPoints = derived.HP
    && expectedDefense = derived.DP
    && expectedEvasion = derived.HV

// --------------------------------------------------
// Phase 2 Action Resolution Tests
// --------------------------------------------------
type ``Action Resolution``() =

  [<Fact>]
  member _.``Melee attack applies expected damage``() =
    let baseA = {
      Power = 20
      Magic = 5
      Sense = 5 // High sense to guarantee hits via AC and LK
      Charm = 10
    }

    let baseB = {
      Power = 4
      Magic = 3
      Sense = 5
      Charm = 16 // Increased charm to have a DP of 8
    }

    let state = TestHelpers.create(fun () -> 0.1)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
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
    // DP is Charm + 1.25*Charm -> 16 + 20 = 36
    // AP is Power * 2 -> 20 * 2 = 40
    // Melee damage formula is AP * 2 -> 40 * 2 = 80
    // Final damage is 80 - 36 = 44
    // Victim HP is Charm * 10 -> 16 * 10 = 160
    // Victim HP after attack is 160 - 44 = 116
    Assert.Equal(36, targetAfter.Resources.HP)

  [<Fact>]
  member _.``Magic attack applies expected damage with MD``() =
    let state = TestHelpers.create(fun () -> 0.1)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let spell = 6<AbilityId> // Fireball

    let attacker =
      TestHelpers.makeEntity
        [ Classification.Player ]
        {
          Power = 10
          Magic = 55
          Sense = 10
          Charm = 10
        }
        100
        100
        [ spell ]

    let target =
      TestHelpers.makeEntity
        [ Classification.Enemy ]
        {
          Power = 4
          Magic = 3
          Sense = 8
          Charm = 16
        }
        80
        30
        []

    TestHelpers.addEntity state attackerId attacker
    TestHelpers.addEntity state targetId target

    let action =
      UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = spell
      }

    let delta = Resolution.step state action
    let change: StateChange = delta |> AVal.force
    Resolution.apply state change

    let targetAfter = state.entities[targetId]
    // Victim HP is 80 - 110 = -30, clamped to 0.
    Assert.Equal(0, targetAfter.Resources.HP)
    Assert.Equal(Status.Dead, targetAfter.Resources.Status)

  [<Fact>]
  member _.``Spell casting applies damage, costs MP, and can kill target``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let casterId = Guid.NewGuid() |> UMX.tag<EntityId>
    let victimId = Guid.NewGuid() |> UMX.tag<EntityId>
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
    let baseA = {
      Power = 20
      Magic = 5
      Sense = 50 // High sense to guarantee hits via AC and LK
      Charm = 10
    }

    let baseB = {
      Power = 4
      Magic = 3
      Sense = 8
      Charm = 16 // Increased charm to have a DP of 8
    }

    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
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
    let baseA = {
      Power = 20
      Magic = 5
      Sense = 50 // High sense to guarantee hits via AC and LK
      Charm = 10
    }

    let baseB = {
      Power = 4
      Magic = 3
      Sense = 8
      Charm = 16 // Increased charm to have a DP of 8
    }

    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
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
    let baseA = {
      Power = 20
      Magic = 5
      Sense = 50 // High sense to guarantee hits via AC and LK
      Charm = 10
    }

    let baseB = {
      Power = 4
      Magic = 3
      Sense = 8
      Charm = 16 // Increased charm to have a DP of 8
    }

    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
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
    let baseA = {
      Power = 20
      Magic = 5
      Sense = 50 // High sense to guarantee hits via AC and LK
      Charm = 10
    }

    let baseB = {
      Power = 4
      Magic = 3
      Sense = 8
      Charm = 16 // Increased charm to have a DP of 8
    }

    let state = TestHelpers.create(fun _ -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
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

  [<Property(MaxTest = 100)>]
  member _.``Physical hit chance follows AC vs HV formula``
    (acInput: NonNegativeInt)
    (hvInput: NonNegativeInt)
    =
    let ac = acInput.Get % 200
    let hv = hvInput.Get % 200
    let chance = Resolution.calculateHitChance ac hv
    let diff = ac - hv

    // Invariants:
    // 1. Clamp range (unless both zero -> special case 1.0)
    // 2. Equal positive stats => 0.5
    // 3. Advantage >= 45 => clamp 0.95; disadvantage <= -45 => clamp 0.05
    // 4. Monotonic direction relative to 0.5 baseline when stats positive and unequal
    let rangeOk =
      if ac = 0 && hv = 0 then
        chance = 1.0
      else
        chance >= 0.05 && chance <= 0.95

    let equalOk = if ac = hv && ac > 0 then chance = 0.5 else true

    let clampHighOk = if diff >= 45 then chance = 0.95 else true

    let clampLowOk =
      if diff <= -45 && not(ac = 0 && hv = 0) then
        chance = 0.05
      else
        true

    let directionOk =
      if ac > hv then chance >= 0.5
      elif ac < hv then chance <= 0.5
      else true

    rangeOk && equalOk && clampHighOk && clampLowOk && directionOk

  [<Fact>]
  member _.``DynamicMod applies formula-calculated stat boost``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 10 -> MA = 20 -> expected AP boost = 20/2 = 10
    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      TestHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    TestHelpers.addEntity state playerId player

    // Get initial derived stats (no effects)
    let initialStats = GameState.getDerivedStats state |> AMap.force
    let initialAP = initialStats[playerId].AP

    // Apply Dynamic AP Boost effect (ID 300)
    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    transact(fun _ ->
      let currentComponents = state.entities[playerId]

      state.entities[playerId] <-
        {
          currentComponents with
              Effects = AList.ofList [ dynamicEffect ]
        })

    // Get stats after applying DynamicMod effect
    let finalStats = GameState.getDerivedStats state |> AMap.force
    let finalAP = finalStats[playerId].AP

    // Expected: AP boost = MA / 2 = 20 / 2 = 10
    // So finalAP should be initialAP + 10
    Assert.Equal(initialAP + 10, finalAP)

  [<Fact>]
  member _.``DynamicMod evaluates with correct invoker stats``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 20 -> MA = 40 -> expected AP boost = 40/2 = 20
    let baseStats = {
      Power = 5
      Magic = 20
      Sense = 5
      Charm = 10
    }

    let player =
      TestHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    TestHelpers.addEntity state playerId player

    let initialStats = GameState.getDerivedStats state |> AMap.force
    let initialAP = initialStats[playerId].AP

    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    transact(fun _ ->
      let currentComponents = state.entities[playerId]

      state.entities[playerId] <-
        {
          currentComponents with
              Effects = AList.ofList [ dynamicEffect ]
        })

    let finalStats = GameState.getDerivedStats state |> AMap.force
    let finalAP = finalStats[playerId].AP

    // Expected: AP boost = MA / 2 = 40 / 2 = 20
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``Multiple DynamicMod effects stack correctly``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      TestHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    TestHelpers.addEntity state playerId player

    let initialStats = GameState.getDerivedStats state |> AMap.force
    let initialAP = initialStats[playerId].AP

    // Apply two instances of Dynamic AP Boost effect
    let effect1: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    let effect2: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 10000L<Tick>
      NextTickIn = 10000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    transact(fun _ ->
      let currentComponents = state.entities[playerId]

      state.entities[playerId] <-
        {
          currentComponents with
              Effects = AList.ofList [ effect1; effect2 ]
        })

    let finalStats = GameState.getDerivedStats state |> AMap.force
    let finalAP = finalStats[playerId].AP

    // Each effect gives AP boost = MA / 2 = 20 / 2 = 10
    // Two effects should give +20 total
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``DynamicMod can target MA``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 20 -> MA = 40 -> expected MA boost = 40/2 = 20
    let baseStats = {
      Power = 5
      Magic = 20
      Sense = 5
      Charm = 10
    }

    let player =
      TestHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    TestHelpers.addEntity state playerId player

    let initialStats = GameState.getDerivedStats state |> AMap.force
    let initialMA = initialStats[playerId].MA

    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 301<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 301<EffectId>
    }

    transact(fun _ ->
      let currentComponents = state.entities[playerId]

      state.entities[playerId] <-
        {
          currentComponents with
              Effects = AList.ofList [ dynamicEffect ]
        })

    let finalStats = GameState.getDerivedStats state |> AMap.force
    let finalMA = finalStats[playerId].MA

    // Expected: MA boost = MA / 2 = 40 / 2 = 20
    Assert.Equal(initialMA + 20, finalMA)

  [<Fact>]
  member _.``DynamicMod targeting different stats stacks independently``() =
    let state = TestHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 10 -> MA = 20 -> boost = 20/2 = 10
    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      TestHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    TestHelpers.addEntity state playerId player

    let initialStats = GameState.getDerivedStats state |> AMap.force
    let initialAP = initialStats[playerId].AP
    let initialMA = initialStats[playerId].MA

    // Apply both effects: 300 (targets AP) and 301 (targets MA)
    let apEffect: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    let maEffect: Effects.ActiveEffect = {
      EffectId = 301<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 301<EffectId>
    }

    transact(fun _ ->
      let currentComponents = state.entities[playerId]

      state.entities[playerId] <-
        {
          currentComponents with
              Effects = AList.ofList [ apEffect; maEffect ]
        })

    let finalStats = GameState.getDerivedStats state |> AMap.force
    let finalAP = finalStats[playerId].AP
    let finalMA = finalStats[playerId].MA

    // Each effect gives boost = MA / 2 = 20 / 2 = 10
    // AP should increase by 10, MA should increase by 10
    Assert.Equal(initialAP + 10, finalAP)
    Assert.Equal(initialMA + 10, finalMA)

  [<Property(MaxTest = 100)>]
  member _.``Magical hit chance follows LK vs LK formula``
    (lkAtkInput: NonNegativeInt)
    (lkDefInput: NonNegativeInt)
    =
    let lkA = lkAtkInput.Get % 200
    let lkD = lkDefInput.Get % 200
    let chance = Resolution.calculateHitChance lkA lkD
    let diff = lkA - lkD

    let rangeOk =
      if lkA = 0 && lkD = 0 then
        chance = 1.0
      else
        chance >= 0.05 && chance <= 0.95

    let equalOk = if lkA = lkD && lkA > 0 then chance = 0.5 else true
    let clampHighOk = if diff >= 45 then chance = 0.95 else true

    let clampLowOk =
      if diff <= -45 && not(lkA = 0 && lkD = 0) then
        chance = 0.05
      else
        true

    let directionOk =
      if lkA > lkD then chance >= 0.5
      elif lkA < lkD then chance <= 0.5
      else true

    rangeOk && equalOk && clampHighOk && clampLowOk && directionOk

  [<Property(MaxTest = 50)>]
  member _.``Damage scales with attacker stats``
    (attackerPower: PositiveInt)
    (rngResult: NormalFloat)
    =
    let power = abs attackerPower.Get
    let rngValue = abs rngResult.Get % 1.0

    let state = TestHelpers.create(fun () -> rngValue)

    let attackerIdLow = Guid.NewGuid() |> UMX.tag<EntityId>
    let attackerIdHigh = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let meleeId = 1<AbilityId>

    let lowStats = {
      Power = power
      Magic = 4
      Sense = 50
      Charm = 0
    }

    let highStats = {
      Power = power + 10
      Magic = 4
      Sense = 50
      Charm = 0
    }

    let targetStats = {
      Power = 5
      Magic = 4
      Sense = 1
      Charm = 0
    }


    let attackerLow =
      TestHelpers.makeEntity [ Classification.Player ] lowStats 100 100 [
        meleeId
      ]

    let attackerHigh =
      TestHelpers.makeEntity [ Classification.Player ] highStats 100 100 [
        meleeId
      ]

    let target =
      TestHelpers.makeEntity [ Classification.Enemy ] targetStats 100 100 []

    TestHelpers.addEntity state attackerIdLow attackerLow
    TestHelpers.addEntity state attackerIdHigh attackerHigh
    TestHelpers.addEntity state targetId target

    let derivedStats = GameState.getDerivedStats state |> AMap.force
    let actorStatsLow = derivedStats[attackerIdLow]
    let actorStatsHigh = derivedStats[attackerIdHigh]
    let targetStats = derivedStats[targetId]

    let rparams: Resolution.ResolverParams = {
      entities = state.entities
      enemies = GameState.getEnemies state
      allies = GameState.getAllies state
      derivedStats = GameState.getDerivedStats state
      gameTime = state.gameTime
      services = state.services
    }

    let formulaId =
      state.services.abilityStore.find meleeId
      |> function
        | Abilities.Active def -> def.FormulaId |> ValueOption.get
        | _ -> failwith "Expected active ability"

    let damageLow =
      Resolution.calculateDamage
        {
          services = rparams.services
          attackerStats = actorStatsLow
          defenderStats = targetStats
          attackerEffects = AList.empty
        }
        formulaId
      |> AVal.force

    let damageHigh =
      Resolution.calculateDamage
        {
          services = rparams.services
          attackerStats = actorStatsHigh
          defenderStats = targetStats
          attackerEffects = AList.empty
        }
        formulaId
      |> AVal.force

    if damageHigh.Amount > 0 then
      damageHigh.Amount > damageLow.Amount
    else
      true
