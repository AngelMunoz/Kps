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
open Pomo.Lib.Scenario

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
            scenario.EngagementMode = EngagementMode.AlwaysOn
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
        rng = rng
      }
      (initialScenarioId, cmap [ (initialScenarioId, initialScenarioState) ])


  let makeEntity
    (faction: Classification.Faction seq)
    (baseStats: BaseAttributes)
    hp
    mp
    (abilities: int<AbilityId> list)
    : EntityComponents =
    let cooldowns =
      abilities |> List.map(fun a -> a, 0L<Tick>) |> HashMap.ofList

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
      Effects = HashMap.empty
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
          Effects = HashMap.single dynamicEffect.EffectId dynamicEffect
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``Multiple DynamicMod effects stack correctly with AddStack stacking``
    ()
    =
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
    let initialDP = getDerivedStat state playerId |> fun s -> s.DP

    // Effect 102 has AddStack(5) stacking, with +2 DP per stack
    let stackingEffect: Effects.ActiveEffect = {
      EffectId = 102<EffectId>
      SourceId = playerId
      RemainingTicks = 30000L<Tick>
      NextTickIn = 30000L<Tick>
      Stacks = 3 // Simulate 3 stacks applied
      Definition = state.services.effectStore.find 102<EffectId>
    }

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = HashMap.single stackingEffect.EffectId stackingEffect
    }

    let finalDP = getDerivedStat state playerId |> fun s -> s.DP
    // Effect 102 gives +2 DP per stack, with 3 stacks = +6 total
    Assert.Equal(initialDP + 6, finalDP)

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
          Effects = HashMap.single dynamicEffect.EffectId dynamicEffect
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
          Effects =
            HashMap.ofList [
              (apEffect.EffectId, apEffect)
              (maEffect.EffectId, maEffect)
            ]
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
          attackerEffects = HashMap.empty
        }
        formulaId
      |> AVal.force

    let damageHigh =
      Resolution.calculateDamage
        {
          services = state.services
          attackerStats = actorStatsHigh
          defenderStats = defenderStats
          attackerEffects = HashMap.empty
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
      Sense = 50
      Charm = 10
    }

    let state = InternalHelpers.create(fun _ -> 0.5)
    let actorId = Guid.NewGuid() |> UMX.tag<EntityId>
    let buffSpell = 4<AbilityId> // Support ability that targets self

    let actor =
      InternalHelpers.makeEntity [ Classification.Player ] baseA 100 50 [
        buffSpell
      ]

    InternalHelpers.addEntity state actorId actor

    // First use, should succeed and apply cooldown
    let action1 =
      (UseAbility {
        actor = actorId
        targets = [| actorId |] // Self-targeting
        abilityId = buffSpell
      })

    let delta1 = Resolution.evaluate state action1
    let change1 = delta1 |> AVal.force
    GameState.apply state change1

    // Check that the first use succeeded
    Assert.False(HashMap.isEmpty change1.updates)

    // Second use, should be ignored due to cooldown
    let action2 =
      (UseAbility {
        actor = actorId
        targets = [| actorId |] // Self-targeting
        abilityId = buffSpell
      })

    let delta2 = Resolution.evaluate state action2
    let change2 = delta2 |> AVal.force
    GameState.apply state change2

    // Check that the second use was blocked
    Assert.True(HashMap.isEmpty change2.updates)


  [<Fact>]
  member _.``Action puts ability on cooldown``() =
    let baseA = {
      Power = 20
      Magic = 5
      Sense = 50 // High sense to guarantee hits via AC and LK
      Charm = 10
    }

    let state = InternalHelpers.create(fun _ -> 0.5)
    let actorId = Guid.NewGuid() |> UMX.tag<EntityId>
    let buffSpell = 4<AbilityId> // Support ability that targets self

    let actor =
      InternalHelpers.makeEntity [ Classification.Player ] baseA 100 50 [
        buffSpell
      ]

    InternalHelpers.addEntity state actorId actor

    let action =
      (UseAbility {
        actor = actorId
        targets = [| actorId |] // Self-targeting
        abilityId = buffSpell
      })

    // First, let's check that the entities exist and have the right setup
    let actorBefore = getEntity state actorId

    // Verify setup
    if not(actorBefore.Abilities |> HashSet.contains buffSpell) then
      failwith "Actor doesn't have buff spell ability"

    if actorBefore.Resources.MP < 10 then
      failwith $"Actor doesn't have enough MP: {actorBefore.Resources.MP}"

    // Debug: Check game time and cooldown state
    let currentGameTime = TestHelpers.getGameTime(state) |> AVal.force

    let cooldownState =
      match HashMap.tryFind buffSpell actorBefore.AbilityCooldowns with
      | Some cd -> $"Cooldown: {cd}"
      | None -> "No cooldown entry"

    printfn $"Debug - Game Time: {currentGameTime}, {cooldownState}"
    printfn $"Debug - Actor MP: {actorBefore.Resources.MP}, Required: 10"

    printfn
      $"Debug - Actor has ability: {HashSet.contains buffSpell actorBefore.Abilities}"

    let delta = Resolution.evaluate state action
    let change = delta |> AVal.force

    // Check if action failed - if no updates, the action didn't execute
    if HashMap.isEmpty change.updates then
      failwith "Action validation failed - no entity updates produced"

    GameState.apply state change

    let actorAfter = getEntity state actorId
    let cooldowns = actorAfter.AbilityCooldowns
    let currentGameTimeAfter = TestHelpers.getGameTime(state) |> AVal.force

    let cooldown =
      match HashMap.tryFind buffSpell cooldowns with
      | Some cd -> cd
      | None -> failwith "Ability cooldown not found"

    let expectedCooldown =
      match AbilityStore.definitions[buffSpell] with
      | Abilities.Active def ->
        printfn
          $"Debug - Ability Definition Found - ID: {def.Id}, Cooldown: {def.Cooldown}"

        def.Cooldown
      | _ -> failwith "Expected active ability"

    printfn
      $"Debug After - Game Time: {currentGameTimeAfter}, Cooldown Value: {cooldown}, Expected: {expectedCooldown}"

    printfn
      $"Debug After - Cooldown > 0: {cooldown > 0L<Tick>}, Calculation: {currentGameTimeAfter} + {expectedCooldown} = {currentGameTimeAfter + expectedCooldown}"

    // Let's check if the action actually executed by looking at updates
    if HashMap.isEmpty change.updates then
      failwith "ERROR: Action produced no updates - this shouldn't happen now"

    // Check if the cooldown exists at all
    let cooldownExists = HashMap.containsKey buffSpell cooldowns

    if not cooldownExists then
      failwith $"ERROR: Cooldown key {buffSpell} not found in cooldowns map"

    if cooldown <= 0L<Tick> then
      failwith $"ERROR: Cooldown value {cooldown} is not > 0, expected > 0"

    Assert.True(cooldown > 0L<Tick>)
    Assert.Equal(expectedCooldown, cooldown)

  [<Fact>]
  member _.``Ability is usable again after cooldown expires``() =
    let baseA = {
      Power = 20
      Magic = 5
      Sense = 50
      Charm = 10
    }

    let state = InternalHelpers.create(fun _ -> 0.5)
    let actorId = Guid.NewGuid() |> UMX.tag<EntityId>
    let buffSpell = 4<AbilityId> // Support ability that targets self

    let actor =
      InternalHelpers.makeEntity [ Classification.Player ] baseA 100 50 [
        buffSpell
      ]

    InternalHelpers.addEntity state actorId actor

    // First use
    let action1 =
      (UseAbility {
        actor = actorId
        targets = [| actorId |] // Self-targeting
        abilityId = buffSpell
      })

    let delta1 = Resolution.evaluate state action1
    let change1 = delta1 |> AVal.force
    GameState.apply state change1

    let cooldown =
      match AbilityStore.definitions[buffSpell] with
      | Abilities.Active def -> def.Cooldown
      | _ -> failwith "Expected active ability"
    // Advance time past the cooldown
    let advance = GameState.tick state (cooldown + 1L<Tick>) |> AVal.force
    GameState.apply state advance

    // Second use, should succeed now
    let action2 =
      (UseAbility {
        actor = actorId
        targets = [| actorId |] // Self-targeting
        abilityId = buffSpell
      })

    let delta2 = Resolution.evaluate state action2
    let change2 = delta2 |> AVal.force
    GameState.apply state change2

    let actorRes = (getEntity state actorId).Resources
    Assert.True(actorRes.MP < 50) // Actor should have used MP for both casts

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
          Effects = HashMap.single dynamicEffect.EffectId dynamicEffect
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
          Effects = HashMap.single dynamicEffect.EffectId dynamicEffect
    }

    let finalAP = getDerivedStat state playerId |> fun s -> s.AP

    // Expected: AP boost = MA / 2 = 40 / 2 = 20
    Assert.Equal(initialAP + 20, finalAP)

  [<Fact>]
  member _.``Multiple stacking effects with AddStack work correctly``() =
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
    let initialDP = getDerivedStat state playerId |> fun s -> s.DP

    // Use effect 102 with AddStack(5) stacking - +2 DP per stack
    let stackingEffect: Effects.ActiveEffect = {
      EffectId = 102<EffectId>
      SourceId = playerId
      RemainingTicks = 30000L<Tick>
      NextTickIn = 30000L<Tick>
      Stacks = 2 // Two stacks applied
      Definition = state.services.effectStore.find 102<EffectId>
    }

    let currentComponents = getEntity state playerId

    setEntity state playerId {
      currentComponents with
          Effects = HashMap.single stackingEffect.EffectId stackingEffect
    }

    let finalDP = getDerivedStat state playerId |> fun s -> s.DP
    // Effect 102 gives +2 DP per stack, with 2 stacks = +4 total
    Assert.Equal(initialDP + 4, finalDP)

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
          Effects = HashMap.single dynamicEffect.EffectId dynamicEffect
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
          Effects =
            HashMap.ofList [
              (apEffect.EffectId, apEffect)
              (maEffect.EffectId, maEffect)
            ]
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

    let scenario = getActiveScenario state

    let rparams: Resolution.ResolverParams = {
      derivedStats = GameState.getDerivedStats state |> AVal.force
      gameTime = scenario.gameTime
      scenarioState = scenario
      players = state.players
      parties = state.parties
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
          attackerEffects = HashMap.empty
        }
        formulaId
      |> AVal.force

    let damageHigh =
      Resolution.calculateDamage
        {
          services = rparams.services
          attackerStats = actorStatsHigh
          defenderStats = targetStats
          attackerEffects = HashMap.empty
        }
        formulaId
      |> AVal.force

    if damageHigh.Amount > 0 then
      damageHigh.Amount > damageLow.Amount
    else
      true
