namespace Pomo.Lib.Rules

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.State
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Services
open Pomo.Lib.Domain.Abilities

module Resolution =
  module ProcessHook =
    let processModifier
      (context: HookContext)
      (services: EngineServices)
      (modifier: Effects.EffectModifier)
      =
      match modifier with
      | Effects.EffectModifier.StaticMod _ ->
          // TODO: Handle static stat modifications if they can be triggered by hooks
          {
            DamageModification = DamageResult.Zero
            ResourceChanges = Array.empty
          }
      | Effects.EffectModifier.DynamicMod formulaId ->
        let formula = services.formulaStore.tryFind formulaId

        match formula with
        | ValueSome formula -> {
            DamageModification =
              formula.Calculate {
                InvokerStats = context.InvokerStats
                InvokerElementalAttributes =
                  context.InvokerStats.ElementAttributes
                TargetElementalResistances =
                  context.TargetStats.ElementResistances
              }
            ResourceChanges = Array.empty
          }
        | ValueNone ->
            {
              DamageModification = DamageResult.Zero
              ResourceChanges = Array.empty
            }
      | Effects.EffectModifier.AbilityDamageMod percent ->
        let damageMod =
          match context.ResolvedDamage with
          | ValueSome damage -> int(float damage.Amount * percent)
          | ValueNone -> 0 // No resolved damage yet, so no modification

        {
          DamageModification = {
            DamageResult.Zero with
                BaseDamage = damageMod
          }
          ResourceChanges = Array.empty
        }
      | Effects.EffectModifier.ResourceConversion(fromType, toType, ratio) ->
        // Calculate the conversion amount based on current resources
        let conversionAmount =
          match fromType with
          | ResourceType.HP -> int(float context.InvokerStats.HP * ratio)
          | ResourceType.MP -> int(float context.InvokerStats.MP * ratio)

        {
          DamageModification = DamageResult.Zero
          ResourceChanges = [|
            ResourceChange.Additive(struct (fromType, -conversionAmount)) // Subtract from source
            ResourceChange.Additive(struct (toType, conversionAmount)) // Add to target
          |]
        }
      | Effects.EffectModifier.ShieldGeneration formulaId ->
        match services.formulaStore.tryFind formulaId with
        | ValueSome formula ->
          let formulaContext = {
            InvokerStats = context.InvokerStats
            InvokerElementalAttributes = context.InvokerStats.ElementAttributes
            TargetElementalResistances = context.TargetStats.ElementResistances
          }

          let result = formula.Calculate formulaContext
          let _ = result.BaseDamage + result.ElementalDamage

          {
            DamageModification = DamageResult.Zero
            ResourceChanges = Array.empty
          }
        | ValueNone ->
            {
              DamageModification = DamageResult.Zero
              ResourceChanges = Array.empty
            }

    let processEffect
      (context: HookContext)
      (services: EngineServices)
      (effect: Effects.ActiveEffect)
      =
      effect.Definition.Modifiers
      |> Array.fold
        (fun acc modifier ->
          let result = processModifier context services modifier

          {
            DamageModification =
              acc.DamageModification + result.DamageModification
            ResourceChanges =
              Array.append acc.ResourceChanges result.ResourceChanges
          })
        {
          DamageModification = DamageResult.Zero
          ResourceChanges = Array.empty
        }

    let processAll
      (hook: Effects.EffectHook)
      (context: HookContext)
      (entityEffects: alist<Effects.ActiveEffect>)
      (services: EngineServices)
      =
      adaptive {
        let! effects =
          entityEffects
          |> AList.filter(fun effect ->
            effect.Definition.Hooks |> Array.exists(fun h -> h = hook))
          |> AList.toAVal

        let result =
          effects
          |> IndexList.fold
            (fun acc effect ->
              let modifications = processEffect context services effect

              {
                DamageModification =
                  acc.DamageModification + modifications.DamageModification
                ResourceChanges =
                  Array.append
                    acc.ResourceChanges
                    modifications.ResourceChanges
              })
            {
              DamageModification = DamageResult.Zero
              ResourceChanges = Array.empty
            }

        return result
      }

  let calculateHitChance (attackerStat: int) (defenderStat: int) =
    let attackerValue = float attackerStat
    let defenderValue = float defenderStat

    // Base chance to hit is 50%, adjusted by stats
    let baseHitChance = 0.5

    // If both stats are zero, it's a guaranteed hit.
    if attackerValue = 0.0 && defenderValue = 0.0 then
      1.0
    else
      // The effective stat difference. We use max to avoid negative results which would flip the logic.
      let effectiveAttacker = max 0.0 attackerValue
      let effectiveDefender = max 0.0 defenderValue

      let statAdvantage = effectiveAttacker - effectiveDefender

      // The divisor scales the effect of the stat advantage.
      // A larger divisor means stats have less impact on hit chance.
      let divisor = 100.0

      let chance = baseHitChance + (statAdvantage / divisor)

      // Clamp the result between a minimum and maximum hit chance
      // to ensure there's always a chance to hit or miss.
      max 0.05 (min 0.95 chance)

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
        | DamageType.Neutral
        | DamageType.Physical ->
          calculateHitChance attackerStats.AC defenderStats.HV
        | DamageType.Magical ->
          calculateHitChance attackerStats.LK defenderStats.LK

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
    entities: amap<int<EntityId>, EntityComponents>
    enemies: amap<int<EntityId>, EntityComponents>
    allies: amap<int<EntityId>, EntityComponents>
    derivedStats: amap<int<EntityId>, DerivedStats>
    gameTime: cval<int64<Tick>>
    services: EngineServices
  }

  type ResolverActors = {
    actor: int<EntityId>
    target: int<EntityId>
  }

  /// Helper function to check if an actor is taunted and must target a specific entity
  let checkTauntTarget(actorEffects: alist<Effects.ActiveEffect>) = adaptive {
    let tauntEffects =
      actorEffects
      |> AList.filter(fun effect ->
        effect.Definition.Kind = Effects.EffectKind.Taunt)

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
    let checkStun(actor: EntityComponents) =
      actor.Effects
      |> AList.exists(fun e -> e.Definition.Kind = Effects.EffectKind.Stun)

    let checkSilence
      (actor: EntityComponents)
      (abilityDef: ActiveAbilityDefinition)
      =
      adaptive {
        let! hasSilence =
          actor.Effects
          |> AList.exists(fun e ->
            e.Definition.Kind = Effects.EffectKind.Silence)

        let isSpellAbility =
          match abilityDef.Cost with
          | ValueSome c when c.Type = ResourceType.MP -> true
          | _ -> false

        return hasSilence && isSpellAbility
      }

    let checkCooldown
      (actor: EntityComponents)
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
      (actor: EntityComponents)
      (abilityDef: ActiveAbilityDefinition)
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
      (actor: EntityComponents)
      (actorStats: DerivedStats)
      (requirements: AbilityRequirement[])
      (_: EngineServices)
      =
      adaptive {
        let! hasAllRequirements =
          requirements
          |> AList.ofArray
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
            | FormulaRequirement _ ->
              // For now, return true - formula validation would be implemented later
              AVal.constant true)

        return hasAllRequirements
      }

    let resolveTaunt
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (initialTarget: EntityComponents)
      =
      adaptive {
        let! actor = rparams.entities |> AMap.tryFind ractors.actor

        match actor with
        | None -> return struct (ractors.target, initialTarget)
        | Some actor ->

        let! forcedTargetId = checkTauntTarget actor.Effects

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

    let determineNewEffect
      (effectDef: Effects.EffectDefinition)
      (existingEffect: Effects.ActiveEffect)
      =
      let stacking = effectDef.Stacking

      match stacking with
      | Effects.NoStack -> ValueNone
      | Effects.RefreshDuration ->
        ValueSome {
          existingEffect with
              RemainingTicks =
                effectDef.Duration.Ticks |> ValueOption.defaultValue 0L<Tick>
              NextTickIn =
                effectDef.Duration.Interval |> ValueOption.defaultValue 0L<Tick>
        }
      | Effects.AddStack maxStacks ->
        let newStacks = min maxStacks (existingEffect.Stacks + 1)

        ValueSome {
          existingEffect with
              Stacks = newStacks
              RemainingTicks =
                effectDef.Duration.Ticks |> ValueOption.defaultValue 0L<Tick>
              NextTickIn =
                effectDef.Duration.Interval |> ValueOption.defaultValue 0L<Tick>
        }



    let processEffect
      (effectDef: Effects.EffectDefinition)
      (targetComponents: EntityComponents)
      (actorId: int<EntityId>)
      (_: int<EntityId>)
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
              RemainingTicks =
                effectDef.Duration.Ticks |> ValueOption.defaultValue 0L<Tick>
              NextTickIn =
                effectDef.Duration.Interval |> ValueOption.defaultValue 0L<Tick>
              Stacks = 1
              Definition = effectDef
            }

        return newEffect
      }

    let applyAbilityEffects
      (effectStore: IEffectStore)
      (abilityDef: ActiveAbilityDefinition)
      (actorId: int<EntityId>)
      (targetComponents: EntityComponents)
      =
      let processEffects(currentEffects: IndexList<Effects.ActiveEffect>) =
        let currentMap =
          currentEffects
          |> IndexList.fold
            (fun m e -> HashMap.add e.EffectId e m)
            HashMap.empty

        let mutable updatedMap = currentMap

        for effId in abilityDef.Effects do
          match effectStore.tryFind effId with
          | ValueNone -> ()
          | ValueSome effectDef ->
            let existing = HashMap.tryFindV effId updatedMap

            let newEffect =
              match existing with
              | ValueSome actEff ->
                // Determine stacking update
                match determineNewEffect effectDef actEff with
                | ValueSome newEff -> ValueSome newEff
                | ValueNone -> ValueNone
              | ValueNone ->
                // Create new effect instance
                let newEff: Effects.ActiveEffect = {
                  EffectId = effectDef.Id
                  SourceId = actorId
                  RemainingTicks =
                    effectDef.Duration.Ticks
                    |> ValueOption.defaultValue 0L<Tick>
                  NextTickIn =
                    effectDef.Duration.Interval
                    |> ValueOption.defaultValue 0L<Tick>
                  Stacks = 1
                  Definition = effectDef
                }

                ValueSome newEff

            newEffect
            |> ValueOption.iter(fun ne ->
              updatedMap <- HashMap.add effId ne updatedMap)

        let updatedEffects = updatedMap |> HashMap.toValueArray |> AList.ofArray

        {
          targetComponents with
              Effects = updatedEffects
        }

      adaptive {
        let! currentEffects = targetComponents.Effects |> AList.toAVal
        return processEffects currentEffects
      }

    let checkForDeath (newHp: int) (targetComponents: EntityComponents) =
      if newHp <= 0 && targetComponents.Resources.Status = Status.Alive then
        {
          targetComponents.Resources with
              Status = Dead
              HP = newHp
        }
      else
        {
          targetComponents.Resources with
              HP = newHp
        }

    let applyResourceCost
      (costOpt: ResourceCost voption)
      (actorComponents: EntityComponents)
      =
      match costOpt with
      | ValueSome cost ->
        let updatedResources =
          match cost.Type with
          | ResourceType.HP ->
            let newAmount = actorComponents.Resources.HP - cost.Amount

            {
              actorComponents.Resources with
                  HP = newAmount
            }
          | ResourceType.MP ->
            let newAmount = actorComponents.Resources.MP - cost.Amount

            {
              actorComponents.Resources with
                  MP = newAmount
            }

        {
          actorComponents with
              Resources = updatedResources
        }
      | ValueNone -> actorComponents

    let updateCooldowns
      (actorComponents: EntityComponents)
      (abilityId: int<AbilityId>)
      (gameTime: int64<Tick>)
      (abilityDef: ActiveAbilityDefinition)
      =
      {
        actorComponents with
            AbilityCooldowns =
              actorComponents.AbilityCooldowns
              |> AMap.map(fun k v ->
                if k = abilityId then gameTime + abilityDef.Cooldown else v)
      }

  [<Struct>]
  type ValidatedActionResult = {
    actor: EntityComponents
    target: int<EntityId>
    targetComponents: EntityComponents
    cost: ResourceCost voption
    abilityDefinition: ActiveAbilityDefinition
    actorComponents: EntityComponents
  }

  [<Struct>]
  type ValidateActionResult =
    | Stunned
    | Silenced
    | OnCooldown
    | MissingRequirements
    | InsufficientResource
    | NotAlive
    | NotFound
    | IsPassive
    | ValidAction of ValidatedActionResult

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
      | ValueNone -> return NotFound
      | ValueSome(Passive _) -> return IsPassive
      | ValueSome(Active abilityDef) ->

      match actor, target with
      | Some actor, Some target when actor.Resources.Status = Alive ->
        let! actorStats = rparams.derivedStats |> AMap.find ractors.actor

        let! isStunned = ValidateAction.checkStun actor

        let! isSilenced = ValidateAction.checkSilence actor abilityDef

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

        if isStunned then
          return Stunned
        else if isSilenced then
          return Silenced
        else if isOnCooldown then
          return OnCooldown
        else if not hasEnoughResource then
          return InsufficientResource
        else if not hasRequirements then
          return MissingRequirements
        else

          let! struct (targetId, targetComponents) =
            ValidateAction.resolveTaunt rparams ractors target

          return
            ValidAction {
              actor = actor
              target = targetId
              targetComponents = targetComponents
              cost = cost
              abilityDefinition = abilityDef
              actorComponents = actor
            }
      | _ -> return NotAlive
    }

  module AbilityResolution =
    let processInvokeHooks
      (rparams: ResolverParams)
      (abilityId: int<AbilityId>)
      (actorComponents: EntityComponents)
      (actorStats: DerivedStats)
      (targetStats: DerivedStats)
      =
      adaptive {
        let! gameTime = rparams.gameTime

        let hookContext = {
          InvokerStats = actorStats
          TargetStats = targetStats
          AbilityId = abilityId
          GameTime = gameTime
          ResolvedDamage = ValueNone
        }

        return!
          ProcessHook.processAll
            Effects.OnAbilityInvoke
            hookContext
            actorComponents.Effects
            rparams.services
      }

    let calculateBaseDamage
      (rparams: ResolverParams)
      (abilityDef: ActiveAbilityDefinition)
      (actorStats: DerivedStats)
      (targetStats: DerivedStats)
      =
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
          rparams.services.rng

    let processDamageReceivedHooks
      (rparams: ResolverParams)
      (abilityId: int<AbilityId>)
      (damageResult: ResolvedDamage)
      (actorStats: DerivedStats)
      (targetComponents: EntityComponents)
      (targetStats: DerivedStats)
      =
      adaptive {
        let! gameTime = rparams.gameTime

        let hookContext = {
          InvokerStats = actorStats
          TargetStats = targetStats
          AbilityId = abilityId
          GameTime = gameTime
          ResolvedDamage = ValueSome damageResult
        }

        return!
          ProcessHook.processAll
            Effects.OnDamageReceived
            hookContext
            targetComponents.Effects
            rparams.services
      }

    let applyDamageAndCheckDeath
      (damage: int)
      (targetComponents: EntityComponents)
      =
      let newHp = max 0 (targetComponents.Resources.HP - damage)

      let updatedTargetWithDamage = {
        targetComponents with
            EntityComponents.Resources.HP = newHp
      }

      Shared.checkForDeath newHp updatedTargetWithDamage

    let applyResourceChanges
      (changes: ResourceChange[])
      (entity: EntityComponents)
      =
      let mutable updatedResources = entity.Resources

      for rc in changes do
        match rc with
        | Additive(struct (resType, amount)) ->
          match resType with
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
        | SetTo _ -> ()

      {
        entity with
            Resources = updatedResources
      }

    let resolve
      (abilityId: int<AbilityId>)
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (actorComponents: EntityComponents)
      (targetComponents: EntityComponents)
      (costOpt: ResourceCost voption)
      (abilityDef: ActiveAbilityDefinition)
      =
      adaptive {
        let actorId = ractors.actor
        let targetId = ractors.target

        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId
        let! gameTime = rparams.gameTime

        // 1. OnAbilityInvoke hooks
        let! invokeHookResult =
          processInvokeHooks
            rparams
            abilityId
            actorComponents
            actorStats
            targetStats

        let baseDamageResult =
          calculateBaseDamage rparams abilityDef actorStats targetStats

        let damageAfterInvoke: ResolvedDamage = {
          baseDamageResult with
              Amount =
                baseDamageResult.Amount
                + invokeHookResult.DamageModification.BaseDamage
        }

        // 2. OnDamageReceived hooks
        let! damageReceivedResult =
          processDamageReceivedHooks
            rparams
            abilityId
            damageAfterInvoke
            actorStats
            targetComponents
            targetStats

        // 3. Apply shield absorption
        let damageAfterReceived =
          damageAfterInvoke.Amount
          + damageReceivedResult.DamageModification.BaseDamage
          |> max 0

        // 4. Apply final damage and check for death
        let finalResources =
          applyDamageAndCheckDeath damageAfterReceived targetComponents

        let targetAfterDamage = {
          targetComponents with
              Resources = finalResources
        }

        // 5. Apply ability effects
        let! targetAfterEffects =
          Shared.applyAbilityEffects
            rparams.services.effectStore
            abilityDef
            actorId
            targetAfterDamage

        // 6. Apply costs to actor
        let actorWithCost = Shared.applyResourceCost costOpt actorComponents

        // 7. Update actor cooldowns
        let actorWithCooldown =
          Shared.updateCooldowns actorWithCost abilityId gameTime abilityDef

        // 8. OnAbilityComplete hooks
        let completeHookContext = {
          InvokerStats = actorStats
          TargetStats = targetStats
          AbilityId = abilityId
          GameTime = gameTime
          ResolvedDamage = ValueSome damageAfterInvoke
        }


        // 9. Apply resource and shield changes from all hooks
        let actorAfterResourceChanges =
          applyResourceChanges
            invokeHookResult.ResourceChanges
            actorWithCooldown

        let targetWithResourceChanges =
          applyResourceChanges
            damageReceivedResult.ResourceChanges
            targetAfterEffects

        let! result =
          ProcessHook.processAll
            Effects.OnAbilityComplete
            completeHookContext
            actorWithCooldown.Effects
            rparams.services

        let actorAfterAllChanges =
          applyResourceChanges result.ResourceChanges actorAfterResourceChanges



        return {
          entities =
            HashMap.ofList [
              actorId, actorAfterAllChanges
              targetId, targetWithResourceChanges
            ]
          gameTime = ValueNone
        }
      }

  /// Resolves an ability command, calculating damage and generating events.
  let resolveAbility(abilityId: int<AbilityId>) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | Stunned
      | Silenced
      | IsPassive
      | NotFound
      | NotAlive
      | InsufficientResource
      | OnCooldown
      | MissingRequirements ->
        return {
          entities = HashMap.empty
          gameTime = ValueNone
        }
      | ValidAction action ->
        return!
          AbilityResolution.resolve
            abilityId
            rparams
            ractors
            action.actorComponents
            action.targetComponents
            action.cost
            action.abilityDefinition
    }



  let resolveUseAbility
    (action: UseAbilityAction)
    (rparams: ResolverParams)
    : aval<StateChange> =
    adaptive {
      let abilityKind = rparams.services.abilityStore.tryFind action.abilityId

      match abilityKind with
      | ValueNone
      | ValueSome(Passive _) ->
        // Passive abilities cannot be invoked
        return {
          entities = HashMap.empty
          gameTime = ValueNone
        }
      | ValueSome(Active abilityDef) ->

      // Determine actual targets based on ability targeting constraints
      let actualTargets =
        match abilityDef.Targeting with
        | Self -> [| action.actor |]
        | SingleAlly
        | SingleEnemy ->
          action.targets
          |> Array.tryHead
          |> Option.map(fun targetId -> [| targetId |])
          |> Option.defaultValue Array.empty
        | MultiTarget maxTargets -> action.targets |> Array.take maxTargets

      // Process each target
      let! components =
        actualTargets
        |> AList.ofArray
        |> AList.mapA(fun targetId ->
          let ractors = {
            actor = action.actor
            target = targetId
          }

          // Use unified resolver for all ability types
          resolveAbility action.abilityId (rparams, ractors))
        |> AList.fold
          (fun acc result -> HashMap.union acc result.entities)
          HashMap.empty

      return {
        entities = components
        gameTime = ValueNone
      }
    }

  let step (state: GameState) (cmd: Command) : aval<StateChange> =
    let derivedStats = GameState.getDerivedStats state
    let enemies = GameState.getEnemies state
    let allies = GameState.getAllies state

    let resolverParams = {
      entities = state.entities
      enemies = enemies
      allies = allies
      derivedStats = derivedStats
      gameTime = state.gameTime
      services = state.services
    }

    match cmd with
    | UseAbility action -> resolveUseAbility action resolverParams

  let apply (state: GameState) (change: StateChange) =
    transact(fun _ ->
      for id, components in change.entities do
        state.entities[id] <- components)
