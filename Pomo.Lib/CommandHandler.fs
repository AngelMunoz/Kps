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
open Pomo.Lib.Domain.Inventory
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.BattleManager
open Pomo.Lib.Battle
open Pomo.Lib.EffectApplication
open Pomo.Lib.InventoryManagement

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

  [<Struct>]
  type CanTargetPredicateParams = {
    targetId: Guid<EntityId>
    targetFactions: HashSet<Classification.Faction>
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
            | MovementSpeed -> actorStats.MovementSpeed

          actualValue >= minValue
        | AbilityRequirement abilityId ->
          HashSet.contains abilityId actor.Abilities
        | FormulaRequirement _ ->
          // For now, return true - formula validation would be implemented later
          true)

    let inline distance (p1: Position) (p2: Position) =
      let dx = p1.X - p2.X
      let dy = p1.Y - p2.Y
      sqrt(dx * dx + dy * dy)

    let checkRange (actorPos: Position) (targetPos: Position) (range: float32) =
      distance actorPos targetPos <= range

    let resolveTaunt rparams ractors initialTarget = adaptive {
      let! actor =
        ScenarioState.getEntityById ractors.actor rparams.scenarioState

      match actor with
      | None -> return struct (ractors.target, initialTarget)
      | Some actor ->

      let forcedTargetId = checkTauntTarget actor.Effects

      match forcedTargetId with
      | ValueNone -> return struct (ractors.target, initialTarget)
      | ValueSome targetId ->

      let! newTarget =
        ScenarioState.getEntityById targetId rparams.scenarioState

      return
        match newTarget with
        | Some t -> struct (targetId, t)
        | None -> struct (ractors.target, initialTarget)
    }

  module TargetResolution =
    type TargetFilterPredicate = CanTargetPredicateParams -> aval<bool>

    let getGroundTargets
      (entities: amap<Guid<EntityId>, EntityComponents>)
      (targetPos: Position)
      (radius: float32)
      (targetFilter: TargetFilterPredicate)
      =
      entities
      |> AMap.filterA(fun entityId entity -> adaptive {
        let canTargetParams = {
          targetId = entityId
          targetFactions = entity.Factions
        }

        let dist = ValidateAction.distance entity.Position targetPos
        let isInRange = dist <= radius
        let! canTarget = targetFilter canTargetParams
        return isInRange && canTarget
      })
      |> AMap.fold
        (fun acc id _ -> ResizeArray.add id acc)
        (ResizeArray.empty())
      |> AVal.map(fun targets -> targets |> ResizeArray.toArray)

    let getAreaRandomTargets
      (entities: amap<Guid<EntityId>, EntityComponents>)
      (targetPos: Position)
      (radius: float32)
      (maxTargets: int)
      (targetFilter: TargetFilterPredicate)
      =
      entities
      |> AMap.filterA(fun entityId entity -> adaptive {
        let dist = ValidateAction.distance entity.Position targetPos
        let isInRange = dist <= radius

        let! canTarget =
          targetFilter {
            targetId = entityId
            targetFactions = entity.Factions
          }

        return isInRange && canTarget
      })
      |> AMap.fold
        (fun acc id _ -> ResizeArray.add id acc)
        (ResizeArray.empty())
      |> AVal.map(fun targets ->
        let arr = targets |> ResizeArray.toArray
        arr |> Array.randomShuffleInPlace
        arr |> Array.truncate maxTargets)


    let getAreaRandomPoints
      (targetPos: Position)
      (radius: float32)
      (numPoints: int)
      (rng: unit -> float)
      : Position[] =
      Array.init numPoints (fun _ ->
        let angle = float32(rng() * 2.0 * Math.PI)
        let r = radius * sqrt(float32(rng()))
        let x = targetPos.X + float32(r * cos angle)
        let y = targetPos.Y + float32(r * sin(angle))
        { X = x; Y = y })


    let getChainTargets
      (entities: amap<Guid<EntityId>, EntityComponents>)
      (initialTargetId: Guid<EntityId>)
      (maxChains: int)
      (range: float32)
      (targetFilter: TargetFilterPredicate)
      : aval<Guid<EntityId>[]> =
      adaptive {
        let rec findChain
          (remaining: int)
          (currentId: Guid<EntityId>)
          (acc: Guid<EntityId> HashSet)
          =
          adaptive {
            if remaining <= 0 then
              return acc
            else
              let! currentEntityOpt = entities |> AMap.tryFind currentId

              match currentEntityOpt with
              | None -> return acc
              | Some currentEntity ->
                let! closest =
                  entities
                  |> AMap.filterA(fun targetId targetEntity ->
                    targetFilter {
                      targetId = targetId
                      targetFactions = targetEntity.Factions
                    })
                  |> AMap.fold
                    (fun
                         (closestOpt: (Guid<EntityId> * float32) voption)
                         targetId
                         targetEntity ->
                      if not(acc |> HashSet.contains targetId) then
                        let dist =
                          ValidateAction.distance
                            currentEntity.Position
                            targetEntity.Position

                        if dist <= range then
                          match closestOpt with
                          | ValueSome(_, closestDist) ->
                            if dist < closestDist then
                              ValueSome(targetId, dist)
                            else
                              closestOpt
                          | ValueNone -> ValueSome(targetId, dist)
                        else
                          closestOpt
                      else
                        closestOpt)
                    ValueNone

                match closest with
                | ValueSome(closestId, _) ->
                  return!
                    findChain
                      (remaining - 1)
                      closestId
                      (HashSet.add closestId acc)
                | ValueNone -> return acc
          }

        let! chainedTargets =
          findChain maxChains initialTargetId (HashSet.single initialTargetId)

        return chainedTargets |> HashSet.toArray |> Array.rev
      }

    let getConeTargets
      (entities: amap<Guid<EntityId>, EntityComponents>)
      (actorId: Guid<EntityId>)
      (targetId: Guid<EntityId>)
      (angle: float32)
      (range: float32)
      (maxTargets: int)
      (targetFilter: TargetFilterPredicate)
      : aval<Guid<EntityId>[]> =
      adaptive {
        let! actorOpt = entities |> AMap.tryFind actorId
        let! targetOpt = entities |> AMap.tryFind targetId

        match actorOpt, targetOpt with
        | Some actor, Some target ->
          let actorPos = actor.Position
          let targetPos = target.Position

          let toAngle(p: Position) = atan2 p.Y p.X

          let direction = {
            X = targetPos.X - actorPos.X
            Y = targetPos.Y - actorPos.Y
          }

          let coneAngle = toAngle direction
          let halfAngle = angle / 2.0f

          let! targetsInCone =
            entities
            |> AMap.filterA(fun entityId entity ->
              targetFilter {
                targetId = entityId
                targetFactions = entity.Factions
              })
            |> AMap.fold
              (fun acc entityId entity ->
                let dist = ValidateAction.distance actorPos entity.Position

                if dist <= range then
                  let entityDirection = {
                    X = entity.Position.X - actorPos.X
                    Y = entity.Position.Y - actorPos.Y
                  }

                  let entityAngle = toAngle entityDirection
                  let angleDifference = abs(coneAngle - entityAngle)

                  let normalizedAngleDiff =
                    if angleDifference > MathF.PI then
                      2.0f * MathF.PI - angleDifference
                    else
                      angleDifference

                  if normalizedAngleDiff <= halfAngle then
                    ResizeArray.add entityId acc
                  else
                    acc
                else
                  acc)
              (ResizeArray.empty())

          let arr = targetsInCone |> ResizeArray.toArray
          arr |> Array.randomShuffleInPlace
          return arr |> Array.truncate maxTargets
        | _ -> return Array.empty
      }

    let getStraightLineTargets
      (entities: amap<Guid<EntityId>, EntityComponents>)
      (actorPos: Position)
      (targetPos: Position)
      (range: float32)
      (width: float32)
      (maxTargets: int)
      (targetFilter: TargetFilterPredicate)
      (actorId: Guid<EntityId>)
      : aval<Guid<EntityId>[]> =
      adaptive {
        let direction = {
          X = targetPos.X - actorPos.X
          Y = targetPos.Y - actorPos.Y
        }

        let length = sqrt(direction.X * direction.X + direction.Y * direction.Y)

        let normalizedDir =
          if length = 0.0f then
            { X = 1.0f; Y = 0.0f } // Default direction if actor and target are at same spot
          else
            {
              X = direction.X / length
              Y = direction.Y / length
            }

        let perpendicularDir = {
          X = -normalizedDir.Y
          Y = normalizedDir.X
        }

        let effectiveRange = if range > 0.0f then min length range else length

        let! targetsInLine =
          entities
          |> AMap.filterA(fun entityId entity ->
            if entityId = actorId then
              AVal.constant false
            else
              targetFilter {
                targetId = entityId
                targetFactions = entity.Factions
              })
          |> AMap.fold
            (fun acc entityId entity ->
              let entityVector = {
                X = entity.Position.X - actorPos.X
                Y = entity.Position.Y - actorPos.Y
              }

              let dot_product_projection =
                entityVector.X * normalizedDir.X
                + entityVector.Y * normalizedDir.Y

              let dot_product_perpendicular =
                abs(
                  entityVector.X * perpendicularDir.X
                  + entityVector.Y * perpendicularDir.Y
                )

              if
                dot_product_projection >= 0.0f
                && dot_product_projection <= effectiveRange
                && dot_product_perpendicular <= (width / 2.0f)
              then
                ResizeArray.add entityId acc
              else
                acc)
            (ResizeArray.empty())

        let arr = targetsInLine |> ResizeArray.toArray
        arr |> Array.randomShuffleInPlace
        return arr |> Array.truncate maxTargets
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
    | OutOfRange
    | TargetResourceFull
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
        let! struct (targetId, targetComponents) =
          ValidateAction.resolveTaunt rparams ractors target

        let! canUse =
          Engagement.canUseAbility
            rparams.scenarioState
            rparams.parties
            ractors.actor
            targetComponents
            targetId
            abilityDef

        if not canUse then
          return InvalidTarget
        else
          let! actorStats = rparams.derivedStats |> AMap.find ractors.actor
          let! targetStats = rparams.derivedStats |> AMap.find targetId

          let isResourceFull =
            if abilityDef.Intent = AbilityIntent.Support then
              abilityDef.Effects
              |> Array.exists(fun effectId ->
                let effectDef = rparams.services.effectStore.find effectId

                effectDef.Modifiers
                |> Array.exists(fun modifier ->
                  match modifier with
                  | StaticMod(Additive(HP, _)) ->
                    targetComponents.Resources.HP >= targetStats.HP
                  | StaticMod(Additive(MP, _)) ->
                    targetComponents.Resources.MP >= targetStats.MP
                  | _ -> false))
            else
              false

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

          let inRange =
            ValidateAction.checkRange
              actor.Position
              targetComponents.Position
              abilityDef.Range

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
          else if not inRange then
            return OutOfRange
          else if isResourceFull then
            return TargetResourceFull
          else
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

    let resolveCasting
      (abilityId: int<AbilityId>)
      (rparams: ResolverParams)
      (ractors: ResolverActors)
      (action: ValidatedActionResult)
      =
      adaptive {
        let! gameTime = rparams.gameTime
        let resolutionId = %Guid.NewGuid()

        let resolution = {
          Id = resolutionId
          ActorId = ractors.actor
          Target = EntityResolution action.target
          AbilityId = abilityId
          TriggerTick = gameTime + action.abilityDefinition.CastingTime.Value
        }

        // Only apply cost and cooldown immediately
        let! actorWithCost =
          Resolution.applyResourceCost action.cost action.actorComponents 0 // No damage dealt yet

        let actorWithCooldown =
          Resolution.updateCooldowns
            actorWithCost
            abilityId
            gameTime
            action.abilityDefinition.Cooldown

        let finalActor = {
          actorWithCooldown with
              Movement = {
                actorWithCooldown.Movement with
                    Path = []
                    Destination = ValueNone
              }
        }

        let visualEffects = [|
          let resGuid = UMX.untag resolutionId

          AddObject(
            resGuid,
            VisualEffects.ActiveObject.PendingResolution resolution
          )

          match action.abilityDefinition.PreActivationVisualEffectIds with
          | [||] -> ()
          | [| defId |] ->
            let impactGuid = Guid.NewGuid()

            let impact = {
              Id = impactGuid |> UMX.tag<ImpactId>
              DefinitionId = defId
              Position = action.actorComponents.Position
              CreationTick = gameTime
              PendingResolutionId = ValueNone
            }

            AddObject(impactGuid, ActiveObject.Impact impact)
          | rest ->
            for defId in rest do
              let impactGuid = Guid.NewGuid()

              let impact = {
                Id = impactGuid |> UMX.tag<ImpactId>
                DefinitionId = defId
                Position = action.actorComponents.Position
                CreationTick = gameTime
                PendingResolutionId = ValueNone
              }

              AddObject(impactGuid, ActiveObject.Impact impact)

        |]

        return {
          StateChange.empty with
              updates = HashMap.single ractors.actor finalActor
              visualEffects = visualEffects
        }
      }

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
            let ftId = Guid.NewGuid()

            let ft = {
              Id = ftId |> UMX.tag
              Text = "Miss"
              Position = action.targetComponents.Position
              Color = Evade
              CreationTick = gameTime
            }


            AddObject(ftId, ActiveObject.FloatingText ft)

          else if baseDamageResult.Amount > 0 then
            let ftId = Guid.NewGuid()

            let ft = {
              Id = ftId |> UMX.tag
              Text = string baseDamageResult.Amount
              Position = action.targetComponents.Position
              Color = if baseDamageResult.IsCritical then Critical else Damage
              CreationTick = gameTime
            }


            AddObject(ftId, ActiveObject.FloatingText ft)


          for effectId in action.abilityDefinition.Effects do
            let effectDef = rparams.services.effectStore.find effectId

            for modifier in effectDef.Modifiers do
              match modifier with
              | StaticMod(Subtractive(stat, value))
              | StaticMod(Additive(stat, value)) ->
                let ftId = Guid.NewGuid()

                let ft = {
                  Id = ftId |> UMX.tag
                  Text = $"+%d{value}{stat}"
                  Position = action.targetComponents.Position
                  Color = if value > 0 then Heal else Damage
                  CreationTick = gameTime
                }


                AddObject(ftId, ActiveObject.FloatingText ft)

              | ResourceConversion(from, into, rate) ->
                let ftId = Guid.NewGuid()

                let ft = {
                  Id = ftId |> UMX.tag
                  Text =
                    $"{from} -> %d{int(float baseDamageResult.Amount * rate)}{into}"
                  Position = action.targetComponents.Position
                  Color = if rate > 0. then Heal else Damage
                  CreationTick = gameTime
                }


                AddObject(ftId, ActiveObject.FloatingText ft)

              | AbilityDamageMod damage ->
                let ftId = Guid.NewGuid()

                let ft = {
                  Id = ftId |> UMX.tag
                  Text = $"- %d{int damage}"
                  Position = action.targetComponents.Position
                  Color = Damage
                  CreationTick = gameTime
                }


                AddObject(ftId, ActiveObject.FloatingText ft)

              | StaticMod(Multiplicative _)
              | StaticMod(Divisive _)
              | DynamicMod _ -> ()
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

        let! targetStats = rparams.derivedStats |> AMap.find targetId

        let clampedResources = {
          targetAfterEffects.Resources with
              HP = min targetStats.HP targetAfterEffects.Resources.HP
              MP = min targetStats.MP targetAfterEffects.Resources.MP
        }

        let targetAfterEffects = {
          targetAfterEffects with
              Resources = clampedResources
        }

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
            action.abilityDefinition.Cooldown

        let finalActor = {
          actorWithCooldown with
              Movement = {
                actorWithCooldown.Movement with
                    Path = []
                    Destination = ValueNone
              }
        }

        if actorId = targetId then
          let merged = {
            finalActor with
                Effects = targetAfterEffects.Effects
                Resources = targetAfterEffects.Resources
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
                    actorId, finalActor
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

        action.abilityDefinition.ProjectileIds
        |> Array.iter(fun defId ->
          let resolution = {
            Id = resolutionId
            ActorId = ractors.actor
            Target = EntityResolution ractors.target
            AbilityId = abilityId
            TriggerTick = gameTime + TimeSpan.FromSeconds(5.0) // Fallback timeout
          }

          let resGuid = UMX.untag resolutionId

          visualEffects.Add(AddObject(resGuid, PendingResolution resolution))

          let origin =
            match action.abilityDefinition.ProjectileOrigin with
            | ValueSome(FromTargetPoint offset) -> {
                X = action.targetComponents.Position.X + offset.X
                Y = action.targetComponents.Position.Y + offset.Y
              }
            | ValueSome FromCaster -> action.actorComponents.Position
            | ValueNone -> action.actorComponents.Position

          let projId = Guid.NewGuid()

          let proj = {
            Id = projId |> UMX.tag
            DefinitionId = defId
            CurrentPosition = origin
            Target = EntityTarget ractors.target
            CreationTick = gameTime
            PendingResolutionId = ValueSome resolutionId
          }

          visualEffects.Add(AddObject(projId, ActiveObject.Projectile proj)))

        action.abilityDefinition.AoeIds
        |> Array.iter(fun defId ->
          let resolution = {
            Id = resolutionId
            ActorId = ractors.actor
            Target = EntityResolution ractors.target
            AbilityId = abilityId
            TriggerTick = gameTime + TimeSpan.FromSeconds(0.5) // Example delay
          }

          let resGuid = UMX.untag resolutionId

          visualEffects.Add(AddObject(resGuid, PendingResolution resolution))

          let aoeGuid = Guid.NewGuid()

          let aoe: VisualEffects.ActiveAoe = {
            Id = aoeGuid |> UMX.tag<AoeId>
            DefinitionId = defId
            Position = action.targetComponents.Position
            CreationTick = gameTime
            PendingResolutionId = ValueSome resolutionId
          }

          visualEffects.Add(AddObject(aoeGuid, ActiveObject.Aoe aoe)))

        action.abilityDefinition.ImpactIds
        |> Array.iter(fun defId ->
          let impactDef = rparams.services.impactStore.find defId

          let resolution = {
            Id = resolutionId
            ActorId = ractors.actor
            Target = EntityResolution ractors.target
            AbilityId = abilityId
            TriggerTick = gameTime + impactDef.Duration
          }

          let resGuid = UMX.untag resolutionId

          visualEffects.Add(AddObject(resGuid, PendingResolution resolution))

          let impactId = Guid.NewGuid()

          let impact = {
            Id = impactId |> UMX.tag
            DefinitionId = defId
            Position = action.targetComponents.Position
            CreationTick = gameTime
            PendingResolutionId = ValueSome resolutionId
          }

          visualEffects.Add(AddObject(impactId, ActiveObject.Impact impact)))

        // Only apply cost and cooldown immediately
        let! actorWithCost =
          Resolution.applyResourceCost action.cost action.actorComponents 0 // No damage dealt yet

        let actorWithCooldown =
          Resolution.updateCooldowns
            actorWithCost
            abilityId
            gameTime
            action.abilityDefinition.Cooldown

        let finalActor = {
          actorWithCooldown with
              Movement = {
                actorWithCooldown.Movement with
                    Path = []
                    Destination = ValueNone
              }
        }

        return {
          StateChange.empty with
              updates = HashMap.single ractors.actor finalActor
              visualEffects = visualEffects |> ResizeArray.toArray
              audioChanges = audioChanges |> ResizeArray.toArray
        }
      }

    let resolveDeferredOnPosition
      (abilityId: int<AbilityId>)
      (rparams: ResolverParams)
      (actorId: Guid<EntityId>)
      (actorComponents: EntityComponents)
      (targetPosition: Position)
      (abilityDef: ActiveAbilityDefinition)
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
            actorId
            actorComponents.Position
            gameTime

        audioChanges.AddRange(castCues)

        abilityDef.ProjectileIds
        |> Array.iter(fun defId ->
          let resolution = {
            Id = resolutionId
            ActorId = actorId
            Target = PositionResolution targetPosition
            AbilityId = abilityId
            TriggerTick = gameTime + TimeSpan.FromSeconds(5.0) // Fallback timeout
          }

          let resGuid = UMX.untag resolutionId

          visualEffects.Add(AddObject(resGuid, PendingResolution resolution))

          let origin =
            match abilityDef.ProjectileOrigin with
            | ValueSome(FromTargetPoint offset) -> {
                X = targetPosition.X + offset.X
                Y = targetPosition.Y + offset.Y
              }
            | ValueSome FromCaster -> actorComponents.Position
            | ValueNone -> actorComponents.Position

          let projId = Guid.NewGuid()

          let proj = {
            Id = projId |> UMX.tag
            DefinitionId = defId
            CurrentPosition = origin
            Target = PositionTarget targetPosition
            CreationTick = gameTime
            PendingResolutionId = ValueSome resolutionId
          }

          visualEffects.Add(AddObject(projId, ActiveObject.Projectile proj)))

        // Only apply cost and cooldown immediately
        let! actorWithCost =
          Resolution.applyResourceCost abilityDef.Cost actorComponents 0 // No damage dealt yet

        let actorWithCooldown =
          Resolution.updateCooldowns
            actorWithCost
            abilityId
            gameTime
            abilityDef.Cooldown

        let finalActor = {
          actorWithCooldown with
              Movement = {
                actorWithCooldown.Movement with
                    Path = []
                    Destination = ValueNone
              }
        }

        return {
          StateChange.empty with
              updates = HashMap.single actorId finalActor
              visualEffects = visualEffects |> ResizeArray.toArray
              audioChanges = audioChanges |> ResizeArray.toArray
        }
      }

    let resolveAbilityOnPosition rparams abilityId actorId position = adaptive {
      let abilityKind = rparams.services.abilityStore.tryFind abilityId

      match abilityKind with
      | ValueSome(Active abilityDef) ->
        let! actor = rparams.scenarioState.entities |> AMap.find actorId

        return!
          resolveDeferredOnPosition
            abilityId
            rparams
            actorId
            actor
            position
            abilityDef
      | _ -> return StateChange.empty
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
    | MissingRequirements
    | OutOfRange ->
      let! gameTime = rparams.gameTime
      let! actor = rparams.scenarioState.entities |> AMap.tryFind ractors.actor

      let floatingText =
        actor
        |> Option.map(fun a ->
          let text =
            match validationResult with
            | Stunned -> "Stunned"
            | Silenced -> "Silenced"
            | OnCooldown -> "On Cooldown"
            | InsufficientResource -> "Not Enough Resources"
            | MissingRequirements -> "Requirements Not Met"
            | InvalidTarget -> "Invalid Target"
            | OutOfRange -> "Out of Range"
            | _ -> "Cannot Use"

          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text = text
            Position = a.Position
            Color = SystemMessage
            CreationTick = gameTime
          }

          AddObject(ftId, ActiveObject.FloatingText ft))
        |> Option.toArray

      return {
        StateChange.empty with
            visualEffects = floatingText
      }
    | TargetResourceFull ->
      let! gameTime = rparams.gameTime

      let! target =
        rparams.scenarioState.entities |> AMap.tryFind ractors.target

      let floatingText =
        target
        |> Option.map(fun t ->
          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text = "Resource is full"
            Position = t.Position
            Color = SystemMessage
            CreationTick = gameTime
          }

          AddObject(ftId, ActiveObject.FloatingText ft))
        |> Option.toArray

      return {
        StateChange.empty with
            visualEffects = floatingText
      }
    | ValidAction action ->
      let hasCastingTime = action.abilityDefinition.CastingTime.IsSome

      if hasCastingTime then
        return!
          AbilityResolution.resolveCasting abilityId rparams ractors action
      else
        let hasVisualEffect =
          action.abilityDefinition.ProjectileIds.Length > 0
          || action.abilityDefinition.AoeIds.Length > 0
          || action.abilityDefinition.ImpactIds.Length > 0

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

  let canTargetPredicate ability actor actorFactions parties canTargetParams =
    let {
          targetId = target
          targetFactions = targetFactions
        } =
      canTargetParams

    match ability.Intent with
    | Offensive ->
      Engagement.canTargetOffensive
        actor
        actorFactions
        target
        targetFactions
        parties
    | Support ->
      Engagement.canTargetSupport
        actor
        actorFactions
        target
        targetFactions
        parties
    | AbilityIntent.Neutral -> AVal.constant true

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
        let entities = rparams.scenarioState.entities

        let! actorFactions =
          entities
          |> AMap.tryFind action.actor
          |> AVal.map(
            Option.map _.Factions >> Option.defaultValue HashSet.empty
          )

        let canTargetPredicate =
          canTargetPredicate
            abilityDef
            action.actor
            actorFactions
            rparams.scenarioState.parties

        let! struct (actualTargets: ResolvedTarget[], primaryTargetPos) =
          match action.target with
          | AbilityTarget.PositionTarget pos ->
            let targets: aval<ResolvedTarget[]> =
              match abilityDef.Targeting with
              | GroundArea radius ->
                TargetResolution.getGroundTargets
                  entities
                  pos
                  radius
                  canTargetPredicate
                |> AVal.map(Array.map Entity)
              | GroundPoint -> AVal.constant [| Position pos |]
              | AreaRandomTargets(radius, maxTargets) ->
                TargetResolution.getAreaRandomTargets
                  entities
                  pos
                  radius
                  maxTargets
                  canTargetPredicate
                |> AVal.map(
                  Array.choose(fun id ->
                    match entities.TryGetValue id with
                    | Some e -> Some(Position e.Position)
                    | None -> None)
                )
              | AreaRandomPoints(radius, numPoints) ->
                TargetResolution.getAreaRandomPoints
                  pos
                  radius
                  numPoints
                  rparams.services.rng
                |> Array.map Position
                |> AVal.constant
              | Self
              | SingleAlly
              | SingleEnemy
              | ChainTargets _
              | ConeTargets _ -> AVal.constant Array.empty
              | StraightLine(range, width, maxTargets, _) ->
                  adaptive {
                    let! actor = entities |> AMap.find action.actor

                    let! targetsInLine =
                      TargetResolution.getStraightLineTargets
                        entities
                        actor.Position
                        pos
                        range
                        width
                        maxTargets
                        canTargetPredicate
                        action.actor

                    return targetsInLine |> Array.map Entity
                  }


            let pos = AVal.constant pos
            AVal.map2 (fun t p -> struct (t, p)) targets pos

          | EntityTargets targets ->
            let actualTargets: aval<ResolvedTarget[]> =
              match abilityDef.Targeting with
              | Self -> AVal.constant [| Entity action.actor |]
              | SingleAlly
              | SingleEnemy ->
                AVal.constant(
                  targets
                  |> Array.tryHead
                  |> Option.map(fun targetId -> [| Entity targetId |])
                  |> Option.defaultValue Array.empty
                )
              | GroundArea radius ->
                match targets |> Array.tryHead with
                | Some initialTarget ->
                  entities
                  |> AMap.find initialTarget
                  |> AVal.bind(fun e ->
                    TargetResolution.getGroundTargets
                      entities
                      e.Position
                      radius
                      canTargetPredicate)
                  |> AVal.map(Array.map Entity)
                | None -> AVal.constant Array.empty
              | GroundPoint ->
                match targets |> Array.tryHead with
                | Some initialTarget -> adaptive {
                    let! targetEntity = entities |> AMap.find initialTarget

                    let! canTarget =
                      canTargetPredicate {
                        targetId = initialTarget
                        targetFactions = targetEntity.Factions
                      }

                    if canTarget then
                      return [| Position targetEntity.Position |]
                    else
                      return Array.empty
                  }
                | None -> AVal.constant Array.empty
              | AreaRandomTargets(radius, maxTargets) ->
                match targets |> Array.tryHead with
                | Some initialTarget ->
                  entities
                  |> AMap.find initialTarget
                  |> AVal.bind(fun e ->
                    TargetResolution.getAreaRandomTargets
                      entities
                      e.Position
                      radius
                      maxTargets
                      canTargetPredicate)
                  |> AVal.map(Array.map Entity)
                | None -> AVal.constant Array.empty
              | ChainTargets(maxChains, range) ->
                match targets |> Array.tryHead with
                | Some initialTarget ->
                  TargetResolution.getChainTargets
                    entities
                    initialTarget
                    maxChains
                    range
                    canTargetPredicate
                  |> AVal.map(Array.map Entity)
                | None -> AVal.constant Array.empty
              | ConeTargets(angle, range, maxTargets) ->
                match targets |> Array.tryHead with
                | Some initialTarget ->
                  TargetResolution.getConeTargets
                    entities
                    action.actor
                    initialTarget
                    angle
                    range
                    maxTargets
                    canTargetPredicate
                  |> AVal.map(Array.map Entity)
                | None -> AVal.constant Array.empty
              | AreaRandomPoints _ -> AVal.constant Array.empty

              | StraightLine(width, effectiveRange, maxTargets, _) ->
                AVal.constant Array.empty

            let primaryPos = adaptive {
              match targets |> Array.tryHead with
              | None ->
                let! actor = entities |> AMap.find action.actor
                return actor.Position
              | Some headId ->
                let! headEntity = entities |> AMap.find headId
                return headEntity.Position
            }

            AVal.map2 (fun t p -> struct (t, p)) actualTargets primaryPos

        let! actor = rparams.scenarioState.entities |> AMap.tryFind action.actor

        match actor with
        | None -> return StateChange.empty
        | Some actor when not actor.Resources.Status.IsAlive ->
          return StateChange.empty
        | Some actor ->

        let! actorStats = rparams.derivedStats |> AMap.find action.actor
        let! gameTime = rparams.gameTime

        let isStunned = ValidateAction.checkStun actor
        let isSilenced = ValidateAction.checkSilence actor abilityDef

        let! isOnCooldown =
          ValidateAction.checkCooldown actor action.abilityId rparams.gameTime

        let struct (hasEnoughResource, _) =
          ValidateAction.checkResourceCost actor abilityDef

        let hasRequirements =
          ValidateAction.checkAbilityRequirements
            actor
            actorStats
            abilityDef.Requirements

        let inRange =
          ValidateAction.checkRange
            actor.Position
            primaryTargetPos
            abilityDef.Range

        if
          isStunned
          || isSilenced
          || isOnCooldown
          || not hasEnoughResource
          || not hasRequirements
          || not inRange
        then
          let text =
            if isStunned then "Stunned"
            elif isSilenced then "Silenced"
            elif isOnCooldown then "On Cooldown"
            elif not hasEnoughResource then "Not Enough Resources"
            elif not inRange then "Out of Range"
            else "Requirements Not Met"

          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text = text
            Position = actor.Position
            Color = SystemMessage
            CreationTick = gameTime
          }

          return {
            StateChange.empty with
                visualEffects = [|
                  AddObject(ftId, ActiveObject.FloatingText ft)
                |]
          }
        else
          let stateChanges =
            actualTargets
            |> AList.ofArray
            |> AList.mapA(fun resolvedTarget ->
              match resolvedTarget with
              | Entity targetId ->
                let resolver = resolveAbility rparams action.abilityId

                resolver {
                  actor = action.actor
                  target = targetId
                }
              | Position pos ->
                AbilityResolution.resolveAbilityOnPosition
                  rparams
                  action.abilityId
                  action.actor
                  pos)

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

  let resolveAddEntitiesWithAI
    (resolverParams: ResolverParams)
    (entitiesToAddWithAI: HashMap<Guid<EntityId>, _>)
    : aval<StateChange> =
    adaptive {
      let! gameTime = resolverParams.gameTime

      let struct (additions, aiControllers) =
        entitiesToAddWithAI
        |> HashMap.fold
          (fun struct (adds, controllers) entityId spawnData ->
            let newAdds = HashMap.add entityId spawnData.components adds

            let newControllers =
              match spawnData.archetypeId with
              | ValueSome archetypeId ->
                match
                  resolverParams.services.aiArchetypeStore.tryFind archetypeId
                with
                | ValueSome archetype ->
                  let controller =
                    EnemyAI.AILifecycle.createController
                      entityId
                      archetypeId
                      spawnData.components.Position
                      archetype.patrolWaypoints
                      gameTime

                  HashMap.add entityId controller controllers
                | ValueNone -> controllers
              | ValueNone -> controllers

            struct (newAdds, newControllers))
          struct (HashMap.empty, HashMap.empty)

      return {
        StateChange.empty with
            additions = additions
            aiControllers = aiControllers
      }
    }

  let resolveUseItem
    (scenarioState: ScenarioState)
    (action: UseItemAction)
    (resolverParams: ResolverParams)
    =
    adaptive {
      let! actor = scenarioState.entities |> AMap.tryFind action.actor

      match actor with
      | None -> return StateChange.empty
      | Some actor ->
        let useResult =
          Inventory.useItem resolverParams.services.itemStore action actor

        match useResult with
        | Ok useOutput ->
          let! abilityStateChange =
            resolveAbility resolverParams useOutput.AbilityId {
              actor = action.actor
              target = action.actor
            }

          return {
            abilityStateChange with
                updates =
                  abilityStateChange.updates
                  |> HashMap.alterV action.actor (fun components ->
                    // if components are not present, do not add them
                    components
                    |> ValueOption.map(fun components -> {
                      components with
                          // in this particular case we know useItem only updates Inventory
                          // otherwise this merge operation would be tricky
                          Inventory = useOutput.UpdatedComponents.Inventory
                    }))

          }
        | Error e ->
          // TODO: Create floating text for error
          return StateChange.empty
    }

  let resolveEquipItem
    (scenarioState: ScenarioState)
    (action: EquipItemAction)
    (resolverParams: ResolverParams)
    =
    adaptive {
      let! actor = scenarioState.entities |> AMap.tryFind action.actor

      match actor with
      | None -> return StateChange.empty
      | Some actor ->
        let equipResult =
          Equip.equipItem resolverParams.services.itemStore action actor

        match equipResult with
        | Ok updatedActor ->
          return {
            StateChange.empty with
                updates = HashMap.single action.actor updatedActor
          }
        | Error _ ->
          // TODO: Create floating text for error
          return StateChange.empty
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
    | AddEntitiesWithAI entitiesToAddWithAI ->
      return! resolveAddEntitiesWithAI resolverParams entitiesToAddWithAI
    | UseItem action ->
      return! resolveUseItem scenarioState action resolverParams
    | EquipItem action ->
      return! resolveEquipItem scenarioState action resolverParams
    | UnequipItem action ->
      let! actor = scenarioState.entities |> AMap.tryFind action.actor

      match actor with
      | None -> return StateChange.empty
      | Some actor ->
        let updatedActor = Equip.unequipItem action actor

        return {
          StateChange.empty with
              updates = HashMap.single action.actor updatedActor
        }
  }
