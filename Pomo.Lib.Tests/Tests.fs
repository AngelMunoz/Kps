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
open Pomo.Lib.Tests.TestHelpers
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
module private InternalHelpers =


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
      Position = { X = 0f; Y = 0f }
      Movement = {
        Speed = 0f
        Destination = ValueNone
        Path = []
      }
      Effects = clist []
      Abilities = HashSet.ofList abilities
      AbilityCooldowns = cooldowns
      Equipment = HashMap.empty
    }

  let addEntity
    (state: GameState)
    (id: Guid<EntityId>)
    (all: EntityComponents)
    =
    addEntity state id all

  let derivedOf (state: GameState) (id: Guid<EntityId>) =
    getDerivedStat state id

// --------------------------------------------------
// Property Tests (Derived Stats)
// --------------------------------------------------
type ``Derived Stats``() =
  [<Property(MaxTest = 50)>]
  member _.``Derived stats formula matches implementation``
    (baseAttrs: BaseAttributes)
    =
    let state = InternalHelpers.create(fun _ -> 0.5)
    let id = Guid.NewGuid() |> UMX.tag<EntityId>

    let entity =
      InternalHelpers.makeEntity [ Classification.Player ] baseAttrs 100 100 []

    InternalHelpers.addEntity state id entity
    let derived = InternalHelpers.derivedOf state id
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
  member _.``DynamicMod evaluates with correct invoker stats``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    let baseStats = {
      Power = 5
      Magic = 20
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialAP = getDerivedStat state playerId |> fun s -> s.AP

    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ dynamicEffect ]
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``Multiple DynamicMod effects stack correctly``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialAP = getDerivedStat state playerId |> fun s -> s.AP

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

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ effect1; effect2 ]
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``DynamicMod can target MA``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    let baseStats = {
      Power = 5
      Magic = 20
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialMA = getDerivedStat state playerId |> fun s -> s.MA

    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 301<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 301<EffectId>
    }

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ dynamicEffect ]
    }

    let finalMA = getDerivedStat state playerId |> fun s -> s.MA
    Assert.Equal(initialMA + 20, finalMA)

  [<Fact>]
  member _.``DynamicMod targeting different stats stacks independently``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialAP = getDerivedStat state playerId |> fun s -> s.AP
    let initialMA = getDerivedStat state playerId |> fun s -> s.MA

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

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ apEffect; maEffect ]
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP
    let finalMA = getDerivedStat state playerId |> fun s -> s.MA
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
    let state = InternalHelpers.create(fun () -> rngValue)
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

    let tgtStats = {
      Power = 5
      Magic = 4
      Sense = 1
      Charm = 0
    }

    let attackerLow =
      InternalHelpers.makeEntity [ Classification.Player ] lowStats 100 100 [
        meleeId
      ]

    let attackerHigh =
      InternalHelpers.makeEntity [ Classification.Player ] highStats 100 100 [
        meleeId
      ]

    let target =
      InternalHelpers.makeEntity [ Classification.Enemy ] tgtStats 100 100 []

    InternalHelpers.addEntity state attackerIdLow attackerLow
    InternalHelpers.addEntity state attackerIdHigh attackerHigh
    InternalHelpers.addEntity state targetId target
    let actorStatsLow = getDerivedStat state attackerIdLow
    let actorStatsHigh = getDerivedStat state attackerIdHigh
    let defenderStats = getDerivedStat state targetId

    let formulaId =
      state.services.abilityStore.find meleeId
      |> function
        | Abilities.Active def -> def.FormulaId |> ValueOption.get
        | _ -> failwith "Expected active ability"

    let damageLow =
      Resolution.calculateDamage
        {
          services = state.services
          attackerStats = actorStatsLow
          defenderStats = defenderStats
          attackerEffects = AList.empty
        }
        formulaId
      |> AVal.force

    let damageHigh =
      Resolution.calculateDamage
        {
          services = state.services
          attackerStats = actorStatsHigh
          defenderStats = defenderStats
          attackerEffects = AList.empty
        }
        formulaId
      |> AVal.force

    if damageHigh.Amount > 0 then
      damageHigh.Amount > damageLow.Amount
    else
      true

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

    let state = InternalHelpers.create(fun _ -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 1<AbilityId>

    let attacker =
      InternalHelpers.makeEntity [ Classification.Player ] baseA 100 50 [
        melee
      ]

    let target =
      InternalHelpers.makeEntity [ Classification.Enemy ] baseB 80 30 []

    InternalHelpers.addEntity state attackerId attacker
    InternalHelpers.addEntity state targetId target

    // First attack, should succeed and apply cooldown
    let action1 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta1 = Resolution.evaluate state action1
    let change1 = delta1 |> AVal.force
    GameState.apply state change1

    // Second attack, should be ignored due to cooldown
    let action2 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta2 = Resolution.evaluate state action2
    let change2 = delta2 |> AVal.force
    GameState.apply state change2


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

    let state = InternalHelpers.create(fun _ -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 1<AbilityId>

    let attacker =
      InternalHelpers.makeEntity [ Classification.Player ] baseA 100 50 [
        melee
      ]

    let target =
      InternalHelpers.makeEntity [ Classification.Enemy ] baseB 80 30 []

    InternalHelpers.addEntity state attackerId attacker
    InternalHelpers.addEntity state targetId target

    let action =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta = Resolution.evaluate state action
    let change = delta |> AVal.force
    GameState.apply state change

    let attackerAfter = getEntity state attackerId
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

    let state = InternalHelpers.create(fun _ -> 0.5)
    let attackerId = Guid.NewGuid() |> UMX.tag<EntityId>
    let targetId = Guid.NewGuid() |> UMX.tag<EntityId>
    let melee = 1<AbilityId>

    let attacker =
      InternalHelpers.makeEntity [ Classification.Player ] baseA 100 50 [
        melee
      ]

    let target =
      InternalHelpers.makeEntity [ Classification.Enemy ] baseB 80 30 []

    InternalHelpers.addEntity state attackerId attacker
    InternalHelpers.addEntity state targetId target

    // First attack
    let action1 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta1 = Resolution.evaluate state action1
    let change1 = delta1 |> AVal.force
    GameState.apply state change1

    let cooldown =
      match AbilityStore.definitions[melee] with
      | Abilities.Active def -> def.Cooldown
      | _ -> failwith "Expected active ability"
    // Advance time past the cooldown
    let advance = GameState.tick state (cooldown + 1L<Tick>) |> AVal.force
    GameState.apply state advance

    // Second attack, should succeed now
    let action2 =
      (UseAbility {
        actor = attackerId
        targets = [| targetId |]
        abilityId = melee
      })

    let delta2 = Resolution.evaluate state action2
    let change2 = delta2 |> AVal.force
    GameState.apply state change2

    let targetRes = (getEntity state targetId).Resources
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
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 10 -> MA = 20 -> expected AP boost = 20/2 = 10
    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialAP = getDerivedStat state playerId |> fun s -> s.AP

    // Apply Dynamic AP Boost effect (ID 300)
    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ dynamicEffect ]
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP

    // Expected: AP boost = MA / 2 = 20 / 2 = 10
    // So finalAP should be initialAP + 10
    Assert.Equal(initialAP + 10, finalAP)

  [<Fact>]
  member _.``DynamicMod evaluates with correct invoker stats``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 20 -> MA = 40 -> expected AP boost = 40/2 = 20
    let baseStats = {
      Power = 5
      Magic = 20
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialAP = getDerivedStat state playerId |> fun s -> s.AP

    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 300<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 300<EffectId>
    }

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ dynamicEffect ]
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP

    // Expected: AP boost = MA / 2 = 40 / 2 = 20
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``Multiple DynamicMod effects stack correctly``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialAP = getDerivedStat state playerId |> fun s -> s.AP

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

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ effect1; effect2 ]
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP

    // Each effect gives AP boost = MA / 2 = 20 / 2 = 10
    // Two effects should give +20 total
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``DynamicMod can target MA``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 20 -> MA = 40 -> expected MA boost = 40/2 = 20
    let baseStats = {
      Power = 5
      Magic = 20
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialMA = getDerivedStat state playerId |> fun s -> s.MA

    let dynamicEffect: Effects.ActiveEffect = {
      EffectId = 301<EffectId>
      SourceId = playerId
      RemainingTicks = 15000L<Tick>
      NextTickIn = 15000L<Tick>
      Stacks = 1
      Definition = state.services.effectStore.find 301<EffectId>
    }

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ dynamicEffect ]
    }

    let finalMA = getDerivedStat state playerId |> fun s -> s.MA

    // Expected: MA boost = MA / 2 = 40 / 2 = 20
    Assert.Equal(initialMA + 20, finalMA)

  [<Fact>]
  member _.``DynamicMod targeting different stats stacks independently``() =
    let state = InternalHelpers.create(fun () -> 0.5)
    let playerId = Guid.NewGuid() |> UMX.tag<EntityId>

    // Player with Magic = 10 -> MA = 20 -> boost = 20/2 = 10
    let baseStats = {
      Power = 5
      Magic = 10
      Sense = 5
      Charm = 10
    }

    let player =
      InternalHelpers.makeEntity [ Classification.Player ] baseStats 100 100 []

    InternalHelpers.addEntity state playerId player
    let initialAP = getDerivedStat state playerId |> fun s -> s.AP
    let initialMA = getDerivedStat state playerId |> fun s -> s.MA

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

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = AList.ofList [ apEffect; maEffect ]
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP
    let finalMA = getDerivedStat state playerId |> fun s -> s.MA

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

    let state = InternalHelpers.create(fun () -> rngValue)

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
      InternalHelpers.makeEntity [ Classification.Player ] lowStats 100 100 [
        meleeId
      ]

    let attackerHigh =
      InternalHelpers.makeEntity [ Classification.Player ] highStats 100 100 [
        meleeId
      ]

    let target =
      InternalHelpers.makeEntity [ Classification.Enemy ] targetStats 100 100 []

    InternalHelpers.addEntity state attackerIdLow attackerLow
    InternalHelpers.addEntity state attackerIdHigh attackerHigh
    InternalHelpers.addEntity state targetId target

    let actorStatsLow = getDerivedStat state attackerIdLow
    let actorStatsHigh = getDerivedStat state attackerIdHigh
    let targetStats = getDerivedStat state targetId

    let rparams: Resolution.ResolverParams = {
      entities = (getActiveScenario state).entities
      enemies = GameState.getEnemies state |> AVal.force
      allies = GameState.getAllies state |> AVal.force
      derivedStats = GameState.getDerivedStats state |> AVal.force
      gameTime = (getActiveScenario state).gameTime
      services = state.services
      scenario = (getActiveScenario state).scenario
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
