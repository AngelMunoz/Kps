namespace Pomo.Lib.Rules

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.VisualEffects
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain.Services
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.BattleManager
open Pomo.Lib.Battle
open Pomo.Lib.EffectApplication

module CommandHandler =
  open Pomo.Lib.Domain.Effects

  type ResolverParams = {
    derivedStats: amap<Guid<EntityId>, Attributes.DerivedStats>
    gameTime: cval<TimeSpan>
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

  type ResolverFn = ResolverParams -> ResolverActors -> aval<StateChange>

  let checkTauntTarget(actorEffects: HashMap<'a, ActiveEffect>) =
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
      | Some actor, Some target when actor.Resources.Status.IsAlive ->
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

    let resolveImmediate
      (abilityId: int<AbilityId>)
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (action: ValidatedActionResult)
      =
      adaptive {
        let actorId = ractors.actor
        let targetId = action.target

        let! gameTime = rparams.gameTime
        and! actorStats = rparams.derivedStats |> AMap.find actorId
        and! targetStats = rparams.derivedStats |> AMap.find targetId

        let calculateDamage =
          Resolution.calculateDamage {
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

        let visualEffects = [|
          if baseDamageResult.IsEvaded then
            AddFloatingText {
              Id = Guid.NewGuid() |> UMX.tag
              Text = "Miss"
              Position = action.targetComponents.Position
              Color = Evade
              CreationTick = gameTime
            }

          else if baseDamageResult.Amount > 0 then
            AddFloatingText {
              Id = Guid.NewGuid() |> UMX.tag
              Text = string baseDamageResult.Amount
              Position = action.targetComponents.Position
              Color = if baseDamageResult.IsCritical then Critical else Damage
              CreationTick = gameTime
            }
        |]


        let finalResources =
          Resolution.applyDamage baseDamageResult.Amount action.targetComponents

        let targetAfterDamage = {
          action.targetComponents with
              Resources = finalResources
        }

        let! targetAfterEffects =
          Resolution.applyAbilityEffects
            rparams.services.effectStore
            actorId
            action.abilityDefinition
            targetAfterDamage

        let! actorWithCost =
          Resolution.applyResourceCost
            action.cost
            action.actorComponents
            baseDamageResult.Amount

        let actorWithCooldown =
          Resolution.updateCooldowns
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
            StateChange.empty with
                updates = HashMap.single actorId merged
                visualEffects = visualEffects
          }
        else
          return {
            StateChange.empty with
                updates =
                  HashMap.ofSeq [
                    actorId, actorWithCooldown
                    targetId, targetAfterEffects
                  ]
                visualEffects = visualEffects
          }
      }

    let resolveDeferred
      (abilityId: int<AbilityId>)
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (action: ValidatedActionResult)
      =
      adaptive {
        let! gameTime = rparams.gameTime
        let resolutionId = %Guid.NewGuid()
        let mutable visualEffects = ResizeArray()
        let mutable audioChanges = ResizeArray()

        let castCues =
          Audio.Cues.createAbilityCastCue
            rparams.services.audioStore
            abilityId
            ractors.actor
            action.actorComponents.Position
            gameTime

        audioChanges.AddRange(castCues)

        // Schedule visual effects and pending resolutions
        action.abilityDefinition.ProjectileId
        |> ValueOption.iter(fun defId ->
          let resolution = {
            Id = resolutionId
            ActorId = ractors.actor
            Target = EntityResolution ractors.target
            AbilityId = abilityId
            TriggerTick = gameTime + TimeSpan.FromSeconds(5.0) // Fallback timeout
          }

          visualEffects.Add(AddPendingResolution resolution)

          visualEffects.Add(
            AddProjectile {
              Id = Guid.NewGuid() |> UMX.tag
              DefinitionId = defId
              CurrentPosition = action.actorComponents.Position
              Target = EntityTarget ractors.target
              CreationTick = gameTime
              PendingResolutionId = resolutionId
            }
          ))

        action.abilityDefinition.AoeId
        |> ValueOption.iter(fun defId ->
          let resolution = {
            Id = resolutionId
            ActorId = ractors.actor
            Target = EntityResolution ractors.target
            AbilityId = abilityId
            TriggerTick = gameTime + TimeSpan.FromSeconds(0.5) // Example delay
          }

          visualEffects.Add(AddPendingResolution resolution)

          visualEffects.Add(
            AddAoe {
              Id = Guid.NewGuid() |> UMX.tag
              DefinitionId = defId
              Position = action.targetComponents.Position
              CreationTick = gameTime
              PendingResolutionId = resolutionId
            }
          ))

        action.abilityDefinition.ImpactId
        |> ValueOption.iter(fun defId ->
          let impactDef = rparams.services.impactStore.find defId

          let resolution = {
            Id = resolutionId
            ActorId = ractors.actor
            Target = EntityResolution ractors.target
            AbilityId = abilityId
            TriggerTick = gameTime + impactDef.Duration
          }

          visualEffects.Add(AddPendingResolution resolution)

          visualEffects.Add(
            AddImpact {
              Id = Guid.NewGuid() |> UMX.tag
              DefinitionId = defId
              Position = action.targetComponents.Position
              CreationTick = gameTime
              PendingResolutionId = resolutionId
            }
          ))

        // Only apply cost and cooldown immediately
        let! actorWithCost =
          Resolution.applyResourceCost action.cost action.actorComponents 0 // No damage dealt yet

        let actorWithCooldown =
          Resolution.updateCooldowns
            actorWithCost
            abilityId
            gameTime
            action.abilityDefinition

        return {
          StateChange.empty with
              updates = HashMap.single ractors.actor actorWithCooldown
              visualEffects = visualEffects.ToArray()
              audioChanges = audioChanges.ToArray()
        }
      }

  /// Resolves an ability command
  let resolveAbility rparams abilityId ractors = adaptive {
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
    | MissingRequirements -> return StateChange.empty
    | ValidAction action ->
      let hasVisualEffect =
        action.abilityDefinition.ProjectileId.IsSome
        || action.abilityDefinition.AoeId.IsSome
        || action.abilityDefinition.ImpactId.IsSome

      if hasVisualEffect then
        return!
          AbilityResolution.resolveDeferred abilityId rparams ractors action
      else
        return!
          AbilityResolution.resolveImmediate abilityId rparams ractors action
  }

  let resolveNavigate
    (action: NavigateAction)
    (resolverParams: ResolverParams)
    : aval<StateChange> =
    adaptive {
      let! entity =
        ScenarioState.getEntityById action.actor resolverParams.scenarioState

      match entity with
      | Some e ->
        // Use entity-aware pathfinding to calculate the path
        let entityRadius = Movement.Utils.radiusOfStage e.Identity.Stage

        let updatedMovement =
          Movement.PathfindingCommands.setDestinationWithPathfinding
            resolverParams.scenarioState.scenario
            e.Position
            action.destination
            entityRadius
            e.Movement

        let updatedEntity = { e with Movement = updatedMovement }

        return {
          StateChange.empty with
              updates = HashMap.ofSeq [ action.actor, updatedEntity ]
        }
      | None -> return StateChange.empty
    }

  let resolveAdvancePosition
    (action: AdvancePositionAction)
    (resolverParams: ResolverParams)
    : aval<StateChange> =
    adaptive {
      let! entity =
        ScenarioState.getEntityById action.actor resolverParams.scenarioState

      match entity with
      | Some e ->
        let entityRadius = Movement.Utils.radiusOfStage e.Identity.Stage

        let proposedPos =
          Movement.Update.advancePosition {
            Position = e.Position
            Velocity = action.velocity
            Elapsed = action.elapsed
            Scenario = resolverParams.scenarioState.scenario
            EntityRadius = entityRadius
          }

        let terrainClear =
          Collision.Query.canMoveTo
            proposedPos
            entityRadius
            resolverParams.scenarioState.scenario

        if terrainClear then
          let updatedEntity = { e with Position = proposedPos }

          return {
            StateChange.empty with
                updates = HashMap.ofSeq [ action.actor, updatedEntity ]
          }
        else
          return StateChange.empty
      | None -> return StateChange.empty
    }

  let resolveUseAbility
    (action: UseAbilityAction)
    (rparams: ResolverParams)
    : aval<StateChange> =
    adaptive {
      let abilityKind = rparams.services.abilityStore.tryFind action.abilityId

      match abilityKind with
      | ValueNone
      | ValueSome(Passive _) -> return StateChange.empty
      | ValueSome(Active abilityDef) ->

      match action.target with
      | AbilityTarget.PositionTarget targetPos ->
        match abilityDef.Targeting with
        | GroundTarget radius ->
          let! gameTime = rparams.gameTime
          let resolutionId = %Guid.NewGuid()
          let mutable visualEffects = ResizeArray()
          
          let! actor = rparams.scenarioState.entities |> AMap.find action.actor
          
          abilityDef.ProjectileId
          |> ValueOption.iter(fun defId ->
            let resolution = {
              Id = resolutionId
              ActorId = action.actor
              Target = PositionResolution targetPos
              AbilityId = action.abilityId
              TriggerTick = gameTime + TimeSpan.FromSeconds(5.0)
            }
            
            visualEffects.Add(AddPendingResolution resolution)
            
            visualEffects.Add(
              AddProjectile {
                Id = Guid.NewGuid() |> UMX.tag
                DefinitionId = defId
                CurrentPosition = actor.Position
                Target = PositionTarget targetPos
                CreationTick = gameTime
                PendingResolutionId = resolutionId
              }
            ))
          let! actorWithCost = Resolution.applyResourceCost abilityDef.Cost actor 0
          let actorWithCooldown = Resolution.updateCooldowns actorWithCost action.abilityId gameTime abilityDef
          
          return {
            StateChange.empty with
                updates = HashMap.single action.actor actorWithCooldown
                visualEffects = visualEffects.ToArray()
          }
        | _ -> return StateChange.empty
      | EntityTargets targets ->

      let actualTargets =
        match abilityDef.Targeting with
        | Self -> [| action.actor |]
        | SingleAlly
        | SingleEnemy ->
          targets
          |> Array.tryHead
          |> Option.map(fun targetId -> [| targetId |])
          |> Option.defaultValue Array.empty
        | MultiTarget maxTargets -> targets |> Array.take maxTargets
        | GroundTarget _ -> Array.empty

      let resolver = resolveAbility rparams action.abilityId

      let stateChanges =
        actualTargets
        |> AList.ofArray
        |> AList.mapA(fun targetId ->
          resolver {
            actor = action.actor
            target = targetId
          })

      return!
        stateChanges
        |> AList.fold
          (fun acc result -> {
            acc with
                updates = HashMap.union acc.updates result.updates
                visualEffects =
                  Array.append acc.visualEffects result.visualEffects
                audioChanges =
                  Array.append acc.audioChanges result.audioChanges
          })
          StateChange.empty
    }

  [<Struct>]
  type ProcessReplenishmentDeps = {
    replenishments: ResourceReplenishment[]
    length: int
    entities: amap<Guid<EntityId>, EntityComponents>
    derivedStats: amap<Guid<EntityId>, DerivedStats>
  }

  [<TailCall>]
  let rec processReplenishments (deps: ref<ProcessReplenishmentDeps>) i acc = adaptive {
    if i >= deps.Value.length then
      return acc
    else
      let rep = deps.Value.replenishments[i]
      let actorId = rep.Actor
      let! actor = deps.Value.entities |> AMap.find actorId
      let! stats = deps.Value.derivedStats |> AMap.find actorId

      let updatedResources =
        match rep.ResourceType with
        | ResourceType.HP ->
          let newHp = min stats.HP (actor.Resources.HP + rep.Amount)
          { actor.Resources with HP = newHp }
        | ResourceType.MP ->
          let newMp = min stats.MP (actor.Resources.MP + rep.Amount)
          { actor.Resources with MP = newMp }

      let updatedActor = {
        actor with
            Resources = updatedResources
      }

      return!
        processReplenishments
          deps
          (i + 1)
          (HashMap.add actorId updatedActor acc)
  }

  let private resolveReplenishResources
    (replenishments: ResourceReplenishment[])
    (rparams: ResolverParams)
    : aval<StateChange> =
    adaptive {

      let dependencies = {
        replenishments = replenishments
        length = replenishments.Length
        entities = rparams.scenarioState.entities
        derivedStats = rparams.derivedStats
      }

      let! updatedEntities =
        processReplenishments (ref dependencies) 0 HashMap.empty

      return {
        StateChange.empty with
            updates = updatedEntities
      }
    }

  let evaluate (state: GameState) (cmd: Command) : aval<StateChange> = adaptive {
    let! activeScenarioId = state.activeScenarioId
    let! scenarioState = state.scenarios |> AMap.find activeScenarioId

    let derivedStats = DerivedStats.byScenario state.services scenarioState

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
    | Navigate action -> return! resolveNavigate action resolverParams
    | AdvancePosition action ->
      return! resolveAdvancePosition action resolverParams
    | ReplenishResources replenishments ->
      return! resolveReplenishResources replenishments resolverParams
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
        StateChange.empty with
            scenarioChanges = scenarioChanges
      }
    | RemoveEntities entityIds ->
      return {
        StateChange.empty with
            removals = entityIds |> Seq.toArray
      }
    | AddEntities entitiesToAdd ->
      return {
        StateChange.empty with
            additions = entitiesToAdd
      }

    | Teleport tp ->
      return {
        StateChange.empty with
            teleports = [| tp |]
      }
  }
