namespace Pomo.Lib.Gameplay

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Effects
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.AggregatedEffects

type GameState = {
  entities: cmap<int<EntityId>, EntityComponents>
  gameTime: cval<int64<Tick>>
  services: Services.EngineServices
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

  let private applyModifiers
    (effectStore: Services.IEffectStore)
    (baseStats: BaseAttributes)
    (effects: alist<ActiveEffect>)
    : aval<DerivedStats> =
    adaptive {
      // Gather all effect modifiers from active effects
      let modifiers =
        effects
        |> AList.collect(fun effect ->
          getModifiersForEffect effectStore effect.EffectId |> AList.ofArray)

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
        let pre = current + addV - subV
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

        // TODO: Grab elements from equipment, buffs, etc.
        ElementAttributes = FSharp.Data.Adaptive.HashMap.empty
        ElementResistances = FSharp.Data.Adaptive.HashMap.empty
      }

      // 3) Apply derived stat static modifiers (all kinds)
      let finalDerived = {
        initialDerived with
            HP = applyAll HP initialDerived.HP
            MP = applyAll MP initialDerived.MP
            AP = applyAll AP initialDerived.AP
            MA = applyAll MA initialDerived.MA
            MD = applyAll MD initialDerived.MD
            DA = applyAll DA initialDerived.DA
            DX = applyAll DX initialDerived.DX
            WT = applyAll WT initialDerived.WT
            LK = applyAll LK initialDerived.LK
            DP = applyAll DP initialDerived.DP
            AC = applyAll AC initialDerived.AC
            HV = applyAll HV initialDerived.HV
      }

      return finalDerived
    }

  let create'(services: Services.EngineServices) = {
    entities = cmap()
    gameTime = cval 0L<Tick>
    services = services
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

  let getDerivedStats(state: GameState) : amap<int<EntityId>, DerivedStats> =
    state.entities
    |> AMap.mapA(fun _ c ->
      applyModifiers state.services.effectStore c.BaseStats c.Effects)

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

        let! derivedStats = getDerivedStats state |> AMap.tryFind entityId

        let maxHp =
          match derivedStats with
          | Some stats -> stats.HP
          | None -> components.Resources.HP

        let currentHp = components.Resources.HP
        let newHp = min maxHp (currentHp + tickResult.Healing)
        let finalHp = max 0 (newHp - tickResult.Damage)

        let updatedResources = {
          components.Resources with
              HP = finalHp
        }

        let updatedComponents = {
          components with
              Effects = updatedEffects |> AList.ofIndexList
              Resources = updatedResources
        }

        return { components = updatedComponents }
      })
      |> AMap.toAVal

    let entities =
      allEntityChanges |> HashMap.map(fun _ change -> change.components)


    return {
      entities = entities
      gameTime = ValueSome newTime
    }
  }

  let applyTick (state: GameState) (change: StateChange) =
    transact(fun _ ->
      match change.gameTime with
      | ValueSome newTime -> state.gameTime.Value <- newTime
      | ValueNone -> ()

      for entityId, updatedComponents in change.entities do
        state.entities[entityId] <- updatedComponents)

  let aAlive entities : aset<int<EntityId>> =
    entities
    |> AMap.toASet
    |> ASet.filter(fun (_, c) -> c.Resources.Status = Status.Alive)
    |> ASet.map fst

  let aReadyAbilities entities gameTime : aset<int<EntityId> * int<AbilityId>> =
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
