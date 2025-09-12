namespace Pomo.Lib.Effects

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives
open Pomo.Lib.Domain.Effects

module StatusEffects =
  let applyEffect
    (targetEffects: ActiveEffect alist)
    (effectToApply: EffectDefinition)
    (sourceId: EntityId)
    =

    let getDuration(d: Duration) =
      match d with
      | Timed t -> t
      | Instant -> 0L<ticks>
      | Loop(_, total) -> total

    let getInterval(d: Duration) =
      match d with
      | Loop(interval, _) -> interval
      | _ -> 0L<ticks>

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
    (ticksElapsed: Ticks)
    (target: EntityId)
    =
    adaptive {
      let! effects = activeEffects |> AList.toAVal
      let! effectDefs = allEffects |> AMap.toAVal

      let remaining, expired =
        effects
        |> IndexList.partition(fun effect ->
          effect.RemainingTicks - ticksElapsed > 0L<ticks>)

      // 2. Create expiration events for the expired effects.
      let expirationEvents =
        expired
        |> IndexList.map(fun effect ->
          GameEvent.EffectExpired {
            target = target
            effectId = effect.EffectId
          })

      // 3. Process the remaining effects to handle ticks and update timers.
      // We'll collect updated effects and new tick events in one pass.
      let updatedRemaining, tickEvents =
        remaining
        |> IndexList.fold
          (fun (accEffects, accEvents) effect ->

            let newRemainingTicks = effect.RemainingTicks - ticksElapsed
            let newNextTickIn = effect.NextTickIn - ticksElapsed
            let effectDef = effectDefs[effect.EffectId]

            let updatedEffect, newEvent =
              match effectDef.Duration with
              | Loop(interval, _) when newNextTickIn <= 0L<ticks> ->
                // This periodic effect should tick.
                let event =
                  GameEvent.EffectApplied {
                    source = effect.SourceId
                    target = target
                    effectId = effect.EffectId
                  }

                let updated = {
                  effect with
                      RemainingTicks = newRemainingTicks
                      // Reset the tick timer, accounting for any "overdue" time.
                      NextTickIn = interval + newNextTickIn
                }

                updated, Some event
              | _ ->
                // Not a periodic effect or not time to tick yet.
                let updated = {
                  effect with
                      RemainingTicks = newRemainingTicks
                      NextTickIn = newNextTickIn
                }

                updated, None

            let newAccEvents =
              match newEvent with
              | Some e -> IndexList.add e accEvents
              | None -> accEvents

            IndexList.add updatedEffect accEffects, newAccEvents)
          (IndexList.empty, IndexList.empty)


      // 4. Combine all events and return the final state.
      // The fold processes in reverse, so we reverse the results back.
      let finalEffects = IndexList.rev updatedRemaining

      let allEvents =
        IndexList.append expirationEvents (IndexList.rev tickEvents)

      return finalEffects, allEvents
    }
