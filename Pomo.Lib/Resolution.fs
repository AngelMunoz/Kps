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
    rng: cval<System.Random>
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
        | None -> return initialTarget
        | Some actor ->
          let! forcedTargetId = checkTauntTarget actor.Effects ractors.target

          match forcedTargetId with
          | None -> return initialTarget
          | Some targetId ->
            if targetId <> ractors.target then
              let! newTarget = rparams.entities |> AMap.tryFind targetId

              return
                match newTarget with
                | Some t -> t
                | None -> initialTarget
            else
              return initialTarget
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
        }

        Some event, resources
      else
        None, targetComponents.Resources

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

          if effectDef.Kind = Effects.EffectKind.Shield then
            Some effect
          else
            None)

      let! totalShieldValue =
        shieldEffects |> AList.sumBy(fun effect -> effect.Stacks * 10)

      let shieldDamage =
        damageResult.Amount - (max 0 (damageResult.Amount - totalShieldValue))

      let updatedEffects =
        if shieldDamage > 0 then
          targetComponents.Effects
          |> AList.mapA(fun effect -> adaptive {
            let effectDef = EffectStore.definitions.[effect.EffectId]

            if effectDef.Kind = Effects.EffectKind.Shield then
              let damageToThisShield = min shieldDamage (effect.Stacks * 10)

              let stacksLost = (damageToThisShield + 9) / 10

              return {
                effect with
                    Stacks = max 0 (effect.Stacks - stacksLost)
              }
            else
              return effect
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
    let applyEffects
      (abilityDef: Abilities.AbilityDefinition)
      (actorId: EntityId)
      (targetId: EntityId)
      (gameTime: int64<ticks>)
      =
      abilityDef.Effects
      |> List.map(fun effectId ->
        let effectDef = EffectStore.definitions.[effectId]

        let duration =
          match effectDef.Duration with
          | Effects.Duration.Timed d -> d
          | _ -> 0L<ticks>

        let interval =
          match effectDef.Duration with
          | Effects.Duration.Loop(i, _) -> i
          | _ -> 0L<ticks>

        let activeEffect: Effects.ActiveEffect = {
          EffectId = effectId
          SourceId = actorId
          RemainingTicks = gameTime + duration
          NextTickIn = interval
          Stacks = 1
        }

        let event =
          EffectApplied {
            target = targetId
            effectId = effectId
            source = actorId
          }

        event, activeEffect)
      |> List.unzip

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
      let { actor = actorId; target = targetId } = ractors
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None -> return Array.empty, Map.empty
      | Some(actorComponents, targetComponents, costOpt, abilityDef) ->
        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime
        let! rng = rparams.rng

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
      let { actor = actorId; target = targetId } = ractors
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None -> return Array.empty, Map.empty
      | Some(actorComponents, targetComponents, costOpt, abilityDef) ->
        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime
        let! rng = rparams.rng

        let damageResult =
          Combat.calculateMagicalDamage
            Attributes.Element.Neutral
            actorStats.SpellPower
            targetStats
            rng

        let damageEvent =
          DamageApplied {
            target = targetId
            amount = damageResult.Amount
          }

        let costEvents, actorWithCost =
          Shared.applyResourceCost costOpt actorComponents actorId

        let updatedActor =
          Shared.updateCooldowns actorWithCost abilityId gameTime abilityDef

        let targetHpAfter =
          max 0 (targetComponents.Resources.HP - damageResult.Amount)

        let deathEvent, finalTargetResources =
          Shared.checkForDeath targetHpAfter targetComponents targetId

        let updatedTarget = {
          targetComponents with
              Resources = finalTargetResources
        }

        let effectEvents, effectsToApply =
          CastSpell.applyEffects abilityDef actorId targetId gameTime

        let finalTarget = {
          updatedTarget with
              Effects =
                updatedTarget.Effects
                |> AList.append(AList.ofList effectsToApply)
        }

        let changes =
          Map.ofList [ actorId, updatedActor; targetId, finalTarget ]

        let allEvents = [|
          damageEvent
          yield! costEvents
          yield! effectEvents
          match deathEvent with
          | Some ev -> ev
          | None -> ()
        |]

        return allEvents, changes
    }

  let private step
    (currentEntities: amap<EntityId, All>)
    (derivedStats: amap<EntityId, Attributes.DerivedStats>)
    (gameTime: cval<int64<ticks>>)
    (rng: cval<System.Random>)
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
