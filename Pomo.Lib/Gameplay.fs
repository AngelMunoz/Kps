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


  let private applyModifiers
    (effectStore: Services.IEffectStore)
    (baseStats: BaseAttributes)
    (effects: alist<ActiveEffect>)
    : aval<DerivedStats> =
    adaptive {
      let modifiers =
        effects
        |> AList.collect(fun effect ->
          getModifiersForEffect effectStore effect.EffectId |> AList.ofArray)

      let additiveModifiers = getAdditiveModifiers modifiers
      // 1. Apply base stat modifiers
      let! modifiedBase =
        additiveModifiers
        |> AMap.fold
          (fun acc stat value ->
            let currentBase = acc
            let v = value

            match stat with
            | Power -> {
                currentBase with
                    Power = currentBase.Power + v
              }
            | Magic -> {
                currentBase with
                    Magic = currentBase.Magic + v
              }
            | Sense -> {
                currentBase with
                    Sense = currentBase.Sense + v
              }
            | Charm -> {
                currentBase with
                    Charm = currentBase.Charm + v
              }
            | _ -> currentBase)
          baseStats

      // 2. Calculate initial derived stats from modified base stats
      let initialDerived = {
        // Power derived stats
        AP = modifiedBase.Power * 2
        AC = modifiedBase.Power / 100
        DX = modifiedBase.Power
        // Magic derived stats
        MP = modifiedBase.Magic * 5
        MA = modifiedBase.Magic * 2
        MD = modifiedBase.Magic
        // Sense derived stats
        WT = modifiedBase.Sense
        DA = modifiedBase.Sense
        LK = modifiedBase.Sense
        // Charm derived stats
        HP = modifiedBase.Charm * 10
        DP = modifiedBase.Charm / 2
        HV = modifiedBase.Charm / 100

        // TODO: Grab elements from equipment, buffs, etc.
        ElementAttributes = FSharp.Data.Adaptive.HashMap.empty
        ElementResistances = FSharp.Data.Adaptive.HashMap.empty
      }

      // 3. Apply derived stat modifiers
      let! finalDerived =
        additiveModifiers
        |> AMap.fold
          (fun acc stat value ->
            let currentDerived: DerivedStats = acc
            let v = value

            match stat with
            | HP -> {
                currentDerived with
                    HP = currentDerived.HP + v
              }
            | MP -> {
                currentDerived with
                    MP = currentDerived.MP + v
              }
            | AP -> {
                currentDerived with
                    AP = currentDerived.AP + v
              }
            | MA -> {
                currentDerived with
                    MA = currentDerived.MA + v
              }
            | MD -> {
                currentDerived with
                    MD = currentDerived.MD + v
              }
            | DA -> {
                currentDerived with
                    DA = currentDerived.DA + v
              }
            | DX -> {
                currentDerived with
                    DX = currentDerived.DX + v
              }
            | WT -> {
                currentDerived with
                    WT = currentDerived.WT + v
              }
            | LK -> {
                currentDerived with
                    LK = currentDerived.LK + v
              }
            | DP -> {
                currentDerived with
                    DP = currentDerived.DP + v
              }
            | AC -> {
                currentDerived with
                    AC = currentDerived.AC + v
              }
            | HV -> {
                currentDerived with
                    HV = currentDerived.HV + v
              }
            | _ -> currentDerived)
          initialDerived

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
