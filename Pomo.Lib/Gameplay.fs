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
  rng: unit -> float
}


module GameState =
  let private applyModifiers
    (baseStats: Attributes.BaseAttributes)
    (effects: alist<Effects.ActiveEffect>)
    : aval<Attributes.DerivedStats> =
    let modifiers =
      effects
      |> AList.collect(fun activeEffect ->
        (Pomo.Lib.Content.EffectStore.definitions.[activeEffect.EffectId])
          .Modifiers
        |> AList.ofList)


    let additiveModifiers =
      modifiers
      |> AList.choose (function
        | Effects.StatModifier.Additive(stat, value) -> Some(stat, value)
        | _ -> None)
      |> AList.groupBy fst
      |> AMap.map(fun _ values -> values |> IndexList.map snd |> IndexList.sum)

    adaptive {
      // 1. Apply base stat modifiers
      let! modifiedBase =
        additiveModifiers
        |> AMap.fold
          (fun acc stat value ->
            let currentBase = acc
            let v = value

            match stat with
            | Effects.Stat.Strength -> {
                currentBase with
                    Strength = currentBase.Strength + v
              }
            | Effects.Stat.Agility -> {
                currentBase with
                    Agility = currentBase.Agility + v
              }
            | Effects.Stat.Intellect -> {
                currentBase with
                    Intellect = currentBase.Intellect + v
              }
            | Effects.Stat.Vitality -> {
                currentBase with
                    Vitality = currentBase.Vitality + v
              }
            | Effects.Stat.Willpower -> {
                currentBase with
                    Willpower = currentBase.Willpower + v
              }
            | Effects.Stat.Luck -> {
                currentBase with
                    Luck = currentBase.Luck + v
              }
            | _ -> currentBase)
          baseStats

      // 2. Calculate initial derived stats from modified base stats
      let initialDerived = {
        MaxHP = modifiedBase.Vitality * 10
        MaxMP = modifiedBase.Willpower * 10
        AttackPower = modifiedBase.Strength * 2
        SpellPower = modifiedBase.Intellect * 2
        Armor = modifiedBase.Agility
        Evasion = float modifiedBase.Agility / 100.0
        CritChance = float modifiedBase.Luck / 100.0
        Resistances = Map.empty // Placeholder
      }

      // 3. Apply derived stat modifiers
      let! finalDerived =
        additiveModifiers
        |> AMap.fold
          (fun acc stat value ->
            let currentDerived = acc
            let v = value

            match stat with
            | Effects.Stat.MaxHP -> {
                currentDerived with
                    MaxHP = currentDerived.MaxHP + v
              }
            | Effects.Stat.MaxMP -> {
                currentDerived with
                    MaxMP = currentDerived.MaxMP + v
              }
            | Effects.Stat.AttackPower -> {
                currentDerived with
                    AttackPower = currentDerived.AttackPower + v
              }
            | Effects.Stat.SpellPower -> {
                currentDerived with
                    SpellPower = currentDerived.SpellPower + v
              }
            | Effects.Stat.Armor -> {
                currentDerived with
                    Armor = currentDerived.Armor + v
              }
            | _ -> currentDerived)
          initialDerived

      return finalDerived
    }

  let create'(rng: unit -> float) = {
    entities = cmap()
    gameEvents = clist []
    gameTime = cval 0L<Tick>
    rng = rng
  }

  let create() =
    create'(fun () -> System.Random.Shared.NextDouble())

  let getDerivedStats
    (state: GameState)
    : amap<int<EntityId>, Attributes.DerivedStats> =
    state.entities |> AMap.mapA(fun _ c -> applyModifiers c.BaseStats c.Effects)

  type EntityChange = {
    components: All
    events: GameEvent IndexList
  }

  let tick (state: GameState) (time: int64<Tick>) : aval<StateChange> =
    let allEffects =
      Pomo.Lib.Content.EffectStore.definitions
      |> HashMap.ofMap
      |> AMap.ofHashMap

    adaptive {
      let! currentTime = state.gameTime
      let newTime = currentTime + time

      let! allEntityChanges =
        state.entities
        |> AMap.mapA(fun entityId components -> adaptive {
          let! updatedEffects, generatedEvents, tickResult =
            StatusEffects.tickEffects
              components.Effects
              allEffects
              time
              entityId

          let! derivedStats = getDerivedStats state |> AMap.tryFind entityId

          let maxHp =
            match derivedStats with
            | Some stats -> stats.MaxHP
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

  let aAlive(state: GameState) : aset<int<EntityId>> =
    state.entities
    |> AMap.filter(fun _ c -> c.Resources.Status = Attributes.Status.Alive)
    |> AMap.toASet
    |> ASet.map(fun (id, _) -> id)

  let aReadyAbilities
    (state: GameState)
    : aset<(int<EntityId> * int<AbilityId>)> =
    let allCoolDowns =
      state.entities
      |> AMap.toASet
      |> ASet.collect(fun (id, c) ->
        c.AbilityCooldowns
        |> AMap.map(fun abilityId readyTick -> (id, abilityId, readyTick))
        |> AMap.toASet)

    allCoolDowns
    |> ASet.filterA(fun (_, (_, _, readyTick)) -> adaptive {
      let! gameTime = state.gameTime
      return readyTick <= gameTime
    })
    |> ASet.map(fun (_, (id, abilityId, _)) -> (id, abilityId))
