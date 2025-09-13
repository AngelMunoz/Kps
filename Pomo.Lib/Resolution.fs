namespace Pomo.Lib.Rules

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.GameEvent
open Pomo.Lib.Content
open Pomo.Lib.Gameplay
open Pomo.Lib.Rules.Combat

module Resolution =

  type ResolverParams = {
    entities: amap<EntityId, All>
    derivedStats: amap<EntityId, Attributes.DerivedStats>
    gameTime: cval<int64<ticks>>
    rng: unit -> float
  }

  type ResolverActors = { actor: EntityId; target: EntityId }

  /// Helper function to check if an actor is taunted and must target a specific entity
  let private checkTauntTarget
    (actorEffects: alist<Effects.ActiveEffect>)
    (intendedTarget: EntityId)
    =
    adaptive {
      let tauntEffects =
        actorEffects
        |> AList.filter(fun effect ->
          let effectDef =
            Pomo.Lib.Content.EffectStore.definitions.[effect.EffectId]

          effectDef.Kind = Effects.EffectKind.Taunt)

      let! isEmpty = AList.isEmpty tauntEffects

      if isEmpty then
        return None // No taunt, use intended target
      else
        // If taunted, must target the source of the most recent taunt effect
        let! mostRecentTaunt =
          tauntEffects
          |> AList.fold
            (fun acc effect ->
              match acc with
              | None -> Some effect
              | Some current ->
                if effect.RemainingTicks > current.RemainingTicks then
                  Some effect
                else
                  Some current)
            None

        match mostRecentTaunt with
        | Some taunt -> return Some taunt.SourceId
        | None -> return None // Fallback, should not happen
    }


  type ResolverFn =
    ResolverParams * ResolverActors -> aval<GameEvent[] * Map<EntityId, All>>

  module ValidateAction =
    let checkStun(actor: All) =
      actor.Effects
      |> AList.exists(fun e ->
        let def = EffectStore.definitions.[e.EffectId]
        def.Kind = Effects.EffectKind.Stun)

    let checkSilence (actor: All) (abilityDef: Abilities.AbilityDefinition) = adaptive {
      let! hasSilence =
        actor.Effects
        |> AList.exists(fun e ->
          let def = EffectStore.definitions.[e.EffectId]
          def.Kind = Effects.EffectKind.Silence)

      let isSpellAbility =
        match abilityDef.Cost with
        | Some cost -> cost.Type = Abilities.ResourceType.MP
        | None -> false

      return hasSilence && isSpellAbility
    }

    let checkCooldown
      (actor: All)
      (abilityId: Abilities.AbilityId)
      (gameTime: int64<ticks> aval)
      =
      adaptive {
        let! cooldowns = actor.AbilityCooldowns |> AMap.tryFind abilityId
        let! gameTime = gameTime

        return
          match cooldowns with
          | Some readyTime -> gameTime < readyTime
          | None -> false
      }

    let checkResourceCost
      (actor: All)
      (abilityDef: Abilities.AbilityDefinition)
      =
      match abilityDef.Cost with
      | Some c ->
        let hasEnough =
          match c.Type with
          | Abilities.ResourceType.HP -> actor.Resources.HP >= c.Amount
          | Abilities.ResourceType.MP -> actor.Resources.MP >= c.Amount
          | Abilities.ResourceType.Stamina ->
            actor.Resources.Stamina >= c.Amount

        hasEnough, Some c
      | None -> true, None

    let resolveTaunt
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (initialTarget: All)
      =
      adaptive {
        let! actor = rparams.entities |> AMap.tryFind ractors.actor

        match actor with
        | None -> return ractors.target, initialTarget
        | Some actor ->
          let! forcedTargetId = checkTauntTarget actor.Effects ractors.target

          match forcedTargetId with
          | None -> return ractors.target, initialTarget
          | Some targetId ->
            let! newTarget = rparams.entities |> AMap.tryFind targetId

            return
              match newTarget with
              | Some t -> targetId, t
              | None -> ractors.target, initialTarget
      }

  module Shared =
    let checkForDeath
      (newHp: int)
      (targetComponents: All)
      (targetId: EntityId)
      =
      if
        newHp <= 0
        && targetComponents.Resources.Status = Attributes.Status.Alive
      then
        let event = EntityDied { entityId = targetId }

        let resources = {
          targetComponents.Resources with
              Status = Attributes.Status.Dead
              HP = newHp
        }

        Some event, resources
      else
        None,
        {
          targetComponents.Resources with
              HP = newHp
        }

    let applyResourceCost
      (costOpt: option<Abilities.ResourceCost>)
      (actorComponents: All)
      (actorId: EntityId)
      =
      match costOpt with
      | Some cost ->
        let amount, updatedResources =
          match cost.Type with
          | Abilities.ResourceType.HP ->
            let newAmount = actorComponents.Resources.HP - cost.Amount

            newAmount,
            {
              actorComponents.Resources with
                  HP = newAmount
            }
          | Abilities.ResourceType.MP ->
            let newAmount = actorComponents.Resources.MP - cost.Amount

            newAmount,
            {
              actorComponents.Resources with
                  MP = newAmount
            }
          | Abilities.ResourceType.Stamina ->
            let newAmount = actorComponents.Resources.Stamina - cost.Amount

            newAmount,
            {
              actorComponents.Resources with
                  Stamina = newAmount
            }

        let ev =
          ResourceChanged {
            target = actorId
            resource = sprintf "%A" cost.Type
            newValue = amount
          }

        [| ev |],
        {
          actorComponents with
              Resources = updatedResources
        }
      | None -> Array.empty, actorComponents

    let updateCooldowns
      (actorComponents: All)
      (abilityId: Abilities.AbilityId)
      (gameTime: int64<ticks>)
      (abilityDef: Abilities.AbilityDefinition)
      =
      {
        actorComponents with
            AbilityCooldowns =
              actorComponents.AbilityCooldowns
              |> AMap.map(fun k v ->
                if k = abilityId then gameTime + abilityDef.Cooldown else v)
      }

  module MeleeAttack =
    let applyShields (targetComponents: All) (damageResult: DamageResult) = adaptive {
      let shieldEffects =
        targetComponents.Effects
        |> AList.choose(fun effect ->
          let effectDef = EffectStore.definitions.[effect.EffectId]

          match effectDef.Kind with
          | Effects.EffectKind.Shield _ -> Some effect
          | _ -> None)

      let! totalShieldValue, shieldEffectsWithValue =
        shieldEffects
        |> AList.mapA(fun effect -> adaptive {
          let effectDef = EffectStore.definitions.[effect.EffectId]

          let value =
            match effectDef.Kind with
            | Effects.EffectKind.Shield v -> v
            | _ -> 0 // Should not happen

          return effect, value
        })
        |> AList.fold
          (fun (total, effects) (effect, value) ->
            (total + effect.Stacks * value, (effect, value) :: effects))
          (0, [])

      let shieldDamage =
        damageResult.Amount - max 0 (damageResult.Amount - totalShieldValue)

      let updatedEffects =
        if shieldDamage > 0 then
          let _, updated =
            shieldEffectsWithValue
            |> List.fold
              (fun (remainingDamage, acc) (effect, value) ->
                if remainingDamage <= 0 then
                  (0, effect :: acc)
                else
                  let damageToThisShield =
                    min remainingDamage (effect.Stacks * value)

                  let stacksLost = (damageToThisShield + value - 1) / value

                  let updatedEffect = {
                    effect with
                        Stacks = max 0 (effect.Stacks - stacksLost)
                  }

                  (remainingDamage - damageToThisShield, updatedEffect :: acc))
              (shieldDamage, [])

          targetComponents.Effects
          |> AList.mapA(fun e -> adaptive {
            match
              updated |> List.tryFind(fun ue -> ue.EffectId = e.EffectId)
            with
            | Some ue -> return ue
            | None -> return e
          })
          |> AList.choose(fun e -> if e.Stacks > 0 then Some e else None)
        else
          targetComponents.Effects

      return
        {
          targetComponents with
              Effects = updatedEffects
        },
        shieldDamage
    }

  module CastSpell =
    let private determineNewEffect
      (effectDef: Effects.EffectDefinition)
      (existingEffect: option<Effects.ActiveEffect>)
      (gameTime: int64<ticks>)
      (actorId: EntityId)
      (effectId: Effects.EffectId)
      =
      match existingEffect, effectDef.Stacking with
      | Some _, Effects.StackingRule.NoStack -> None // Do not apply
      | Some e, Effects.StackingRule.RefreshDuration ->
        let duration =
          match effectDef.Duration with
          | Effects.Duration.Timed d -> d
          | Effects.Duration.Loop(_, d) -> d
          | _ -> 0L<ticks>

        let interval =
          match effectDef.Duration with
          | Effects.Duration.Loop(i, _) -> i
          | _ -> 0L<ticks>

        Some {
          e with
              RemainingTicks = duration
              NextTickIn = interval
        }
      | Some e, Effects.StackingRule.AddStack maxStacks ->
        let newStacks = min maxStacks (e.Stacks + 1)

        let duration =
          match effectDef.Duration with
          | Effects.Duration.Timed d -> d
          | Effects.Duration.Loop(_, d) -> d
          | _ -> 0L<ticks>

        let interval =
          match effectDef.Duration with
          | Effects.Duration.Loop(i, _) -> i
          | _ -> 0L<ticks>

        Some {
          e with
              Stacks = newStacks
              RemainingTicks = duration
              NextTickIn = interval
        }
      | None, _ ->
        let duration =
          match effectDef.Duration with
          | Effects.Duration.Timed d -> d
          | Effects.Duration.Loop(_, d) -> d
          | _ -> 0L<ticks>

        let interval =
          match effectDef.Duration with
          | Effects.Duration.Loop(i, _) -> i
          | _ -> 0L<ticks>

        Some {
          EffectId = effectId
          SourceId = actorId
          RemainingTicks = duration
          NextTickIn = interval
          Stacks = 1
        }

    let private processEffect
      (targetComponents: All)
      (gameTime: int64<ticks>)
      (actorId: EntityId)
      (targetId: EntityId)
      (effectId: Effects.EffectId)
      =
      adaptive {
        let effectDef = EffectStore.definitions[effectId]

        let! existingEffect = adaptive {
          let! effects = targetComponents.Effects |> AList.toAVal

          return effects |> IndexList.tryFind(fun _ e -> e.EffectId = effectId)
        }

        let newEffect =
          determineNewEffect effectDef existingEffect gameTime actorId effectId

        let event =
          newEffect
          |> Option.map(fun _ ->
            EffectApplied {
              target = targetId
              effectId = effectId
              source = actorId
            })

        return event, newEffect
      }

    let applyEffects
      (abilityDef: Abilities.AbilityDefinition)
      (actorId: EntityId)
      (targetId: EntityId)
      (gameTime: int64<ticks>)
      (targetComponents: All)
      =
      adaptive {
        let! results =
          abilityDef.Effects
          |> AList.ofList
          |> AList.mapA(
            processEffect targetComponents gameTime actorId targetId
          )
          |> AList.toAVal

        let events, effects =
          results
          |> IndexList.unzip
          |> (fun (e, ef) ->
            e |> IndexList.choose id, ef |> IndexList.choose id)

        return events, effects
      }

  /// A validation function that checks for the presence of actor and target, and the actor's status.
  let validateAction
    (rparams: ResolverParams)
    (ractors: ResolverActors)
    (abilityId: Abilities.AbilityId)
    =
    adaptive {
      let! actor = rparams.entities |> AMap.tryFind ractors.actor
      let! target = rparams.entities |> AMap.tryFind ractors.target
      let abilityDef = AbilityStore.definitions[abilityId]

      match actor, target with
      | Some actor, Some target when
        actor.Resources.Status = Attributes.Status.Alive
        ->
        let! isStunned = ValidateAction.checkStun actor

        if isStunned then
          return None
        else
          let! isSilenced = ValidateAction.checkSilence actor abilityDef

          if isSilenced then
            return None
          else
            let! isOnCooldown =
              ValidateAction.checkCooldown actor abilityId rparams.gameTime

            if isOnCooldown then
              return None
            else
              let hasEnoughResource, cost =
                ValidateAction.checkResourceCost actor abilityDef

              if not hasEnoughResource then
                return None
              else
                let! finalTarget =
                  ValidateAction.resolveTaunt rparams ractors target

                return Some(actor, finalTarget, cost, abilityDef)
      | _ -> return None
    }

  /// Resolves a MeleeAttack command, calculating damage and generating events.
  let private resolveMeleeAttack(abilityId: Abilities.AbilityId) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let { actor = actorId } = ractors
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None -> return Array.empty, Map.empty
      | Some(actorComponents, (targetId, targetComponents), costOpt, abilityDef) ->

        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime
        let rng = rparams.rng

        let damageResult =
          Combat.calculatePhysicalDamage actorStats targetStats rng

        let! updatedTarget, shieldDamage =
          MeleeAttack.applyShields targetComponents damageResult

        let actualDamage = max 0 (damageResult.Amount - shieldDamage)

        let damageEvent =
          DamageApplied {
            target = targetId
            amount = actualDamage
          }

        let newHp = max 0 (updatedTarget.Resources.HP - actualDamage)

        let updatedTarget = {
          updatedTarget with
              Resources.HP = newHp
        }

        let deathEvent, finalResources =
          Shared.checkForDeath newHp updatedTarget targetId

        let finalTarget = {
          updatedTarget with
              Resources = finalResources
        }

        let costEvents, actorWithCost =
          Shared.applyResourceCost costOpt actorComponents actorId

        let finalActor =
          Shared.updateCooldowns actorWithCost abilityId gameTime abilityDef

        let changes = Map.ofList [ actorId, finalActor; targetId, finalTarget ]

        let events = [|
          damageEvent
          yield! costEvents
          match deathEvent with
          | Some ev -> ev
          | None -> ()
        |]

        return events, changes
    }

  let private resolveCastSpell(abilityId: Abilities.AbilityId) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let { actor = actorId } = ractors
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None -> return Array.empty, Map.empty
      | Some(actorComponents, (targetId, targetComponents), costOpt, abilityDef) ->
        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime
        let rng = rparams.rng

        // Spells can either do direct damage, apply effects, or both.
        // We'll calculate damage and then decide whether to apply it.
        let damageResult =
          Combat.calculateMagicalDamage
            Attributes.Element.Neutral
            actorStats.SpellPower
            targetStats
            rng

        // Spells should apply direct damage unless they only have periodic effects (DoT/HoT)
        let shouldApplyDirectDamage =
          if abilityDef.Effects.IsEmpty then
            true // No effects, assume direct damage spell
          else
            // Check if all effects are periodic (DoT/HoT) - if so, no direct damage
            not(
              abilityDef.Effects
              |> List.forall(fun effectId ->
                let effectDef = EffectStore.definitions[effectId]

                match effectDef.Kind with
                | Effects.EffectKind.DamageOverTime _ -> true
                | Effects.EffectKind.HealOverTime _ -> true
                | _ -> false)
            )

        let initialDamage =
          if shouldApplyDirectDamage then damageResult.Amount else 0

        let damageEvent =
          if initialDamage > 0 then
            Some(
              DamageApplied {
                target = targetId
                amount = initialDamage
              }
            )
          else
            None

        let costEvents, actorWithCost =
          Shared.applyResourceCost costOpt actorComponents actorId

        let updatedActor =
          Shared.updateCooldowns actorWithCost abilityId gameTime abilityDef

        let targetHpAfter =
          max 0 (targetComponents.Resources.HP - initialDamage)

        let deathEvent, finalTargetResources =
          Shared.checkForDeath targetHpAfter targetComponents targetId

        let updatedTarget = {
          targetComponents with
              Resources = {
                finalTargetResources with
                    HP = targetHpAfter
              }
        }

        let! effectEvents, effectsToApply =
          CastSpell.applyEffects
            abilityDef
            actorId
            targetId
            gameTime
            targetComponents

        let finalTarget =
          let newEffectMap =
            effectsToApply |> IndexList.map(fun e -> e.EffectId, e) |> Map.ofSeq

          let existingIds =
            updatedTarget.Effects
            |> AList.force
            |> Seq.map(fun e -> e.EffectId)
            |> Set.ofSeq

          let updatedEffects =
            updatedTarget.Effects
            |> AList.map(fun e ->
              match newEffectMap.TryFind e.EffectId with
              | Some ne -> ne
              | None -> e)

          let effectsToAdd =
            newEffectMap
            |> Map.filter(fun id _ -> not(existingIds.Contains id))
            |> Map.toList
            |> List.map snd

          {
            updatedTarget with
                Effects =
                  updatedEffects |> AList.append(AList.ofList effectsToAdd)
          }

        let changes =
          Map.ofList [ actorId, updatedActor; targetId, finalTarget ]

        let allEvents = [|
          if damageEvent.IsSome then
            yield damageEvent.Value
          yield! costEvents
          yield! effectEvents
          if deathEvent.IsSome then
            yield deathEvent.Value
        |]

        return allEvents, changes
    }

  let private step
    (currentEntities: amap<EntityId, All>)
    (derivedStats: amap<EntityId, Attributes.DerivedStats>)
    (gameTime: cval<int64<ticks>>)
    (rng: unit -> float)
    (command: Command)
    : aval<GameEvent[] * Map<EntityId, All>> =
    let resolverParams = {
      entities = currentEntities
      derivedStats = derivedStats
      gameTime = gameTime
      rng = rng
    }

    match command with
    | MeleeAttack action ->
      resolveMeleeAttack
        action.abilityId
        (resolverParams,
         {
           actor = action.actor
           target = action.target
         })
    | CastSpell action ->
      resolveCastSpell
        action.abilityId
        (resolverParams,
         {
           actor = action.actor
           target = action.target
         })

  let apply (state: GameState) (cmd: Command) =
    let derivedStats = GameState.getDerivedStats state

    let events, changes =
      step state.entities derivedStats state.gameTime state.rng cmd
      |> AVal.force

    transact(fun _ ->
      state.gameEvents.AddRange events

      for change in changes do
        state.entities.[change.Key] <- change.Value)
