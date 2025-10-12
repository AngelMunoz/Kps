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

type GameState = {
  entities: cmap<Guid<EntityId>, EntityComponents>
  gameTime: cval<int64<Tick>>
  services: Services.EngineServices
  bounds: ScenarioBounds
}


module GameState =

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

  let getAllies(state: GameState) =
    state.entities
    |> AMap.filter(fun _ c ->
      c.Factions |> HashSet.contains Classification.Ally)

  let getEnemies(state: GameState) =
    state.entities
    |> AMap.filter(fun _ c ->
      c.Factions |> HashSet.contains Classification.Enemy)

  let getValidTargets(state: GameState) =
    state.entities
    |> AMap.filter(fun _ c ->
      c.Factions |> HashSet.contains Classification.Ally
      || c.Factions |> HashSet.contains Classification.Enemy)

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
    (effects: alist<ActiveEffect>)
    (equipment: HashMap<Inventory.Slot, Inventory.Equipment>)
    : aval<DerivedStats> =

    adaptive {
      let struct (equipmentStatBonuses, equipmentElementalAttributes,
                  equipmentElementalResistances) =
        aggregateEquipment equipment
      // Gather all effect modifiers from active effects
      let modifiers =
        effects
        |> AList.collect(fun effect ->
          getModifiersForEffect services.effectStore effect.EffectId
          |> AList.ofArray)

      // Aggregate static modifiers by Stat and kind
      let! addMap, subMap, mulMap, divMap =
        modifiers
        |> AList.fold
          (fun (addMap, subMap, mulMap, divMap) modifier ->
            match modifier with
            | EffectModifier.StaticMod statMod ->
              match statMod with
              | StatModifier.Additive(stat, value) ->
                let addMap =
                  match HashMap.tryFindV stat addMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing + value) addMap
                  | ValueNone -> HashMap.add stat value addMap

                addMap, subMap, mulMap, divMap
              | StatModifier.Subtractive(stat, value) ->
                let subMap =
                  match HashMap.tryFindV stat subMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing + value) subMap
                  | ValueNone -> HashMap.add stat value subMap

                addMap, subMap, mulMap, divMap
              | StatModifier.Multiplicative(stat, value) ->
                let mulMap =
                  match HashMap.tryFindV stat mulMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing * value) mulMap
                  | ValueNone -> HashMap.add stat value mulMap

                addMap, subMap, mulMap, divMap
              | StatModifier.Divisive(stat, value) ->
                let divMap =
                  match HashMap.tryFindV stat divMap with
                  | ValueSome existing ->
                    HashMap.add stat (existing * value) divMap
                  | ValueNone -> HashMap.add stat value divMap

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
      let! dynamicAddMap =
        modifiers
        |> AList.fold
          (fun dynAddMap modifier ->
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

  let create'(services: Services.EngineServices) = {
    entities = cmap()
    gameTime = cval 0L<Tick>
    services = services
    bounds = {
      Width = 2000f
      Height = 2000f
      CenterX = 0f
      CenterY = 0f
    }
  }

  let create() =
    create' {
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

  let getDerivedStats(state: GameState) =
    state.entities
    |> AMap.mapA(fun _ c ->
      applyModifiers state.services c.BaseStats c.Effects c.Equipment)

  [<Struct>]
  type EntityChange = { components: EntityComponents }

  let tick (state: GameState) (time: int64<Tick>) : aval<StateChange> = adaptive {
    let! currentTime = state.gameTime
    let newTime = currentTime + time

    let! allEntityChanges =
      state.entities
      |> AMap.mapA(fun entityId components -> adaptive {
        let! updatedEffects, tickResult =
          StatusEffects.tickEffects
            state.services.effectStore
            components.Effects
            time

        let movedComponents =
          Pomo.Lib.Movement.Update.updateEntityWithContext
            time
            state.bounds
            (state.entities |> AMap.force)
            entityId
            components

        let! derivedStats = getDerivedStats state |> AMap.tryFind entityId

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
              Effects = updatedEffects |> AList.ofIndexList
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
    }
  }

  let apply (state: GameState) (change: StateChange) =
    transact(fun _ ->
      match change.gameTime with
      | ValueSome newTime -> state.gameTime.Value <- newTime
      | ValueNone -> ()

      for entityId, updatedComponents in change.updates do
        state.entities[entityId] <- updatedComponents

      for entityId, newComponents in change.additions do
        state.entities.Add(entityId, newComponents) |> ignore

      for entityId in change.removals do
        state.entities.Remove entityId |> ignore)


module Projections =


  let aAlive entities =
    entities
    |> AMap.toASet
    |> ASet.filter(fun (_, c) -> c.Resources.Status = Status.Alive)
    |> ASet.map fst

  let aReadyAbilities entities gameTime =
    let allCoolDowns =
      entities
      |> AMap.toASet
      |> ASet.collect(fun (id, c) ->
        c.AbilityCooldowns
        |> AMap.toASet
        |> ASet.map(fun (abilityId, readyTick) -> id, abilityId, readyTick))

    allCoolDowns
    |> ASet.filterA(fun (_, _, readyTick) -> adaptive {
      let! gameTime = gameTime
      return readyTick <= gameTime
    })
    |> ASet.map(fun (id, abilityId, _) -> id, abilityId)

  let aReadyForEntity entity gameTime =
    entity.AbilityCooldowns
    |> AMap.toASet
    |> ASet.chooseA(fun (abilityId, readyTick) -> adaptive {
      let! gameTime = gameTime

      if readyTick <= gameTime then
        return Some abilityId
      else
        return None
    })
