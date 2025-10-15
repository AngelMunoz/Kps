namespace Pomo.Lib.Gameplay

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Effects
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.AggregatedEffects
open Pomo.Lib.Scenario

type GameState = {
  scenarios: cmap<Guid<ScenarioId>, ScenarioState>
  activeScenarioId: Guid<ScenarioId> cval
  players: cmap<Guid<PlayerId>, PlayerContext>
  parties: cmap<Guid<PartyId>, Party>
  services: Services.EngineServices
}


module GameState =

  let getActiveScenario(state: GameState) = adaptive {
    let! scenarioId = state.activeScenarioId
    return state.scenarios[scenarioId]
  }

  let getModifiersForEffect (effectStore: Services.IEffectStore) effectId =
    let effect = effectStore.tryFind effectId

    match effect with
    | ValueSome e -> e.Modifiers
    | ValueNone -> Array.empty

  let getAdditiveModifiers(effects: EffectModifier alist) =
    effects
    |> AList.fold
      (fun acc modifier ->
        match modifier with
        | EffectModifier.StaticMod(StatModifier.Additive(stat, value)) ->
          match HashMap.tryFind stat acc with
          | Some existing -> HashMap.add stat (existing + value) acc
          | None -> HashMap.add stat value acc
        | _ -> acc)
      HashMap.empty
    |> AMap.ofAVal

  let getAllies(state: GameState) = adaptive {
    let! scenario = getActiveScenario state

    return
      scenario.entities
      |> AMap.filter(fun _ c ->
        c.Factions |> HashSet.contains Classification.Ally)
  }

  let getEnemies(state: GameState) = adaptive {
    let! scenario = getActiveScenario state

    return
      scenario.entities
      |> AMap.filter(fun _ c ->
        c.Factions |> HashSet.contains Classification.Enemy)
  }

  let getValidTargets(state: GameState) = adaptive {
    let! scenario = getActiveScenario state

    let entities =
      scenario.entities
      |> AMap.filter(fun _ c ->
        c.Factions |> HashSet.contains Classification.Ally
        || c.Factions |> HashSet.contains Classification.Enemy)
      |> AMap.toAVal

    return entities
  }

  let private aggregateEquipment
    (equipment: HashMap<Inventory.Slot, Inventory.Equipment>)
    =
    let mutable equipmentStatBonuses = HashMap.empty<Stat, int>
    let mutable equipmentElementalAttributes = HashMap.empty<Element, float>
    let mutable equipmentElementalResistances = HashMap.empty<Element, float>

    for item in equipment |> HashMap.toValueArray do
      for bonus in item.StatBonuses do
        equipmentStatBonuses <-
          equipmentStatBonuses
          |> HashMap.alterV bonus.Stat (fun existing ->
            match existing with
            | ValueSome value -> ValueSome(value + bonus.Value)
            | ValueNone -> ValueSome bonus.Value)

      for struct (element, value) in
        item.ElementalAttributes |> HashMap.toArrayV do
        equipmentElementalAttributes <-
          equipmentElementalAttributes
          |> HashMap.alterV element (fun existing ->
            match existing with
            | ValueSome existingValue -> ValueSome(existingValue + value)
            | ValueNone -> ValueSome value)

      for struct (element, value) in
        item.ElementalResistances |> HashMap.toArrayV do
        equipmentElementalResistances <-
          equipmentElementalResistances
          |> HashMap.alterV element (fun existing ->
            match existing with
            | ValueSome existingValue -> ValueSome(existingValue + value)
            | ValueNone -> ValueSome value)

    struct (equipmentStatBonuses,
            equipmentElementalAttributes,
            equipmentElementalResistances)

  let private applyModifiers
    (services: Services.EngineServices)
    (baseStats: BaseAttributes)
    (effects: HashMap<int<EffectId>, ActiveEffect>)
    (equipment: HashMap<Inventory.Slot, Inventory.Equipment>)
    : aval<DerivedStats> =

    adaptive {
      let struct (equipmentStatBonuses, equipmentElementalAttributes,
                  equipmentElementalResistances) =
        aggregateEquipment equipment
      // Gather all effect modifiers from active effects
      let modifiers =
        effects
        |> HashMap.toArrayV
        |> Array.collect(fun struct (_, effect) ->
          let mods = getModifiersForEffect services.effectStore effect.EffectId
          mods |> Array.map(fun m -> struct (m, effect.Stacks)))

      // Aggregate static modifiers by Stat and kind
      let addMap, subMap, mulMap, divMap =
        modifiers
        |> Array.fold
          (fun (addMap, subMap, mulMap, divMap) (struct (modifier, stacks)) ->
            match modifier with
            | EffectModifier.StaticMod statMod ->
              match statMod with
              | StatModifier.Additive(stat, value) ->
                let total = value * stacks

                let addMap =
                  match HashMap.tryFindV stat addMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing + total) addMap
                  | ValueNone -> HashMap.add stat total addMap

                addMap, subMap, mulMap, divMap
              | StatModifier.Subtractive(stat, value) ->
                let total = value * stacks

                let subMap =
                  match HashMap.tryFindV stat subMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing + total) subMap
                  | ValueNone -> HashMap.add stat total subMap

                addMap, subMap, mulMap, divMap
              | StatModifier.Multiplicative(stat, value) ->
                let stackedValue =
                  if stacks > 1 then Math.Pow(value, float stacks) else value

                let mulMap =
                  match HashMap.tryFindV stat mulMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing * stackedValue) mulMap
                  | ValueNone -> HashMap.add stat stackedValue mulMap

                addMap, subMap, mulMap, divMap
              | StatModifier.Divisive(stat, value) ->
                let stackedValue =
                  if stacks > 1 then Math.Pow(value, float stacks) else value

                let divMap =
                  match HashMap.tryFindV stat divMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing * stackedValue) divMap
                  | ValueNone -> HashMap.add stat stackedValue divMap

                addMap, subMap, mulMap, divMap
            | _ -> addMap, subMap, mulMap, divMap)
          (HashMap.empty, HashMap.empty, HashMap.empty, HashMap.empty)

      // Helper to apply aggregated modifiers to a given stat value
      let inline applyAll stat current =
        let addV = HashMap.tryFindV stat addMap |> ValueOption.defaultValue 0
        let subV = HashMap.tryFindV stat subMap |> ValueOption.defaultValue 0
        let mulV = HashMap.tryFindV stat mulMap |> ValueOption.defaultValue 1.0
        let divV = HashMap.tryFindV stat divMap |> ValueOption.defaultValue 1.0

        let equipBonus =
          HashMap.tryFindV stat equipmentStatBonuses
          |> ValueOption.defaultValue 0

        let pre = current + addV - subV + equipBonus
        let scaled = int(float pre * mulV / divV)
        scaled

      // 1) Apply base stat modifiers (only to base stats)
      let modifiedBase = {
        baseStats with
            Power = applyAll Power baseStats.Power
            Magic = applyAll Magic baseStats.Magic
            Sense = applyAll Sense baseStats.Sense
            Charm = applyAll Charm baseStats.Charm
      }

      // 2) Compute derived stats from modified base
      let initialDerived = {
        // Power derived stats
        AP = modifiedBase.Power * 2
        AC = modifiedBase.Power + int(float modifiedBase.Power * 1.25)
        DX = modifiedBase.Power
        // Magic derived stats
        MP = modifiedBase.Magic * 5
        MA = modifiedBase.Magic * 2
        MD = modifiedBase.Magic + int(float modifiedBase.Magic * 1.25)
        // Sense derived stats
        WT = modifiedBase.Sense * 5
        DA = modifiedBase.Sense * 2
        LK = modifiedBase.Sense + int(float modifiedBase.Sense * 0.5)
        // Charm derived stats
        HP = modifiedBase.Charm * 10
        DP = modifiedBase.Charm + int(float modifiedBase.Charm * 1.25)
        HV = modifiedBase.Charm * 2

        // Equipment elemental attributes and resistances
        ElementAttributes = equipmentElementalAttributes
        ElementResistances = equipmentElementalResistances
      }

      // 2.5) Process DynamicMod modifiers: evaluate formulas and add to addMap
      let dynamicAddMap =
        modifiers
        |> Array.fold
          (fun dynAddMap (struct (modifier, _stacks)) ->
            match modifier with
            | EffectModifier.DynamicMod(formulaId, stat) ->
              match services.formulaStore.tryFind formulaId with
              | ValueSome formula ->
                // Evaluate formula with current derived stats as context
                let context: Abilities.CalculationContext = {
                  InvokerStats = initialDerived
                  InvokerElementalAttributes = initialDerived.ElementAttributes
                  TargetElementalResistances = HashMap.empty
                }

                let result = formula.Calculate context
                // Use BaseDamage as the stat modifier value
                let value = result.BaseDamage

                match HashMap.tryFindV stat dynAddMap with
                | ValueSome existing ->
                  HashMap.add stat (existing + value) dynAddMap
                | ValueNone -> HashMap.add stat value dynAddMap
              | ValueNone -> dynAddMap
            | _ -> dynAddMap)
          HashMap.empty

      // Merge dynamic modifiers into addMap
      let finalAddMap =
        dynamicAddMap
        |> HashMap.fold
          (fun acc stat value ->
            match HashMap.tryFindV stat acc with
            | ValueSome existing -> HashMap.add stat (existing + value) acc
            | ValueNone -> HashMap.add stat value acc)
          addMap

      // Helper to apply all modifiers (including dynamic) to derived stats
      let inline applyAllWithDynamic stat current =
        let addV =
          HashMap.tryFindV stat finalAddMap |> ValueOption.defaultValue 0

        let subV = HashMap.tryFindV stat subMap |> ValueOption.defaultValue 0
        let mulV = HashMap.tryFindV stat mulMap |> ValueOption.defaultValue 1.0
        let divV = HashMap.tryFindV stat divMap |> ValueOption.defaultValue 1.0
        let pre = current + addV - subV
        let scaled = int(float pre * mulV / divV)
        scaled

      // 3) Apply derived stat static and dynamic modifiers (all kinds)
      let finalDerived = {
        initialDerived with
            HP = applyAllWithDynamic HP initialDerived.HP
            MP = applyAllWithDynamic MP initialDerived.MP
            AP = applyAllWithDynamic AP initialDerived.AP
            MA = applyAllWithDynamic MA initialDerived.MA
            MD = applyAllWithDynamic MD initialDerived.MD
            DA = applyAllWithDynamic DA initialDerived.DA
            DX = applyAllWithDynamic DX initialDerived.DX
            WT = applyAllWithDynamic WT initialDerived.WT
            LK = applyAllWithDynamic LK initialDerived.LK
            DP = applyAllWithDynamic DP initialDerived.DP
            AC = applyAllWithDynamic AC initialDerived.AC
            HV = applyAllWithDynamic HV initialDerived.HV
      }

      return finalDerived
    }

  let create'
    (services: Services.EngineServices)
    (
      activeScenarioId: Guid<ScenarioId>,
      scenarios: cmap<Guid<ScenarioId>, ScenarioState>
    ) =
    {
      scenarios = scenarios
      activeScenarioId = cval activeScenarioId
      players = cmap()
      parties = cmap()
      services = services
    }

  let create() =
    let initialScenarioId = %Guid.NewGuid()

    let initialScenarioState =
      {
        Id = initialScenarioId
        Name = "Test Scenario"
        BoundsWidth = 2000f
        BoundsHeight = 2000f
      }
      |> ScenarioState.create id

    create'
      {
        effectStore =
          { new Services.IEffectStore with
              member _.tryFind effectId =
                Pomo.Lib.Content.EffectStore.definitions
                |> Map.tryFind effectId
                |> ValueOption.ofOption

              member _.find effectId =
                Pomo.Lib.Content.EffectStore.definitions |> Map.find effectId
          }
        abilityStore =
          { new Services.IAbilityStore with
              member _.tryFind abilityId =
                Pomo.Lib.Content.AbilityStore.definitions
                |> Map.tryFind abilityId
                |> ValueOption.ofOption

              member _.find abilityId =
                Pomo.Lib.Content.AbilityStore.definitions |> Map.find abilityId
          }
        formulaStore =
          { new Services.IFormulaStore with
              member _.tryFind formulaId =
                Pomo.Lib.Content.FormulaStore.definitions
                |> Map.tryFind formulaId
                |> ValueOption.ofOption

              member _.find formulaId =
                Pomo.Lib.Content.FormulaStore.definitions |> Map.find formulaId
          }
        rng = fun () -> System.Random().NextDouble()
      }
      (initialScenarioId, cmap [ initialScenarioId, initialScenarioState ])


  let getDerivedStats(state: GameState) = adaptive {
    let! scenario = getActiveScenario state

    return
      scenario.entities
      |> AMap.mapA(fun _ c ->
        applyModifiers state.services c.BaseStats c.Effects c.Equipment)
  }

  [<Struct>]
  type EntityChange = { components: EntityComponents }

  let tick (state: GameState) (time: int64<Tick>) : aval<StateChange> = adaptive {
    let! activeId = state.activeScenarioId
    let scenario = state.scenarios[activeId]
    let! currentTime = scenario.gameTime

    let newTime = currentTime + time

    let! allEntityChanges =
      scenario.entities
      |> AMap.mapA(fun entityId components -> adaptive {
        let struct (updatedEffects, tickResult) =
          StatusEffects.tickEffects
            state.services.effectStore
            components.Effects
            time

        let bounds = {
          Width = scenario.scenario.BoundsWidth
          Height = scenario.scenario.BoundsHeight
          CenterX = 0f
          CenterY = 0f
        }

        let! _entities = scenario.entities |> AMap.toAVal

        let movedComponents =
          Pomo.Lib.Movement.Update.updateEntityWithContext
            time
            bounds
            scenario.scenario
            _entities
            entityId
            components

        let! derivedStats = adaptive {
          let! derived = getDerivedStats state
          return! derived |> AMap.tryFind entityId
        }

        let maxHp =
          match derivedStats with
          | Some stats -> stats.HP
          | None -> components.Resources.HP

        let currentHp = components.Resources.HP
        let newHp = min maxHp (currentHp + tickResult.Healing)
        let finalHp = max 0 (newHp - tickResult.Damage)

        let updatedResources = {
          movedComponents.Resources with
              HP = finalHp
        }

        let updatedComponents = {
          movedComponents with
              Effects = updatedEffects
              Resources = updatedResources
        }

        return { components = updatedComponents }
      })
      |> AMap.toAVal

    let entities =
      allEntityChanges |> HashMap.map(fun _ change -> change.components)

    return {
      updates = entities
      additions = HashMap.empty
      removals = Array.empty
      gameTime = ValueSome newTime
      scenarioChanges = Array.empty
    }
  }

  let apply (state: GameState) (change: StateChange) =
    transact(fun _ ->
      let activeId = state.activeScenarioId.Value
      let scenario = state.scenarios[activeId]

      match change.gameTime with
      | ValueSome newTime -> scenario.gameTime.Value <- newTime
      | ValueNone -> ()

      for sc in change.scenarioChanges do
        match sc with
        | ScenarioChange.AddBattleInstance bi ->
            scenario.battleInstances.Add(bi.Id, bi) |> ignore
        | ScenarioChange.UpdateBattleInstance bi ->
            scenario.battleInstances[bi.Id] <- bi
        | ScenarioChange.RemoveBattleInstance biId ->
            scenario.battleInstances.Remove biId |> ignore
        | ScenarioChange.AddPendingDuel (requester, target) ->
            scenario.pendingDuels.Add(requester, target) |> ignore
        | ScenarioChange.RemovePendingDuel requester ->
            scenario.pendingDuels.Remove requester |> ignore

      for entityId, updatedComponents in change.updates do
        scenario.entities[entityId] <- updatedComponents

      for entityId, newComponents in change.additions do
        scenario.entities.Add(entityId, newComponents) |> ignore

      for entityId in change.removals do
        scenario.entities.Remove entityId |> ignore)


module Projections =


  let aAlive entities =
    entities |> AMap.filter(fun _ c -> c.Resources.Status.IsAlive)

  let aReadyAbilities entities gameTime = adaptive {
    let! gameTime = gameTime
    let! entities = entities |> AMap.toAVal
    let entities = entities |> HashMap.toArrayV

    return
      entities
      |> Array.collect(fun struct (_, c) ->
        c.AbilityCooldowns
        |> HashMap.toArrayV
        |> Array.filter(fun struct (_, readyTick) -> readyTick <= gameTime))
      |> HashMap.OfArray

  }

  let aReadyForEntity entity gameTime =
    adaptive {
      let! gameTime = gameTime

      return
        entity.AbilityCooldowns
        |> HashMap.chooseV(fun abilityId readyTick ->
          if readyTick <= gameTime then
            ValueSome abilityId
          else
            ValueNone)

    }
    |> AMap.ofAVal
