namespace Pomo.Lib.Effects

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.AggregatedEffects

module StatusEffects =
  let applyEffect
    (targetEffects: ActiveEffect alist)
    (effectToApply: EffectDefinition)
    sourceId
    =

    adaptive {
      let! existing =
        targetEffects
        |> AList.choose(fun e ->
          if e.EffectId = effectToApply.Id then Some e else None)
        |> AList.tryFirst

      let! effects = targetEffects |> AList.toAVal

      match existing with
      | None ->
        // Effect not present, add it.
        let newEffect = {
          EffectId = effectToApply.Id
          SourceId = sourceId
          RemainingTicks =
            effectToApply.Duration.Ticks |> ValueOption.defaultValue 0L<Tick>
          NextTickIn =
            effectToApply.Duration.Interval |> ValueOption.defaultValue 0L<Tick>
          Stacks = 1
          Definition = effectToApply
        }

        return effects |> IndexList.add newEffect
      | Some _ ->
        return
          IndexList.map
            (fun e ->
              if e.EffectId <> effectToApply.Id then
                e // Keep other effects as they are
              else
                // This is the effect to update
                match effectToApply.Stacking with
                | NoStack -> e // Do nothing
                | RefreshDuration -> {
                    e with
                        RemainingTicks =
                          effectToApply.Duration.Ticks
                          |> ValueOption.defaultValue 0L<Tick>
                        NextTickIn =
                          effectToApply.Duration.Interval
                          |> ValueOption.defaultValue 0L<Tick>
                  }
                | AddStack maxStacks ->
                    {
                      e with
                          Stacks = min maxStacks (e.Stacks + 1)
                          RemainingTicks =
                            effectToApply.Duration.Ticks
                            |> ValueOption.defaultValue 0L<Tick>
                          NextTickIn =
                            effectToApply.Duration.Interval
                            |> ValueOption.defaultValue 0L<Tick>
                    })
            effects
    }
    |> AList.ofAVal


  let processAmountOverTime data =
    let struct (stacks, modifiers) = data

    let amount =
      modifiers
      |> Array.fold
        (fun acc modif ->
          match modif with
          | EffectModifier.StaticMod(StatModifier.Subtractive(HP, v)) ->
            acc + v
          | EffectModifier.StaticMod(StatModifier.Additive(HP, v)) -> acc + v
          | EffectModifier.StaticMod(StatModifier.Multiplicative(HP, v)) ->
            acc * int v
          | EffectModifier.StaticMod(StatModifier.Divisive(HP, v)) when v > 0 ->
            acc / int v
          | _ -> acc)
        0

    amount * stacks

  let tickEffects
    (effectStore: Services.IEffectStore)
    (activeEffects: HashMap<int<EffectId>, ActiveEffect>)
    ticksElapsed
    =
    let mutable updated = HashMap.empty
    let mutable totalDamage = 0
    let mutable totalHealing = 0

    for effectId, effect in activeEffects do
      let newRemainingTicks = effect.RemainingTicks - ticksElapsed
      let newNextTickIn = effect.NextTickIn - ticksElapsed
      let effectDef = effectStore.find effect.EffectId

      let isLoop, interval =
        match effectDef.Duration with
        | Loop(i, _) -> true, i
        | _ -> false, 0L<Tick>

      let shouldTick = isLoop && newNextTickIn <= 0L<Tick>

      let willExpire =
        match effectDef.Duration with
        | Loop _ -> newRemainingTicks <= 0L<Tick>
        | _ -> newRemainingTicks <= 0L<Tick>

      if shouldTick then
        match effectDef.Kind with
        | EffectKind.DamageOverTime ->
          totalDamage <-
            totalDamage
            + processAmountOverTime struct (effect.Stacks, effectDef.Modifiers)
        | EffectKind.HealOverTime ->
          totalHealing <-
            totalHealing
            + processAmountOverTime struct (effect.Stacks, effectDef.Modifiers)
        | _ -> ()

      if not willExpire then
        let nextTickIn =
          if shouldTick then
            interval + newNextTickIn
          else
            newNextTickIn

        let updatedEffect = {
          effect with
              RemainingTicks =
                if newRemainingTicks < 0L<Tick> then
                  0L<Tick>
                else
                  newRemainingTicks
              NextTickIn = nextTickIn
        }

        updated <- HashMap.add effectId updatedEffect updated

    struct (updated,
            {
              Damage = totalDamage
              Healing = totalHealing
            })
