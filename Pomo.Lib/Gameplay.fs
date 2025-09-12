namespace Pomo.Lib.Gameplay

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.GameEvent
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Effects

type GameState = {
  entities: cmap<EntityId, All>
  gameEvents: clist<GameEvent>
  gameTime: cval<int64<ticks>>
  rng: cval<System.Random>
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

  let create() = {
    entities = cmap()
    gameEvents = clist []
    gameTime = cval 0L<ticks>
    rng = cval(System.Random 42)
  }

  let tick (state: GameState) (time: int64<ticks>) =
    let allEffects =
      Pomo.Lib.Content.EffectStore.definitions
      |> HashMap.ofMap
      |> AMap.ofHashMap

    transact(fun _ ->
      let currentTime = state.gameTime.Value
      let newTime = currentTime + time
      state.gameTime.Value <- newTime

      let allEntityChanges =
        state.entities
        |> AMap.mapA(fun entityId components -> adaptive {
          let! updatedEffects, generatedEvents =
            StatusEffects.tickEffects
              components.Effects
              allEffects
              time
              entityId

          let updatedComponents = {
            components with
                Effects = updatedEffects |> AList.ofIndexList
          }

          return (updatedComponents, generatedEvents)
        })

      let changesToApply = allEntityChanges |> AMap.force

      for entityId, (updatedComponents, events) in changesToApply do
        state.entities.[entityId] <- updatedComponents
        state.gameEvents.AddRange(events))

  let getDerivedStats
    (state: GameState)
    : amap<EntityId, Attributes.DerivedStats> =
    state.entities
    |> AMap.mapA(fun id c -> applyModifiers c.BaseStats c.Effects)

  let aAlive(state: GameState) : aset<EntityId> =
    state.entities
    |> AMap.filter(fun _ c -> c.Resources.Status = Attributes.Status.Alive)
    |> AMap.toASet
    |> ASet.map(fun (id, _) -> id)

  let aReadyAbilities
    (state: GameState)
    : aset<(EntityId * Abilities.AbilityId)> =
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
