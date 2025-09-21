namespace Pomo.Lib.Gameplay

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.GameEvent
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Effects
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.AggregatedEffects

type GameState = {
  entities: cmap<int<EntityId>, All>
  gameEvents: clist<GameEvent>
  gameTime: cval<int64<Tick>>
  services: Services.EngineServices
}


module GameState =

  let getModifiersForEffect (effectStore: Services.IEffectStore) effectId =
    let effect = effectStore.tryFind effectId

    match effect with
    | ValueSome e -> e.Modifiers
    | ValueNone -> FSharp.Data.Adaptive.IndexList.empty
    |> AList.ofIndexList

  let getAdditiveModifiers(effects: StatModifier alist) =
    effects
    |> AList.fold
      (fun acc modifier ->
        match modifier with
        | StatModifier.Additive(stat, value) ->
          match HashMap.tryFind stat acc with
          | Some existing -> HashMap.add stat (existing + value) acc
          | None -> HashMap.add stat value acc
        | _ -> acc)
      HashMap.empty
    |> AMap.ofAVal


  let private applyModifiers
    (effectStore: Services.IEffectStore)
    (baseStats: Attributes.BaseAttributes)
    (effects: alist<Effects.ActiveEffect>)
    : aval<Attributes.DerivedStats> =
    adaptive {
      let modifiers =
        effects
        |> AList.collect(fun effect ->
          getModifiersForEffect effectStore effect.EffectId)

      let additiveModifiers = getAdditiveModifiers modifiers
      // 1. Apply base stat modifiers
      let! modifiedBase =
        additiveModifiers
        |> AMap.fold
          (fun acc stat value ->
            let currentBase = acc
            let v = value

            match stat with
            | Effects.Stat.Power -> {
                currentBase with
                    Power = currentBase.Power + v
              }
            | Effects.Stat.Magic -> {
                currentBase with
                    Magic = currentBase.Magic + v
              }
            | Effects.Stat.Sense -> {
                currentBase with
                    Sense = currentBase.Sense + v
              }
            | Effects.Stat.Charm -> {
                currentBase with
                    Charm = currentBase.Charm + v
              }
            | _ -> currentBase)
          baseStats

      // 2. Calculate initial derived stats from modified base stats
      let initialDerived = {
        // Power derived stats
        AP = modifiedBase.Power * 2
        AC = float modifiedBase.Power / 100.0
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
        HV = float modifiedBase.Charm / 100.0
        Resistances = FSharp.Data.Adaptive.HashMap.empty // Placeholder
      }

      // 3. Apply derived stat modifiers
      let! finalDerived =
        additiveModifiers
        |> AMap.fold
          (fun acc stat value ->
            let currentDerived: DerivedStats = acc
            let v = value

            match stat with
            | Effects.Stat.HealthPool -> {
                currentDerived with
                    HP = currentDerived.HP + v
              }
            | Effects.Stat.ManaPool -> {
                currentDerived with
                    MP = currentDerived.MP + v
              }
            | Effects.Stat.AP -> {
                currentDerived with
                    AP = currentDerived.AP + v
              }
            | Effects.Stat.MA -> {
                currentDerived with
                    MA = currentDerived.MA + v
              }
            | Effects.Stat.MD -> {
                currentDerived with
                    MD = currentDerived.MD + v
              }
            | Effects.Stat.DA -> {
                currentDerived with
                    DA = currentDerived.DA + v
              }
            | Effects.Stat.DX -> {
                currentDerived with
                    DX = currentDerived.DX + v
              }
            | Effects.Stat.WT -> {
                currentDerived with
                    WT = currentDerived.WT + v
              }
            | Effects.Stat.LK -> {
                currentDerived with
                    LK = currentDerived.LK + v
              }
            | Effects.Stat.DP -> {
                currentDerived with
                    DP = currentDerived.DP + v
              }
            | Effects.Stat.AC -> {
                currentDerived with
                    AC = currentDerived.AC + float v
              }
            | Effects.Stat.HV -> {
                currentDerived with
                    HV = currentDerived.HV + float v
              }
            | _ -> currentDerived)
          initialDerived

      return finalDerived
    }

  let create'(services: Services.EngineServices) = {
    entities = cmap()
    gameEvents = clist []
    gameTime = cval 0L<Tick>
    services = services
  }

  let create() =
    let effMap =
      Pomo.Lib.Content.EffectStore.definitions
      |> HashMap.ofMap
      |> AMap.ofHashMap

    let abilMap =
      Pomo.Lib.Content.AbilityStore.definitions
      |> HashMap.ofMap
      |> AMap.ofHashMap

    let effList =
      AList.constant(fun () ->
        [ for KeyValue(_, v) in Pomo.Lib.Content.EffectStore.definitions -> v ]
        |> IndexList.ofList)

    let abilList =
      AList.constant(fun () ->
        [
          for KeyValue(_, v) in Pomo.Lib.Content.AbilityStore.definitions -> v
        ]
        |> IndexList.ofList)

    create' {
      effectStore =
        { new Services.IEffectStore with
            member _.tryFind effectId =
              Pomo.Lib.Content.EffectStore.definitions
              |> Map.tryFind effectId
              |> ValueOption.ofOption

            member _.find effectId =
              Pomo.Lib.Content.EffectStore.definitions |> Map.find effectId

            member _.asAMap = effMap

            member _.asAList = effList

            member _.asList =
              [
                for KeyValue(_, v) in Pomo.Lib.Content.EffectStore.definitions ->
                  v
              ]
              |> FSharp.Data.Adaptive.IndexList.ofList
        }
      abilityStore =
        { new Services.IAbilityStore with
            member _.tryFind abilityId =
              Pomo.Lib.Content.AbilityStore.definitions
              |> Map.tryFind abilityId
              |> ValueOption.ofOption

            member _.find abilityId =
              Pomo.Lib.Content.AbilityStore.definitions |> Map.find abilityId

            member _.asAMap = abilMap

            member _.asAList = abilList

            member _.asList =
              [
                for KeyValue(_, v) in Pomo.Lib.Content.AbilityStore.definitions ->
                  v
              ]
              |> FSharp.Data.Adaptive.IndexList.ofList
        }
      rng = fun () -> System.Random().NextDouble()
    }

  let getDerivedStats
    (state: GameState)
    : amap<int<EntityId>, Attributes.DerivedStats> =
    state.entities
    |> AMap.mapA(fun _ c ->
      applyModifiers state.services.effectStore c.BaseStats c.Effects)

  [<Struct>]
  type EntityChange = {
    components: All
    events: FSharp.Data.Adaptive.IndexList<GameEvent>
  }

  let tick (state: GameState) (time: int64<Tick>) : aval<StateChange> = adaptive {
    let! currentTime = state.gameTime
    let newTime = currentTime + time

    let! allEntityChanges =
      state.entities
      |> AMap.mapA(fun entityId components -> adaptive {
        let! updatedEffects, generatedEvents, tickResult =
          StatusEffects.tickEffects
            state.services.effectStore
            components.Effects
            time
            entityId

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

        return {
          components = updatedComponents
          events = generatedEvents
        }
      })
      |> AMap.toAVal


    let events =
      IndexList.ofList [
        for _, change in allEntityChanges do
          yield! change.events
      ]

    let entities =
      allEntityChanges |> HashMap.map(fun _ change -> change.components)


    return {
      entities = entities
      events = events
      gameTime = ValueSome newTime
    }
  }

  let applyTick (state: GameState) (change: StateChange) =
    transact(fun _ ->
      match change.gameTime with
      | ValueSome newTime -> state.gameTime.Value <- newTime
      | ValueNone -> ()

      state.gameEvents.AddRange change.events

      for entityId, updatedComponents in change.entities do
        state.entities[entityId] <- updatedComponents)

  let aAlive entities : aset<int<EntityId>> =
    entities
    |> AMap.toASet
    |> ASet.filter(fun (_, c) -> c.Resources.Status = Attributes.Status.Alive)
    |> ASet.map(fun (id, _) -> id)

  let aReadyAbilities
    entities
    gameTime
    : aset<(int<EntityId> * int<AbilityId>)> =
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
