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
            effectToApply.Duration.Ticks
            |> ValueOption.defaultValue TimeSpan.Zero
          NextTickIn =
            effectToApply.Duration.Interval
            |> ValueOption.defaultValue TimeSpan.Zero
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
                          |> ValueOption.defaultValue TimeSpan.Zero
                        NextTickIn =
                          effectToApply.Duration.Interval
                          |> ValueOption.defaultValue TimeSpan.Zero
                  }
                | AddStack maxStacks ->
                    {
                      e with
                          Stacks = min maxStacks (e.Stacks + 1)
                          RemainingTicks =
                            effectToApply.Duration.Ticks
                            |> ValueOption.defaultValue TimeSpan.Zero
                          NextTickIn =
                            effectToApply.Duration.Interval
                            |> ValueOption.defaultValue TimeSpan.Zero
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

  let processResourceOverTime data : Attributes.Resources =
    let struct (stacks, modifiers, resources) = data

    let amount =
      modifiers
      |> Array.fold
        (fun (acc: Attributes.Resources) modif ->
          match modif with
          | EffectModifier.StaticMod(StatModifier.Subtractive(HP, v)) -> {
              acc with
                  HP = acc.HP + v
            }
          | EffectModifier.StaticMod(StatModifier.Additive(HP, v)) -> {
              acc with
                  HP = acc.HP + v
            }
          | EffectModifier.StaticMod(StatModifier.Multiplicative(HP, v)) -> {
              acc with
                  HP = acc.HP * int v
            }
          | EffectModifier.StaticMod(StatModifier.Divisive(HP, v)) when v > 0 -> {
              acc with
                  HP = acc.HP / int v
            }
          | EffectModifier.StaticMod(StatModifier.Subtractive(MP, v)) -> {
              acc with
                  MP = acc.MP + v
            }
          | EffectModifier.StaticMod(StatModifier.Additive(MP, v)) -> {
              acc with
                  MP = acc.MP + v
            }
          | EffectModifier.StaticMod(StatModifier.Multiplicative(MP, v)) -> {
              acc with
                  MP = acc.MP * int v
            }
          | EffectModifier.StaticMod(StatModifier.Divisive(MP, v)) when v > 0 -> {
              acc with
                  MP = acc.MP / int v
            }
          | _ -> acc)
        resources

    {
      HP = amount.HP * stacks
      MP = amount.MP * stacks
      Status = Attributes.Status.Alive
    }


  let tickEffects
    (effectStore: Services.IEffectStore)
    (activeEffects: HashMap<int<EffectId>, ActiveEffect>)
    ticksElapsed
    =
    let mutable updated = HashMap.empty
    let mutable totalDamage = 0

    let mutable totalResources: Attributes.Resources = {
      HP = 0
      MP = 0
      Status = Attributes.Status.Alive
    }

    for effectId, effect in activeEffects do
      let newRemainingTicks = effect.RemainingTicks - ticksElapsed
      let newNextTickIn = effect.NextTickIn - ticksElapsed
      let effectDef = effectStore.find effect.EffectId

      let isLoop, interval =
        match effectDef.Duration with
        | PermanentLoop i
        | Loop(i, _) -> true, i
        | Instant
        | Timed(_)
        | Permanent -> false, TimeSpan.Zero

      let shouldTick = isLoop && newNextTickIn <= TimeSpan.Zero

      let willExpire =
        match effectDef.Duration with
        | Loop _
        | Timed _
        | Instant -> newRemainingTicks <= TimeSpan.Zero
        | PermanentLoop _
        | Permanent -> false


      if shouldTick then
        match effectDef.Kind with
        | EffectKind.DamageOverTime ->
          totalDamage <-
            totalDamage
            + processAmountOverTime struct (effect.Stacks, effectDef.Modifiers)
        | EffectKind.ResourceOverTime ->
          totalResources <-
            processResourceOverTime
              struct (effect.Stacks, effectDef.Modifiers, totalResources)
        | EffectKind.Buff
        | EffectKind.Debuff
        | EffectKind.Stun
        | EffectKind.Silence
        | EffectKind.Taunt -> ()


      if not willExpire then
        let nextTickIn =
          if shouldTick then
            interval + newNextTickIn
          else
            newNextTickIn

        let updatedEffect = {
          effect with
              RemainingTicks =
                if newRemainingTicks < TimeSpan.Zero then
                  TimeSpan.Zero
                else
                  newRemainingTicks
              NextTickIn = nextTickIn
        }

        updated <- HashMap.add effectId updatedEffect updated

    struct (updated,
            {
              Damage = totalDamage
              Resources = totalResources
            })
