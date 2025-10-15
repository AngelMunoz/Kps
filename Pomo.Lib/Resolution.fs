namespace Pomo.Lib.Rules

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.State
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Services
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Scenario
open Pomo.Lib.BattleManager

open Pomo.Lib.Battle

module Resolution =
  [<Struct>]
  type DamageParams = {
    services: EngineServices
    attackerStats: DerivedStats
    defenderStats: DerivedStats
    attackerEffects: HashMap<int<EffectId>, ActiveEffect>
  }

  type ResolverParams = {
    derivedStats: amap<Guid<EntityId>, DerivedStats>
    gameTime: cval<int64<Tick>>
    scenarioState: ScenarioState
    players: amap<Guid<PlayerId>, PlayerContext>
    parties: amap<Guid<PartyId>, Party>
    services: EngineServices
  }

  [<Struct>]
  type ResolverActors = {
    actor: Guid<EntityId>
    target: Guid<EntityId>
  }

  type ResolverFn = ResolverParams * ResolverActors -> aval<StateChange>

  let calculateHitChance attackerStat defenderStat =
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

  let calculateDamage (damageParams: DamageParams) formulaId = adaptive {
    match damageParams.services.formulaStore.tryFind formulaId with
    | ValueSome formula ->

      let formulaResult =
        formula.Calculate {
          InvokerStats = damageParams.attackerStats
          InvokerElementalAttributes =
            damageParams.attackerStats.ElementAttributes
          TargetElementalResistances =
            damageParams.defenderStats.ElementResistances
        }

      // STEP 1: Hit/Miss calculation based on damage type
      let hitRoll = damageParams.services.rng()

      let hitChance =
        match formulaResult.DamageType with
        | DamageType.Neutral -> 1.0
        | DamageType.Physical ->
          calculateHitChance
            damageParams.attackerStats.AC
            damageParams.defenderStats.HV
        | DamageType.Magical ->
          calculateHitChance
            damageParams.attackerStats.LK
            damageParams.defenderStats.LK

      let isHit = hitRoll <= hitChance

      if not isHit then
        return {
          Amount = 0
          IsCritical = false
          IsEvaded = true
        }
      else
        // STEP 2-4: Calculate damage (includes base damage, modifiers, and final damage)
        // Apply critical hit (uses LK)
        let critRoll = damageParams.services.rng()
        let isCritical = critRoll < float damageParams.attackerStats.LK * 0.01

        let damageBonus =
          if isCritical then
            int(
              float(formulaResult.BaseDamage + formulaResult.ElementalDamage)
              * 0.10
            )
          else
            0

        let finalElementalDamage =
          if formulaResult.ElementalDamage > 0 then
            let elementRes =
              damageParams.defenderStats.ElementResistances.TryFindV
                formulaResult.Element
              |> ValueOption.defaultValue 0.0

            float formulaResult.ElementalDamage * (1.0 - elementRes) |> int
          else
            0

        let totalDamage = formulaResult.BaseDamage + finalElementalDamage

        // Apply defense reduction
        let damageAfterDefense =
          match formulaResult.DamageType with
          | DamageType.Physical -> totalDamage - damageParams.defenderStats.DP
          | DamageType.Magical -> totalDamage - damageParams.defenderStats.MD
          | DamageType.Neutral -> totalDamage

        // Apply AbilityDamageMod from active effects
        let abilityDamageMod =
          damageParams.attackerEffects
          |> HashMap.fold
            (fun acc _ effect ->
              effect.Definition.Modifiers
              |> Array.fold
                (fun modAcc modifier ->
                  match modifier with
                  | EffectModifier.AbilityDamageMod value -> modAcc + value
                  | _ -> modAcc)
                acc)
            0.0

        let damageWithModifier =
          if abilityDamageMod > 0.0 then
            damageAfterDefense
            + int(float damageAfterDefense * abilityDamageMod)
          else
            damageAfterDefense

        let finalDamage = max 0 (damageWithModifier + damageBonus)

        return {
          Amount = int finalDamage
          IsCritical = isCritical
          IsEvaded = false
        }
    | ValueNone ->
      return {
        Amount = 0
        IsCritical = false
        IsEvaded = false
      }
  }

  let checkTauntTarget actorEffects =
    actorEffects
    |> HashMap.filter(fun _ effect -> effect.Definition.Kind.IsTaunt)
    |> HashMap.fold
      (fun acc _ effect ->
        match acc with
        | ValueNone -> ValueSome effect
        | ValueSome current ->
          if effect.RemainingTicks > current.RemainingTicks then
            ValueSome effect
          else
            ValueSome current)
      ValueNone
    |> ValueOption.map _.SourceId

  module ValidateAction =
    let checkStun(actor: EntityComponents) =
      actor.Effects |> HashMap.exists(fun _ e -> e.Definition.Kind.IsStun)

    let checkSilence (actor: EntityComponents) abilityDef =
      let hasSilence =
        actor.Effects |> HashMap.exists(fun _ e -> e.Definition.Kind.IsSilence)

      let isSpellAbility =
        match abilityDef.Cost with
        // this means it costs either HP or MP, so it applies to be silenced
        | ValueSome _ -> true
        | _ -> false

      hasSilence && isSpellAbility


    let checkCooldown (actor: EntityComponents) abilityId gameTime = adaptive {
      let! gameTime = gameTime
      let cooldowns = actor.AbilityCooldowns |> HashMap.tryFindV abilityId

      return
        match cooldowns with
        | ValueSome readyTime -> gameTime < readyTime
        | ValueNone -> false
    }

    let checkResourceCost (actor: EntityComponents) abilityDef =
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
      actorStats
      requirements
      =
      requirements
      |> Array.forall(fun req ->
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

          actualValue >= minValue
        | AbilityRequirement abilityId ->
          HashSet.contains abilityId actor.Abilities
        | FormulaRequirement _ ->
          // For now, return true - formula validation would be implemented later
          true)

    let resolveTaunt rparams ractors initialTarget = adaptive {
      let! actor = rparams.entities |> AMap.tryFind ractors.actor

      match actor with
      | None -> return struct (ractors.target, initialTarget)
      | Some actor ->

      let forcedTargetId = checkTauntTarget actor.Effects

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
      (effectDef: EffectDefinition)
      (existingEffect: ActiveEffect)
      =
      let stacking = effectDef.Stacking

      match stacking with
      | NoStack -> ValueNone
      | RefreshDuration ->
        ValueSome {
          existingEffect with
              RemainingTicks =
                effectDef.Duration.Ticks |> ValueOption.defaultValue 0L<Tick>
              NextTickIn =
                effectDef.Duration.Interval |> ValueOption.defaultValue 0L<Tick>
        }
      | AddStack maxStacks ->
        let newStacks = min maxStacks (existingEffect.Stacks + 1)

        ValueSome {
          existingEffect with
              Stacks = newStacks
              RemainingTicks =
                effectDef.Duration.Ticks |> ValueOption.defaultValue 0L<Tick>
              NextTickIn =
                effectDef.Duration.Interval |> ValueOption.defaultValue 0L<Tick>
        }

    let processEffects
      (effectStore: Services.IEffectStore)
      (abilityDef: ActiveAbilityDefinition)
      (actorId: Guid<EntityId>)
      (currentEffects: HashMap<int<EffectId>, ActiveEffect>)
      =

      let mutable updatedMap = currentEffects

      for effId in abilityDef.Effects do
        let existing = HashMap.tryFindV effId updatedMap

        let newEffect =
          match existing with
          | ValueSome actEff ->
            // Determine stacking update
            match determineNewEffect actEff.Definition actEff with
            | ValueSome newEff -> ValueSome newEff
            | ValueNone -> ValueNone
          | ValueNone ->
            let effect = effectStore.tryFind effId

            match effect with
            | ValueNone -> ValueNone // Effect definition not found, skip
            | ValueSome effectDef ->
              // Create new effect instance
              let newEff: ActiveEffect = {
                EffectId = effId
                SourceId = actorId
                RemainingTicks =
                  effectDef.Duration.Ticks |> ValueOption.defaultValue 0L<Tick>
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

      updatedMap

    let applyAbilityEffects
      effectStore
      actorId
      (abilityDef: ActiveAbilityDefinition)
      (targetComponents: EntityComponents)
      =
      adaptive {
        let currentEffects = targetComponents.Effects

        let effects =
          processEffects effectStore abilityDef actorId currentEffects

        return {
          targetComponents with
              Effects = effects
        }
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
      (damageAmount: int)
      =
      adaptive {
        // Extract ResourceConversion modifiers from active effects
        let resourceConversions =
          actorComponents.Effects
          |> HashMap.fold
            (fun acc _ effect ->
              effect.Definition.Modifiers
              |> Array.fold
                (fun convAcc modifier ->
                  match modifier with
                  | EffectModifier.ResourceConversion(fromType, toType, ratio) ->
                    (fromType, toType, ratio) :: convAcc
                  | _ -> convAcc)
                acc)
            []

        // Apply base cost if present
        let resourcesAfterBaseCost =
          match costOpt with
          | ValueSome cost ->
            match cost.Type with
            | ResourceType.HP -> {
                actorComponents.Resources with
                    HP = actorComponents.Resources.HP - cost.Amount
              }
            | ResourceType.MP ->
                {
                  actorComponents.Resources with
                      MP = actorComponents.Resources.MP - cost.Amount
                }
          | ValueNone -> actorComponents.Resources

        // Apply ResourceConversion modifiers
        let finalResources: Attributes.Resources =
          resourceConversions
          |> List.fold
            (fun (resources: Attributes.Resources) (fromType, toType, ratio) ->
              match fromType, toType with
              | ResourceType.HP, ResourceType.HP when ratio < 0.0 ->
                // HP-cost amplification: consume HP based on damage dealt
                let hpCost = int(float damageAmount * abs ratio)

                {
                  resources with
                      HP = resources.HP - hpCost
                }
              | ResourceType.MP, ResourceType.HP when ratio > 0.0 ->
                // MP to HP conversion: convert MP to HP
                let mpToConvert = resources.MP
                let hpGained = int(float mpToConvert * ratio)

                {
                  resources with
                      MP = 0
                      HP = resources.HP + hpGained
                }
              | ResourceType.HP, ResourceType.MP when ratio > 0.0 ->
                // HP to MP conversion
                let hpToConvert = resources.HP
                let mpGained = int(float hpToConvert * ratio)

                {
                  resources with
                      HP = 0
                      MP = resources.MP + mpGained
                }
              | _ -> resources)
            resourcesAfterBaseCost

        return {
          actorComponents with
              Resources = finalResources
        }
      }

    let updateCooldowns
      (actorComponents: EntityComponents)
      abilityId
      gameTime
      abilityDef
      =
      {
        actorComponents with
            AbilityCooldowns =
              actorComponents.AbilityCooldowns
              |> HashMap.map(fun k v ->
                if k = abilityId then gameTime + abilityDef.Cooldown else v)
      }

  [<Struct>]
  type ValidatedActionResult = {
    actor: EntityComponents
    target: Guid<EntityId>
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
    | InvalidTarget
    | ValidAction of ValidatedActionResult

  let validateAction
    (rparams: ResolverParams)
    (ractors: ResolverActors)
    abilityId
    =
    adaptive {
      let! actor = rparams.scenarioState.entities |> AMap.tryFind ractors.actor

      let! target =
        rparams.scenarioState.entities |> AMap.tryFind ractors.target

      let abilityKind = rparams.services.abilityStore.tryFind abilityId

      match abilityKind with
      | ValueNone -> return NotFound
      | ValueSome(Passive _) -> return IsPassive
      | ValueSome(Active abilityDef) ->

      match actor, target with
      | Some actor, Some target when actor.Resources.Status = Alive ->
        let! canUse =
          Engagement.canUseAbility
            rparams.scenarioState
            rparams.parties
            ractors.actor
            target
            ractors.target
            abilityDef

        if not canUse then
          return InvalidTarget
        else
          let! actorStats = rparams.derivedStats |> AMap.find ractors.actor

          let isStunned = ValidateAction.checkStun actor

          let isSilenced = ValidateAction.checkSilence actor abilityDef

          let! isOnCooldown =
            ValidateAction.checkCooldown actor abilityId rparams.gameTime

          let struct (hasEnoughResource, cost) =
            ValidateAction.checkResourceCost actor abilityDef

          let hasRequirements =
            ValidateAction.checkAbilityRequirements
              actor
              actorStats
              abilityDef.Requirements

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
              ValidateAction.resolveTaunt rparams.scenarioState ractors target

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
    let applyDamage (damage: int) (targetComponents: EntityComponents) =
      let newHp = max 0 (targetComponents.Resources.HP - damage)

      let updatedTargetWithDamage = {
        targetComponents with
            EntityComponents.Resources.HP = newHp
      }

      Shared.checkForDeath newHp updatedTargetWithDamage

    let resolve
      (abilityId: int<AbilityId>)
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (action: ValidatedActionResult)
      =
      adaptive {
        let actorId = ractors.actor
        let targetId = action.target

        let! actorStats = rparams.derivedStats |> AMap.find actorId
        let! targetStats = rparams.derivedStats |> AMap.find targetId

        let calculateDamage =
          calculateDamage {
            services = rparams.services
            attackerStats = actorStats
            defenderStats = targetStats
            attackerEffects = action.actorComponents.Effects
          }

        let! baseDamageResult =
          match action.abilityDefinition.FormulaId with
          | ValueSome formulaId -> calculateDamage formulaId
          | ValueNone ->
            AVal.constant {
              Amount = 0
              IsCritical = false
              IsEvaded = false
            }

        let finalResources =
          applyDamage baseDamageResult.Amount action.targetComponents

        let targetAfterDamage = {
          action.targetComponents with
              Resources = finalResources
        }

        let! targetAfterEffects =
          Shared.applyAbilityEffects
            rparams.services.effectStore
            actorId
            action.abilityDefinition
            targetAfterDamage

        let! gameTime = rparams.gameTime

        let! actorWithCost =
          Shared.applyResourceCost
            action.cost
            action.actorComponents
            baseDamageResult.Amount

        let actorWithCooldown =
          Shared.updateCooldowns
            actorWithCost
            abilityId
            gameTime
            action.abilityDefinition

        if actorId = targetId then
          let merged = {
            targetAfterEffects with
                Resources = actorWithCooldown.Resources
                AbilityCooldowns = actorWithCooldown.AbilityCooldowns
          }

          return {
            updates = HashMap.single actorId merged
            additions = HashMap.empty
            removals = Array.empty
            gameTime = ValueNone
            scenarioChanges = Array.empty
            teleports = Array.empty
          }
        else
          return {
            updates =
              HashMap.ofList [
                actorId, actorWithCooldown
                targetId, targetAfterEffects
              ]
            additions = HashMap.empty
            removals = Array.empty
            gameTime = ValueNone
            scenarioChanges = Array.empty
            teleports = Array.empty
          }
      }

  /// Resolves an ability command
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
      | InvalidTarget
      | MissingRequirements ->
        return {
          updates = HashMap.empty
          additions = HashMap.empty
          removals = Array.empty
          gameTime = ValueNone
          scenarioChanges = Array.empty
          teleports = Array.empty
        }
      | ValidAction action ->
        return! AbilityResolution.resolve abilityId rparams ractors action
    }

  let resolveMove
    (action: MoveAction)
    (resolverParams: ResolverParams)
    : aval<StateChange> =
    adaptive {
      let! entity =
        resolverParams.scenarioState.entities |> AMap.tryFind action.actor

      match entity with
      | Some e ->
        // Use entity-aware pathfinding to calculate the path
        let entityRadius =
          Pomo.Lib.Movement.Utils.radiusOfStage e.Identity.Stage

        let allEntities = resolverParams.scenarioState.entities |> AMap.force
        let entitiesArray = allEntities |> HashMap.toArrayV

        let updatedMovement =
          Pomo.Lib.Movement.PathfindingCommands.setDestinationWithEntities
            resolverParams.scenarioState.scenario
            e.Position
            action.destination
            entityRadius
            entitiesArray
            action.actor
            e.Movement

        let updatedEntity = { e with Movement = updatedMovement }

        return {
          updates = HashMap.ofList [ action.actor, updatedEntity ]
          additions = HashMap.empty
          removals = Array.empty
          gameTime = ValueNone
          scenarioChanges = Array.empty
          teleports = Array.empty
        }
      | None ->
        return {
          updates = HashMap.empty
          additions = HashMap.empty
          removals = Array.empty
          gameTime = ValueNone
          scenarioChanges = Array.empty
          teleports = Array.empty
        }
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
          updates = HashMap.empty
          additions = HashMap.empty
          removals = Array.empty
          gameTime = ValueNone
          scenarioChanges = Array.empty
          teleports = Array.empty
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
          (fun acc result -> HashMap.union acc result.updates)
          HashMap.empty

      return {
        updates = components
        additions = HashMap.empty
        removals = Array.empty
        gameTime = ValueNone
        scenarioChanges = Array.empty
        teleports = Array.empty
      }
    }

  let evaluate (state: GameState) (cmd: Command) : aval<StateChange> = adaptive {
    let! activeScenarioId = state.activeScenarioId
    let! scenarioState = state.scenarios |> AMap.find activeScenarioId
    let! derivedStats = GameState.getDerivedStats state
    let players = state.players
    let parties = state.parties

    let resolverParams = {
      derivedStats = derivedStats
      gameTime = scenarioState.gameTime
      scenarioState = scenarioState
      players = players
      parties = parties
      services = state.services
    }

    match cmd with
    | UseAbility action -> return! resolveUseAbility action resolverParams
    | Move action -> return! resolveMove action resolverParams
    | Duel duelAction ->
      let! scenarioChanges =
        match duelAction with
        | Request(requester, target) ->
          Duel.request requester target scenarioState
        | PartyRequest(requester, target) ->
          PartyDuel.request requester target scenarioState
        | Accept(accepter, requester) ->
          Duel.accept accepter requester scenarioState
        | Cancel(canceller, otherPlayer) ->
          Duel.cancel canceller otherPlayer scenarioState

      return {
        updates = HashMap.empty
        additions = HashMap.empty
        removals = Array.empty
        gameTime = ValueNone
        scenarioChanges = scenarioChanges
        teleports = Array.empty
      }
    | RemoveEntities entityIds ->
      return {
        updates = HashMap.empty
        additions = HashMap.empty
        removals = entityIds |> Seq.toArray
        gameTime = ValueNone
        scenarioChanges = Array.empty
        teleports = Array.empty
      }
    | AddEntities entitiesToAdd ->
      return {
        updates = HashMap.empty
        additions = entitiesToAdd
        removals = Array.empty
        gameTime = ValueNone
        scenarioChanges = Array.empty
        teleports = Array.empty
      }
    | Teleport tp ->
      return {
        updates = HashMap.empty
        additions = HashMap.empty
        removals = Array.empty
        gameTime = ValueNone
        scenarioChanges = Array.empty
        teleports = [| tp |]
      }
  }
