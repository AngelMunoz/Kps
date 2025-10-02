namespace Pomo.Lib.Effects

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.AggregatedEffects

module StatusEffects =
  let applyEffect
    (targetEffects: ActiveEffect alist)
    (effectToApply: EffectDefinition)
    (sourceId: int<EntityId>)
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
    activeEffects
    ticksElapsed
    =
    adaptive {
      let remaining =
        activeEffects
        |> AList.filter(fun effect ->
          let newRemaining = effect.RemainingTicks - ticksElapsed
          let effectDef = effectStore.find effect.EffectId

          match effectDef.Duration with
          | Loop _ ->
            // Periodic effects should be processed even when reaching 0 remaining time
            // to allow the final tick
            newRemaining >= 0L<Tick>
          | _ ->
            // Non-periodic effects expire when remaining time <= 0
            newRemaining > 0L<Tick>)
      // 2. Process the remaining effects to handle ticks and update timers.
      // We'll collect updated effects and aggregated results in one pass.
      let! struct (updatedRemaining, tickResult) =
        remaining
        |> AList.fold
          (fun acc effect ->
            let struct (accEffects, accResult) = acc
            let newRemainingTicks = effect.RemainingTicks - ticksElapsed
            let newNextTickIn = effect.NextTickIn - ticksElapsed
            let effectDef = effectStore.find effect.EffectId

            let updatedEffect, result =
              match effectDef.Duration with
              | Loop(interval, _) when newNextTickIn <= 0L<Tick> ->
                // This periodic effect should tick.

                // Process DoT/HoT damage/healing based on effect kind
                let amountOverTime =
                  match effectDef.Kind with
                  | EffectKind.DamageOverTime -> {
                      Damage =
                        processAmountOverTime
                          struct (effect.Stacks, effectDef.Modifiers)
                      Healing = 0
                    }
                  | EffectKind.HealOverTime -> {
                      Damage = 0
                      Healing =
                        processAmountOverTime
                          struct (effect.Stacks, effectDef.Modifiers)
                    }
                  | _ -> { Damage = 0; Healing = 0 }

                let updated =
                  let willExpire = newRemainingTicks <= 0L<Tick>

                  {
                    effect with
                        RemainingTicks =
                          if willExpire then 0L<Tick> else newRemainingTicks
                        NextTickIn =
                          if willExpire then
                            0L<Tick>
                          else
                            interval + newNextTickIn
                  }

                updated, amountOverTime
              | _ ->
                // Not a periodic effect or not time to tick yet.
                let updated = {
                  effect with
                      RemainingTicks = newRemainingTicks
                      NextTickIn = newNextTickIn
                }

                updated, TickResult.Zero

            (IndexList.add updatedEffect accEffects),
            {
              accResult with
                  Damage = accResult.Damage + result.Damage
                  Healing = accResult.Healing + result.Healing
            })
          (IndexList.empty, TickResult.Zero)


      // 4. Combine all events and return the final state.
      // The fold processes in reverse, so we reverse the results back.
      let finalEffects =
        updatedRemaining
        |> IndexList.filter(fun effect -> effect.RemainingTicks > 0L<Tick>)

      return struct (finalEffects, tickResult)
    }
