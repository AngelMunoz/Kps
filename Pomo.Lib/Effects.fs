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

    let getDuration(d: Duration) =
      match d with
      | Timed t -> t
      | Instant -> 0L<Tick>
      | Loop(_, total) -> total

    let getInterval(d: Duration) =
      match d with
      | Loop(interval, _) -> interval
      | _ -> 0L<Tick>

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
          RemainingTicks = getDuration effectToApply.Duration
          NextTickIn = getInterval effectToApply.Duration
          Stacks = 1
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
                        RemainingTicks = getDuration effectToApply.Duration
                        NextTickIn = getInterval effectToApply.Duration
                  }
                | AddStack maxStacks ->
                    {
                      e with
                          Stacks = min maxStacks (e.Stacks + 1)
                          RemainingTicks = getDuration effectToApply.Duration
                          NextTickIn = getInterval effectToApply.Duration
                    })
            effects
    }
    |> AList.ofAVal

  let tickEffects
    (activeEffects: ActiveEffect alist)
    allEffects
    (ticksElapsed: int64<Tick>)
    (target: int<EntityId>)
    =
    adaptive {
      let! effects = activeEffects |> AList.toAVal
      let! effectDefs = allEffects |> AMap.toAVal

      let remaining, expired =
        effects
        |> IndexList.partition(fun effect ->
          let newRemaining = effect.RemainingTicks - ticksElapsed
          let effectDef = effectDefs[effect.EffectId]

          match effectDef.Duration with
          | Loop(_, _) ->
            // Periodic effects should be processed even when reaching 0 remaining time
            // to allow the final tick
            newRemaining >= 0L<Tick>
          | _ ->
            // Non-periodic effects expire when remaining time <= 0
            newRemaining > 0L<Tick>)

      // 2. Create expiration events for the expired effects.
      let expirationEvents =
        expired
        |> IndexList.map(fun effect ->
          GameEvent.EffectExpired {
            target = target
            effectId = effect.EffectId
          })

      // 3. Process the remaining effects to handle ticks and update timers.
      // We'll collect updated effects, new tick events, and aggregated results in one pass.
      let updatedRemaining, tickEvents, tickResult =
        remaining
        |> IndexList.fold
          (fun (accEffects, accEvents, accResult) effect ->

            let newRemainingTicks = effect.RemainingTicks - ticksElapsed
            let newNextTickIn = effect.NextTickIn - ticksElapsed
            let effectDef = effectDefs[effect.EffectId]

            let updatedEffect, newEvents, result =
              match effectDef.Duration with
              | Loop(interval, _) when newNextTickIn <= 0L<Tick> ->
                // This periodic effect should tick.
                let effectAppliedEvent =
                  GameEvent.EffectApplied {
                    source = effect.SourceId
                    target = target
                    effectId = effect.EffectId
                  }

                // Process DoT/HoT damage/healing based on effect kind
                let tickDamage, tickHealing, damageOrHealEvent =
                  match effectDef.Kind with
                  | EffectKind.DamageOverTime amount ->
                    // Apply damage per stack
                    let totalDamage = amount * effect.Stacks

                    let event =
                      Some(
                        GameEvent.DamageApplied {
                          target = target
                          amount = totalDamage
                        }
                      )

                    totalDamage, 0, event
                  | EffectKind.HealOverTime amount ->
                    // Apply healing per stack
                    let totalHealing = amount * effect.Stacks

                    let event =
                      Some(
                        GameEvent.Healed {
                          target = target
                          amount = totalHealing
                        }
                      )

                    0, totalHealing, event
                  | _ -> 0, 0, None

                // Check if effect will expire after this tick
                let willExpire = newRemainingTicks <= 0L<Tick>

                let updated =
                  if willExpire then
                    // Effect expires after this tick, create a dummy effect that will be filtered out
                    {
                      effect with
                          RemainingTicks = 0L<Tick>
                          NextTickIn = 0L<Tick>
                    }
                  else
                    {
                      effect with
                          RemainingTicks = newRemainingTicks
                          // Reset the tick timer, accounting for any "overdue" time.
                          NextTickIn = interval + newNextTickIn
                    }

                // Combine effect applied event with damage/heal event
                let events =
                  match damageOrHealEvent with
                  | Some dhe -> [ effectAppliedEvent; dhe ]
                  | None -> [ effectAppliedEvent ]

                updated,
                events,
                {
                  Damage = tickDamage
                  Healing = tickHealing
                }
              | _ ->
                // Not a periodic effect or not time to tick yet.
                let updated = {
                  effect with
                      RemainingTicks = newRemainingTicks
                      NextTickIn = newNextTickIn
                }

                updated, [], AggregatedEffects.empty

            let newAccEvents =
              newEvents
              |> List.fold (fun acc e -> IndexList.add e acc) accEvents

            let newAccResult = {
              Damage = accResult.Damage + result.Damage
              Healing = accResult.Healing + result.Healing
            }

            IndexList.add updatedEffect accEffects, newAccEvents, newAccResult)
          (IndexList.empty, IndexList.empty, AggregatedEffects.empty)


      // 4. Combine all events and return the final state.
      // The fold processes in reverse, so we reverse the results back.
      let finalEffects =
        updatedRemaining
        |> IndexList.rev
        |> IndexList.filter(fun effect -> effect.RemainingTicks > 0L<Tick>)

      let allEvents =
        IndexList.append expirationEvents (IndexList.rev tickEvents)

      return finalEffects, allEvents, tickResult
    }
