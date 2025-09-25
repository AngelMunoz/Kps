namespace Pomo.Lib.Rules

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.GameEvent
open Pomo.Lib.Domain.State
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Services
open Pomo.Lib.Domain.Abilities

module Resolution =
  [<Struct>]
  type DamageResult = {
    Amount: int
    IsCritical: bool
    IsEvaded: bool
  }

  [<Struct>]
  type HookContext = {
    InvokerStats: DerivedStats
    TargetStats: DerivedStats
    AbilityId: int<AbilityId>
    GameTime: int64<Tick>
    DamageResult: DamageResult voption
  }

  [<Struct>]
  type ResourceTypeExchange = {
    From: ResourceType
    To: ResourceType
    Ratio: float
  }

  [<Struct>]
  type ResourceChange =
    | Additive of addition: struct (ResourceType * int)
    | Exchange of ResourceTypeExchange
    | SetTo of replace: struct (ResourceType * int)

  [<Struct>]
  type HookResult = {
    DamageModification: int
    ResourceChanges: ResourceChange list
    ShieldGeneration: int
  }

  let processEffectHooks
    (hook: Effects.EffectHook)
    (context: HookContext)
    (entityEffects: alist<Effects.ActiveEffect>)
    (services: EngineServices)
    =
    entityEffects
    |> AList.fold
      (fun acc effect ->
        if
          effect.Definition.Hooks |> IndexList.exists(fun _ h -> h = hook)
        then
          let modifications =
            effect.Definition.Modifiers
            |> IndexList.fold
              (fun acc modifier ->
                match modifier with
                | Effects.AbilityDamageMod multiplier ->
                  match context.DamageResult with
                  | ValueSome damage -> {
                      acc with
                          DamageModification =
                            acc.DamageModification
                            + damage.Amount * int multiplier
                    }
                  | ValueNone -> acc
                | Effects.ResourceConversion(fromType, toType, ratio) -> {
                    acc with
                        ResourceChanges =
                          Exchange {
                            From = fromType
                            To = toType
                            Ratio = ratio
                          }
                          :: acc.ResourceChanges
                  }
                | Effects.ShieldGeneration formulaId ->
                  match services.formulaStore.tryFind formulaId with
                  | ValueSome formula ->
                    let context = {
                      InvokerStats = context.InvokerStats
                      InvokerElementalAttributes =
                        context.InvokerStats.ElementAttributes
                      TargetElementalResistances =
                        context.TargetStats.ElementResistances
                    }

                    let formulaResult = formula.Calculate context

                    {
                      acc with
                          ShieldGeneration =
                            // TODO: Come up with the actual formula for shield generation
                            // requires to re-define how formulas are structured
                            acc.ShieldGeneration + formulaResult.BaseDamage
                    }
                  | ValueNone -> acc
                | _ -> acc)
              {
                DamageModification = 0
                ResourceChanges = []
                ShieldGeneration = 0
              }

          {
            acc with
                DamageModification =
                  acc.DamageModification + modifications.DamageModification
                ResourceChanges =
                  acc.ResourceChanges @ modifications.ResourceChanges
                ShieldGeneration =
                  acc.ShieldGeneration + modifications.ShieldGeneration
          }
        else
          acc)
      {
        DamageModification = 0
        ResourceChanges = []
        ShieldGeneration = 0
      }

  let calculateDamage
    (formulaStore: IFormulaStore)
    (formulaId: int<FormulaId>)
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (rng: unit -> float)
    =

    match formulaStore.tryFind formulaId with
    | ValueSome formula ->
      let context = {
        InvokerStats = attackerStats
        InvokerElementalAttributes = attackerStats.ElementAttributes
        TargetElementalResistances = defenderStats.ElementResistances
      }

      let formulaResult = formula.Calculate context

      // STEP 1: Hit/Miss calculation based on damage type
      let hitRoll = rng()

      let hitChance =
        match formulaResult.DamageType with
        | DamageType.Physical ->
          // AC vs HV for physical attacks
          float attackerStats.AC
          / (float attackerStats.AC + float defenderStats.HV)
        | DamageType.Magical ->
          // LK vs LK for magical/elemental attacks
          float attackerStats.LK
          / (float attackerStats.LK + float defenderStats.LK)

      let isHit = hitRoll < hitChance

      if not isHit then
        {
          Amount = 0
          IsCritical = false
          IsEvaded = true
        }
      else
        // STEP 2-4: Calculate damage (includes base damage, modifiers, and final damage)
        // Apply critical hit (uses LK)
        let critRoll = rng()
        let isCritical = critRoll < float attackerStats.LK * 0.01

        let damageMultiplier =
          if isCritical then
            float(formulaResult.BaseDamage + formulaResult.ElementalDamage)
            * 0.10
          else
            0

        let finalElementalDamage =
          if formulaResult.ElementalDamage > 0 then
            let elementRes =
              defenderStats.ElementResistances.TryFindV formulaResult.Element
              |> ValueOption.defaultValue 0.0

            float formulaResult.ElementalDamage * (1.0 - elementRes) |> int
          else
            int 0.0

        let totalDamage = formulaResult.BaseDamage + finalElementalDamage

        let finalDamage = float totalDamage + damageMultiplier

        {
          Amount = int finalDamage
          IsCritical = isCritical
          IsEvaded = false
        }
    | ValueNone ->
        {
          Amount = 0
          IsCritical = false
          IsEvaded = false
        }

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
      (abilityDef: Abilities.ActiveAbilityDefinition)
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
          | ValueSome c when c.Type = ResourceType.MP -> true
          | _ -> false

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
      (abilityDef: Abilities.ActiveAbilityDefinition)
      =
      match abilityDef.Cost with
      | ValueSome c ->
        let hasEnough =
          match c.Type with
          | ResourceType.HP -> actor.Resources.HP >= c.Amount
          | ResourceType.MP -> actor.Resources.MP >= c.Amount

        struct (hasEnough, ValueSome c)
      | ValueNone -> struct (true, ValueNone)

    let checkAbilityRequirements
      (actor: All)
      (actorStats: Attributes.DerivedStats)
      (requirements: IndexList<AbilityRequirement>)
      (services: EngineServices)
      =
      adaptive {
        let! hasAllRequirements =
          requirements
          |> AList.ofIndexList
          |> AList.forallA(fun req ->
            match req with
            | StatRequirement(stat, minValue) ->
              let actualValue =
                match stat with
                | Power -> actorStats.AP
                | Magic -> actorStats.MA
                | Sense -> actorStats.DA
                | Charm -> actorStats.HP
                | AP -> actorStats.AP
                | AC -> actorStats.AC
                | DX -> actorStats.DX
                | MP -> actorStats.MP
                | MA -> actorStats.MA
                | MD -> actorStats.MD
                | WT -> actorStats.WT
                | DA -> actorStats.DA
                | LK -> actorStats.LK
                | HP -> actorStats.HP
                | DP -> actorStats.DP
                | HV -> actorStats.HV

              AVal.constant(actualValue >= minValue)
            | AbilityRequirement abilityId ->
              actor.Abilities |> AList.exists(fun a -> a = abilityId)
            | FormulaRequirement formulaId ->
              // For now, return true - formula validation would be implemented later
              AVal.constant true)

        return hasAllRequirements
      }

    let resolveTaunt
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (initialTarget: All)
      =
      adaptive {
        let! actor = rparams.entities |> AMap.tryFind ractors.actor

        match actor with
        | None -> return struct (ractors.target, initialTarget)
        | Some actor ->
          let! forcedTargetId =
            checkTauntTarget rparams.services.effectStore actor.Effects

          match forcedTargetId with
          | ValueNone -> return struct (ractors.target, initialTarget)
          | ValueSome targetId ->
            let! newTarget = rparams.entities |> AMap.tryFind targetId

            return
              match newTarget with
              | Some t -> struct (targetId, t)
              | None -> struct (ractors.target, initialTarget)
      }

  module Shared =
    let private getDurationTicks(duration: Effects.Duration) =
      match duration with
      | Effects.Instant
      | Effects.Permanent -> 0L<Tick> // Permanent effects don't tick down
      | Effects.Timed ticks -> ticks
      | Effects.Loop(_, totalDuration) -> totalDuration

    let private getDurationInterval(duration: Effects.Duration) =
      match duration with
      | Effects.Instant -> 0L<Tick>
      | Effects.Timed _ -> 0L<Tick>
      | Effects.Loop(interval, _) -> interval
      | Effects.Permanent -> 0L<Tick>

    let private determineNewEffect
      (effectDef: Effects.EffectDefinition)
      (existingEffect: Effects.ActiveEffect)
      =
      let stacking = effectDef.Stacking

      match stacking with
      | Effects.NoStack -> ValueNone
      | Effects.RefreshDuration ->
        ValueSome {
          existingEffect with
              RemainingTicks = getDurationTicks effectDef.Duration
              NextTickIn = getDurationInterval effectDef.Duration
        }
      | Effects.AddStack maxStacks ->
        let newStacks = min maxStacks (existingEffect.Stacks + 1)

        ValueSome {
          existingEffect with
              Stacks = newStacks
              RemainingTicks = getDurationTicks effectDef.Duration
              NextTickIn = getDurationInterval effectDef.Duration
        }



    let private processEffect
      (effectDef: Effects.EffectDefinition)
      (targetComponents: All)
      (actorId: int<EntityId>)
      (targetId: int<EntityId>)
      =
      adaptive {
        let! existingEffect = adaptive {
          let! effects = targetComponents.Effects |> AList.toAVal

          return
            effects |> IndexList.tryFind(fun _ e -> e.EffectId = effectDef.Id)
        }

        let newEffect =
          match existingEffect with
          | Some e -> determineNewEffect effectDef e
          | None ->
            ValueSome {
              EffectId = effectDef.Id
              SourceId = actorId
              RemainingTicks = getDurationTicks effectDef.Duration
              NextTickIn = getDurationInterval effectDef.Duration
              Stacks = 1
              Definition = effectDef
            }

        let event =
          newEffect
          |> ValueOption.map(fun _ ->
            EffectApplied {
              target = targetId
              effectId = effectDef.Id
              source = actorId
            })

        return event, newEffect
      }

    let applyAbilityEffects
      (effectStore: IEffectStore)
      (abilityDef: Abilities.ActiveAbilityDefinition)
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
          |> AList.mapA(fun effectId -> adaptive {
            let effect = effectStore.tryFind effectId

            match effect with
            | ValueNone -> return ValueNone, ValueNone
            | ValueSome effect ->
              return! processEffect effect targetComponents actorId targetId
          })
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

        return struct (effectEvents, finalTarget)
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
              Status = Dead
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
          | ResourceType.HP ->
            let newAmount = actorComponents.Resources.HP - cost.Amount

            newAmount,
            {
              actorComponents.Resources with
                  HP = newAmount
            }
          | ResourceType.MP ->
            let newAmount = actorComponents.Resources.MP - cost.Amount

            newAmount,
            {
              actorComponents.Resources with
                  MP = newAmount
            }

        let resourceString =
          match cost.Type with
          | ResourceType.HP -> "HP"
          | ResourceType.MP -> "MP"

        let ev =
          ResourceChanged {
            target = actorId
            resource = resourceString
            newValue = amount
          }

        struct (ValueSome ev,
                {
                  actorComponents with
                      Resources = updatedResources
                })
      | ValueNone -> struct (ValueNone, actorComponents)

    let updateCooldowns
      (actorComponents: All)
      (abilityId: int<AbilityId>)
      (gameTime: int64<Tick>)
      (abilityDef: Abilities.ActiveAbilityDefinition)
      =
      {
        actorComponents with
            AbilityCooldowns =
              actorComponents.AbilityCooldowns
              |> AMap.map(fun k v ->
                if k = abilityId then gameTime + abilityDef.Cooldown else v)
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
      let abilityKind = rparams.services.abilityStore.tryFind abilityId

      match abilityKind with
      | ValueNone -> return ValueNone // Invalid ability, cannot proceed
      | ValueSome(Abilities.Passive _) -> return ValueNone // Passive abilities cannot be invoked
      | ValueSome(Abilities.Active abilityDef) ->

      match actor, target with
      | Some actor, Some target when actor.Resources.Status = Alive ->
        let! actorStats = rparams.derivedStats |> AMap.find ractors.actor

        let! isStunned =
          ValidateAction.checkStun rparams.services.effectStore actor

        let! isSilenced =
          ValidateAction.checkSilence
            rparams.services.effectStore
            actor
            abilityDef

        let! isOnCooldown =
          ValidateAction.checkCooldown actor abilityId rparams.gameTime

        let struct (hasEnoughResource, cost) =
          ValidateAction.checkResourceCost actor abilityDef

        let! hasRequirements =
          ValidateAction.checkAbilityRequirements
            actor
            actorStats
            abilityDef.Requirements
            rparams.services

        if
          isStunned
          || isSilenced
          || isOnCooldown
          || not hasEnoughResource
          || not hasRequirements
        then
          return ValueNone
        else
          let! finalTarget = ValidateAction.resolveTaunt rparams ractors target

          return ValueSome struct (actor, finalTarget, cost, abilityDef)
      | _ -> return ValueNone
    }

  /// Resolves an ability command, calculating damage and generating events.
  let resolveAbility(abilityId: int<AbilityId>) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let { actor = actorId } = ractors
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | ValueNone ->
        // Determine why the action was blocked
        let! actor = rparams.entities |> AMap.tryFind ractors.actor
        let abilityDef = rparams.services.abilityStore.tryFind abilityId

        let! blockingEffect =
          match actor, abilityDef with
          | Some actor, ValueSome(Active abilityDef) -> adaptive {
              let! isStunned =
                ValidateAction.checkStun rparams.services.effectStore actor

              let! isSilenced =
                ValidateAction.checkSilence
                  rparams.services.effectStore
                  actor
                  abilityDef

              if isStunned then
                return ValueSome Effects.EffectKind.Stun
              elif isSilenced then
                return ValueSome Effects.EffectKind.Silence
              else
                return ValueNone
            }
          | _ -> AVal.constant ValueNone

        let realizationEvent =
          match blockingEffect with
          | ValueNone -> []
          | ValueSome blockingEffect ->
              [
                EffectRealization {
                  actor = actorId
                  targets = IndexList.single ractors.target
                  abilityId = abilityId
                  RealizedEffect = blockingEffect
                }
              ]

        return {
          entities = HashMap.empty
          events = IndexList.ofList realizationEvent
          gameTime = ValueNone
        }
      | ValueSome struct (actorComponents, struct (targetId, targetComponents),
                          costOpt, abilityDef) ->

        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime
        let rng = rparams.services.rng

        // Process OnAbilityInvoke hooks before damage calculation
        let hookContext = {
          InvokerStats = actorStats
          TargetStats = targetStats
          AbilityId = abilityId
          GameTime = gameTime
          DamageResult = ValueNone
        }

        let! invokeHookResults =
          processEffectHooks
            Effects.OnAbilityInvoke
            hookContext
            actorComponents.Effects
            rparams.services

        let baseDamageResult =
          match abilityDef.FormulaId with
          | ValueNone -> {
              Amount = 0
              IsCritical = false
              IsEvaded = false
            }
          | ValueSome formulaId ->
            calculateDamage
              rparams.services.formulaStore
              formulaId
              actorStats
              targetStats
              rng

        // Apply hook modifications to damage
        // --- Enhanced Effect Hook Processing ---
        // 1. OnAbilityInvoke: collect all modifications
        let! invokeHookResult =
          processEffectHooks
            Effects.OnAbilityInvoke
            hookContext
            actorComponents.Effects
            rparams.services

        let totalInvokeDamageMod = invokeHookResult.DamageModification
        let totalInvokeShieldGen = invokeHookResult.ShieldGeneration
        let invokeResourceChanges = invokeHookResult.ResourceChanges

        let damageResult = {
          baseDamageResult with
              Amount = baseDamageResult.Amount + totalInvokeDamageMod
        }

        let actualDamage = max 0 damageResult.Amount

        // 2. OnDamageReceived: collect all modifications
        let damageHookContext = {
          hookContext with
              DamageResult = ValueSome damageResult
        }

        let! damageReceivedResult =
          processEffectHooks
            Effects.OnDamageReceived
            damageHookContext
            targetComponents.Effects
            rparams.services

        let totalReceivedDamageMod = damageReceivedResult.DamageModification
        let totalReceivedShieldGen = damageReceivedResult.ShieldGeneration
        let receivedResourceChanges = damageReceivedResult.ResourceChanges

        let finalDamage = actualDamage + totalReceivedDamageMod |> max 0

        // 3. Shield Generation (placeholder: add to target's shield pool)
        // TODO: Integrate shield system in future steps
        // let totalShieldGen = totalInvokeShieldGen + totalReceivedShieldGen
        // ... update targetComponents.Resources.Shields ...

        // 4. Resource Changes (placeholder: apply to target)
        // TODO: Integrate resource change system in future steps
        // let allResourceChanges = invokeResourceChanges @ receivedResourceChanges
        // ... apply resource changes ...

        let damageEvent =
          DamageApplied {
            target = targetId
            amount = finalDamage
          }

        let newHp = max 0 (targetComponents.Resources.HP - finalDamage)

        let updatedTargetAfterDamage: All = {
          targetComponents with
              Resources = {
                targetComponents.Resources with
                    HP = newHp
              }
        }

        let deathEvent, finalResources =
          Shared.checkForDeath newHp updatedTargetAfterDamage targetId

        let updatedTargetAfterDeathCheck = {
          updatedTargetAfterDamage with
              Resources = finalResources
        }

        let! struct (effectEvents, finalTarget) =
          Shared.applyAbilityEffects
            rparams.services.effectStore
            abilityDef
            actorId
            targetId
            updatedTargetAfterDeathCheck

        let struct (costEvents, actorWithCost) =
          Shared.applyResourceCost costOpt actorComponents actorId

        let finalActor =
          Shared.updateCooldowns actorWithCost abilityId gameTime abilityDef

        // 5. OnAbilityComplete: collect all modifications
        let completeHookContext = {
          damageHookContext with
              DamageResult =
                ValueSome {
                  damageResult with
                      Amount = finalDamage
                }
        }

        let! completeHookResult =
          processEffectHooks
            Effects.OnAbilityComplete
            completeHookContext
            finalActor.Effects
            rparams.services

        let totalCompleteShieldGen = completeHookResult.ShieldGeneration
        let completeResourceChanges = completeHookResult.ResourceChanges

        // 6. Resource Changes from OnAbilityComplete (placeholder)
        let finalActorWithCompleteEffects =
          if not(List.isEmpty completeResourceChanges) then
            let mutable updatedResources = finalActor.Resources

            for rc in completeResourceChanges do
              match rc with
              | Additive(resourceType, amount) ->
                match resourceType with
                | ResourceType.HP ->
                  updatedResources <- {
                    updatedResources with
                        HP = updatedResources.HP + amount
                  }
                | ResourceType.MP ->
                  updatedResources <- {
                    updatedResources with
                        MP = updatedResources.MP + amount
                  }
              | Exchange exch -> () // TODO: implement exchange logic
              | SetTo(resourceType, value) ->
                match resourceType with
                | ResourceType.HP ->
                  updatedResources <- { updatedResources with HP = value }
                | ResourceType.MP ->
                  updatedResources <- { updatedResources with MP = value }

            {
              finalActor with
                  Resources = updatedResources
            }
          else
            finalActor

        let changes =
          HashMap.ofList [
            actorId, finalActorWithCompleteEffects
            targetId, finalTarget
          ]

        let allEvents =
          [|
            damageEvent
            if costEvents.IsSome then
              costEvents.Value
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



  let resolveUseAbility
    (action: UseAbilityAction)
    (rparams: ResolverParams)
    : aval<StateChange> =
    adaptive {
      let abilityKind = rparams.services.abilityStore.tryFind action.abilityId

      match abilityKind with
      | ValueNone ->
        return {
          entities = HashMap.empty
          events = IndexList.empty
          gameTime = ValueNone
        }
      | ValueSome(Abilities.Passive _) ->
        // Passive abilities cannot be invoked
        return {
          entities = HashMap.empty
          events = IndexList.empty
          gameTime = ValueNone
        }
      | ValueSome(Abilities.Active abilityDef) ->

        // Determine actual targets based on ability targeting constraints
        let actualTargets =
          match abilityDef.Targeting with
          | Self -> IndexList.ofList [ action.actor ]
          | SingleAlly
          | SingleEnemy ->
            action.targets
            |> IndexList.isEmpty
            |> function
              | true -> IndexList.empty
              | false -> action.targets |> IndexList.take 1
          | MultiTarget maxTargets ->
            action.targets |> IndexList.take maxTargets

        if IndexList.isEmpty actualTargets then
          return {
            entities = HashMap.empty
            events = IndexList.empty
            gameTime = ValueNone
          }
        else
          // Process each target
          let! targetResults =
            actualTargets
            |> AList.ofIndexList
            |> AList.mapA(fun targetId ->
              let ractors = {
                actor = action.actor
                target = targetId
              }

              // Use unified resolver for all ability types
              resolveAbility action.abilityId (rparams, ractors))
            |> AList.toAVal

          // Combine all results
          let allEntities =
            targetResults
            |> IndexList.fold
              (fun acc result -> HashMap.union acc result.entities)
              HashMap.empty

          let allEvents =
            targetResults
            |> IndexList.fold
              (fun acc result -> IndexList.append acc result.events)
              IndexList.empty

          return {
            entities = allEntities
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
    | UseAbility action -> resolveUseAbility action resolverParams

  let apply (state: GameState) (change: StateChange) =
    transact(fun _ ->
      state.gameEvents.AddRange change.events

      for id, components in change.entities do
        state.entities[id] <- components)
