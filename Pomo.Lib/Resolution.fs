namespace Pomo.Lib.Rules

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.GameEvent
open Pomo.Lib.Domain.State
open Pomo.Lib.Gameplay
open Pomo.Lib.Rules.Combat
open Pomo.Lib.Rules
open Pomo.Lib.Domain.Services

module Resolution =
  type ResolverParams = {
    entities: amap<int<EntityId>, All>
    derivedStats: amap<int<EntityId>, Attributes.DerivedStats>
    gameTime: cval<int64<Tick>>
    services: EngineServices
  }

  type ResolverActors = {
    actor: int<EntityId>
    target: int<EntityId>
  }

  /// Helper function to check if an actor is taunted and must target a specific entity
  let private checkTauntTarget
    (effectStore: IEffectStore)
    (actorEffects: alist<Effects.ActiveEffect>)
    =
    adaptive {
      let tauntEffects =
        actorEffects
        |> AList.filter(fun effect ->
          let effectDef = effectStore.tryFind effect.EffectId

          match effectDef with
          | ValueNone -> false
          | ValueSome effectDef -> effectDef.Kind = Effects.EffectKind.Taunt)

      let! isEmpty = AList.isEmpty tauntEffects

      if isEmpty then
        return ValueNone // No taunt, use intended target
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
        | Some taunt -> return ValueSome taunt.SourceId
        | None -> return ValueNone // Fallback, should not happen
    }


  type ResolverFn = ResolverParams * ResolverActors -> aval<StateChange>

  module ValidateAction =
    let checkStun (effectStore: IEffectStore) (actor: All) =
      actor.Effects
      |> AList.exists(fun e ->
        let def = effectStore.tryFind e.EffectId

        match def with
        | ValueNone -> false
        | ValueSome def -> def.Kind = Effects.EffectKind.Stun)

    let checkSilence
      (effectStore: IEffectStore)
      (actor: All)
      (abilityDef: Abilities.AbilityDefinition)
      =
      adaptive {
        let! hasSilence =
          actor.Effects
          |> AList.exists(fun e ->
            let def = effectStore.tryFind e.EffectId

            match def with
            | ValueNone -> false
            | ValueSome def -> def.Kind = Effects.EffectKind.Silence)

        let isSpellAbility =
          match abilityDef.Cost with
          | ValueSome cost -> cost.Type = Abilities.ResourceType.MP
          | ValueNone -> false

        return hasSilence && isSpellAbility
      }

    let checkCooldown
      (actor: All)
      (abilityId: int<AbilityId>)
      (gameTime: int64<Tick> aval)
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
      | ValueSome c ->
        let hasEnough =
          match c.Type with
          | Abilities.ResourceType.HP -> actor.Resources.HP >= c.Amount
          | Abilities.ResourceType.MP -> actor.Resources.MP >= c.Amount
          | Abilities.ResourceType.Stamina ->
            actor.Resources.Stamina >= c.Amount

        hasEnough, ValueSome c
      | ValueNone -> true, ValueNone

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
          let! forcedTargetId =
            checkTauntTarget rparams.services.effectStore actor.Effects

          match forcedTargetId with
          | ValueNone -> return ractors.target, initialTarget
          | ValueSome targetId ->
            let! newTarget = rparams.entities |> AMap.tryFind targetId

            return
              match newTarget with
              | Some t -> targetId, t
              | None -> ractors.target, initialTarget
      }

  module Shared =
    let private determineNewEffect
      (effectDef: Effects.EffectDefinition voption)
      (existingEffect: Effects.ActiveEffect option)
      (actorId: int<EntityId>)
      (effectId: int<EffectId>)
      =
      let stacking = effectDef |> ValueOption.map _.Stacking

      match existingEffect, stacking with
      | Some _, ValueSome Effects.StackingRule.NoStack -> ValueNone // Do not apply
      | Some e, ValueSome Effects.StackingRule.RefreshDuration ->
        let duration =
          match effectDef.Value.Duration with
          | Effects.Duration.Timed d -> d
          | Effects.Duration.Loop(_, d) -> d
          | _ -> 0L<Tick>

        let interval =
          match effectDef.Value.Duration with
          | Effects.Duration.Loop(i, _) -> i
          | _ -> 0L<Tick>

        ValueSome {
          e with
              RemainingTicks = duration
              NextTickIn = interval
        }
      | Some e, ValueSome(Effects.StackingRule.AddStack maxStacks) ->
        let newStacks = min maxStacks (e.Stacks + 1)

        let duration =
          match effectDef.Value.Duration with
          | Effects.Duration.Timed d -> d
          | Effects.Duration.Loop(_, d) -> d
          | _ -> 0L<Tick>

        let interval =
          match effectDef.Value.Duration with
          | Effects.Duration.Loop(i, _) -> i
          | _ -> 0L<Tick>

        ValueSome {
          e with
              Stacks = newStacks
              RemainingTicks = duration
              NextTickIn = interval
        }
      | _, ValueNone
      | None, _ ->
        let duration =
          effectDef
          |> ValueOption.map _.Duration.Duration
          |> ValueOption.flatten
          |> ValueOption.defaultValue 0L<Tick>

        let interval =
          effectDef
          |> ValueOption.map _.Duration.Interval
          |> ValueOption.flatten
          |> ValueOption.defaultValue 0L<Tick>

        ValueSome {
          EffectId = effectId
          SourceId = actorId
          RemainingTicks = duration
          NextTickIn = interval
          Stacks = 1
        }

    let private processEffect
      (effectStore: IEffectStore)
      (targetComponents: All)
      (actorId: int<EntityId>)
      (targetId: int<EntityId>)
      (effectId: int<EffectId>)
      =
      adaptive {
        let effectDef = effectStore.tryFind effectId

        let! existingEffect = adaptive {
          let! effects = targetComponents.Effects |> AList.toAVal

          return effects |> IndexList.tryFind(fun _ e -> e.EffectId = effectId)
        }

        let newEffect =
          determineNewEffect effectDef existingEffect actorId effectId

        let event =
          newEffect
          |> ValueOption.map(fun _ ->
            EffectApplied {
              target = targetId
              effectId = effectId
              source = actorId
            })

        return event, newEffect
      }

    let applyAbilityEffects
      (effectStore: IEffectStore)
      (abilityDef: Abilities.AbilityDefinition)
      (actorId: int<EntityId>)
      (targetId: int<EntityId>)
      (targetComponents: All)
      =
      let addNonRefreshingEffects
        (currentTargetEffects: IndexList<Effects.ActiveEffect>)
        (newEffectsMap: HashMap<_, Effects.ActiveEffect>)
        =
        let mutable map = newEffectsMap

        for e in currentTargetEffects do
          if not(HashMap.containsKey e.EffectId map) then
            map <- HashMap.add e.EffectId e map

        map

      adaptive {
        let! results =
          abilityDef.Effects
          |> AList.ofIndexList
          |> AList.mapA(
            processEffect effectStore targetComponents actorId targetId
          )
          |> AList.toAVal

        let effectEvents, effectsToApply =
          results
          |> IndexList.unzip
          |> (fun (e, ef) ->
            e |> IndexList.choose(id >> ValueOption.toOption),
            ef |> IndexList.choose(id >> ValueOption.toOption))


        let newEffectsMap =
          effectsToApply
          |> IndexList.map(fun e -> e.EffectId, e)
          |> HashMap.ofSeq

        let! finalTarget = adaptive {
          let! targetCurrentEffects = targetComponents.Effects |> AList.toAVal

          let updatedMap =
            newEffectsMap
            |> addNonRefreshingEffects targetCurrentEffects
            |> HashMap.toValueArray
            |> AList.ofArray

          return {
            targetComponents with
                Effects = updatedMap
          }
        }

        return effectEvents, finalTarget
      }

    let checkForDeath
      (newHp: int)
      (targetComponents: All)
      (targetId: int<EntityId>)
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
      (costOpt: Abilities.ResourceCost voption)
      (actorComponents: All)
      (actorId: int<EntityId>)
      =
      match costOpt with
      | ValueSome cost ->
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
      | ValueNone -> Array.empty, actorComponents

    let updateCooldowns
      (actorComponents: All)
      (abilityId: int<AbilityId>)
      (gameTime: int64<Tick>)
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
    let applyShields
      (effectStore: IEffectStore)
      (targetComponents: All)
      (damageResult: DamageResult)
      =
      adaptive {
        let shieldEffects =
          targetComponents.Effects
          |> AList.choose(fun effect ->
            let effectDef = effectStore.tryFind effect.EffectId

            match effectDef with
            | ValueNone -> None
            | ValueSome effectDef ->

            match effectDef.Kind with
            | Effects.EffectKind.Shield _ -> Some effect
            | _ -> None)

        let! totalShieldValue, shieldEffectsWithValue =
          shieldEffects
          |> AList.mapA(fun effect -> adaptive {
            let effectDef = effectStore.tryFind effect.EffectId

            match effectDef with
            | ValueNone -> return effect, 0
            | ValueSome effectDef ->

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


  /// A validation function that checks for the presence of actor and target, and the actor's status.
  let validateAction
    (rparams: ResolverParams)
    (ractors: ResolverActors)
    (abilityId: int<AbilityId>)
    =
    adaptive {
      let! actor = rparams.entities |> AMap.tryFind ractors.actor
      let! target = rparams.entities |> AMap.tryFind ractors.target
      let abilityDef = rparams.services.abilityStore.tryFind abilityId

      match abilityDef with
      | ValueNone -> return None // Invalid ability, cannot proceed
      | ValueSome abilityDef ->

      match actor, target with
      | Some actor, Some target when
        actor.Resources.Status = Attributes.Status.Alive
        ->
        let! isStunned =
          ValidateAction.checkStun rparams.services.effectStore actor

        let! isSilenced =
          ValidateAction.checkSilence
            rparams.services.effectStore
            actor
            abilityDef

        let! isOnCooldown =
          ValidateAction.checkCooldown actor abilityId rparams.gameTime

        let hasEnoughResource, cost =
          ValidateAction.checkResourceCost actor abilityDef

        if isStunned || isSilenced || isOnCooldown || not hasEnoughResource then
          return None
        else
          let! finalTarget = ValidateAction.resolveTaunt rparams ractors target

          return Some(actor, finalTarget, cost, abilityDef)
      | _ -> return None
    }

  /// Resolves a MeleeAttack command, calculating damage and generating events.
  let resolveMeleeAttack(abilityId: int<AbilityId>) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let { actor = actorId } = ractors
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None ->
        return {
          entities = HashMap.empty
          events = IndexList.empty
          gameTime = ValueNone
        }
      | Some(actorComponents, (targetId, targetComponents), costOpt, abilityDef) ->

        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime
        let rng = rparams.services.rng

        let damageResult =
          Combat.calculatePhysicalDamage actorStats targetStats rng

        let! updatedTarget, shieldDamage =
          MeleeAttack.applyShields
            rparams.services.effectStore
            targetComponents
            damageResult

        let actualDamage = max 0 (damageResult.Amount - shieldDamage)

        let damageEvent =
          DamageApplied {
            target = targetId
            amount = actualDamage
          }

        let newHp = max 0 (updatedTarget.Resources.HP - actualDamage)

        let updatedTargetAfterDamage = {
          updatedTarget with
              Resources.HP = newHp
        }

        let deathEvent, finalResources =
          Shared.checkForDeath newHp updatedTargetAfterDamage targetId

        let updatedTargetAfterDeathCheck = {
          updatedTargetAfterDamage with
              Resources = finalResources
        }

        let! effectEvents, finalTarget =
          Shared.applyAbilityEffects
            rparams.services.effectStore
            abilityDef
            actorId
            targetId
            updatedTargetAfterDeathCheck

        let costEvents, actorWithCost =
          Shared.applyResourceCost costOpt actorComponents actorId

        let finalActor =
          Shared.updateCooldowns actorWithCost abilityId gameTime abilityDef

        let changes =
          HashMap.ofList [ actorId, finalActor; targetId, finalTarget ]

        let allEvents =
          [|
            damageEvent
            yield! costEvents
            yield! effectEvents
            if deathEvent.IsSome then
              deathEvent.Value
          |]
          |> IndexList.ofArray

        return {
          entities = changes
          events = allEvents
          gameTime = ValueNone
        }
    }

  let resolveCastSpell(abilityId: int<AbilityId>) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let { actor = actorId } = ractors
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None ->
        return {
          entities = HashMap.empty
          events = IndexList.empty
          gameTime = ValueNone
        }
      | Some(actorComponents, (targetId, targetComponents), costOpt, abilityDef) ->
        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime
        let rng = rparams.services.rng

        let damageResult =
          Combat.calculateMagicalDamage
            Attributes.Element.Neutral
            actorStats.MA
            targetStats
            rng

        let initialDamage = damageResult.Amount

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

        let! effectEvents, finalTarget =
          Shared.applyAbilityEffects
            rparams.services.effectStore
            abilityDef
            actorId
            targetId
            updatedTarget

        let changes =
          HashMap.ofList [ actorId, updatedActor; targetId, finalTarget ]

        let allEvents =
          [|
            if damageEvent.IsSome then
              damageEvent.Value
            yield! costEvents
            yield! effectEvents
            if deathEvent.IsSome then
              deathEvent.Value
          |]
          |> IndexList.ofArray

        return {
          entities = changes
          events = allEvents
          gameTime = ValueNone
        }
    }

  let step (state: GameState) (cmd: Command) : aval<StateChange> =
    let derivedStats = GameState.getDerivedStats state

    let resolverParams = {
      entities = state.entities
      derivedStats = derivedStats
      gameTime = state.gameTime
      services = state.services
    }

    match cmd with
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

  let apply (state: GameState) (change: StateChange) =
    transact(fun _ ->
      state.gameEvents.AddRange change.events

      for id, components in change.entities do
        state.entities[id] <- components)
