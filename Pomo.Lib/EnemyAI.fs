namespace Pomo.Lib.EnemyAI

open System
open System.Diagnostics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.AI
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.Attributes

module Perception =
  let inline distance (p1: Position) (p2: Position) =
    let dx = p1.X - p2.X
    let dy = p1.Y - p2.Y
    sqrt(dx * dx + dy * dy)

  let inline isHostileFaction
    (controllerFactions: Classification.Faction HashSet)
    (targetFactions: Classification.Faction HashSet)
    =
    let isEnemy = controllerFactions.Contains Classification.Faction.Enemy
    let isAlly = controllerFactions.Contains Classification.Faction.Ally
    let targetIsPlayer = targetFactions.Contains Classification.Faction.Player
    let targetIsAlly = targetFactions.Contains Classification.Faction.Ally
    let targetIsEnemy = targetFactions.Contains Classification.Faction.Enemy
    isEnemy && (targetIsPlayer || targetIsAlly) || isAlly && targetIsEnemy

  let gatherVisualCues
    (controllerPos: Position)
    (controllerFactions: Classification.Faction HashSet)
    (config: PerceptionConfig)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (currentTick: TimeSpan)
    =
    adaptive {
      let cues =
        entities
        |> AMap.choose(fun entityId entity ->
          let dist = distance controllerPos entity.Position

          if
            dist <= config.visualRange
            && isHostileFaction controllerFactions entity.Factions
          then
            let strength =
              if dist < config.visualRange * 0.3f then Overwhelming
              elif dist < config.visualRange * 0.6f then Strong
              elif dist < config.visualRange * 0.8f then Moderate
              else Weak

            Some {
              cueType = Visual
              strength = strength
              sourceEntityId = ValueSome entityId
              position = entity.Position
              timestamp = currentTick
            }
          else
            None)

      let! gatheredCues =
        cues
        |> AMap.fold (fun acc _ cue -> ResizeArray.add cue acc) (ResizeArray())

      return gatheredCues.ToArray()
    }

  let decayMemories
    (memories: HashMap<Guid<EntityId>, MemoryEntry>)
    (currentTick: TimeSpan)
    (memoryDuration: TimeSpan)
    : HashMap<Guid<EntityId>, MemoryEntry> =
    memories
    |> HashMap.filter(fun _ entry ->
      currentTick - entry.lastSeenTick < memoryDuration)

  let gatherCues
    (controller: AIController)
    (archetype: AIArchetype)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (controllerEntity: EntityComponents)
    (currentTick: TimeSpan aval)
    =
    adaptive {
      let! currentTick = currentTick

      let! visualCues =
        gatherVisualCues
          controllerEntity.Position
          controllerEntity.Factions
          archetype.perceptionConfig
          entities
          currentTick

      let decayedMemories =
        decayMemories
          controller.memories
          currentTick
          archetype.perceptionConfig.memoryDuration

      let updatedMemories =
        visualCues
        |> Array.fold
          (fun (mem: HashMap<Guid<EntityId>, MemoryEntry>) (cue: PerceptionCue) ->
            match cue.sourceEntityId with
            | ValueSome entityId ->
              let confidence =
                match cue.strength with
                | Weak -> 0.25f
                | Moderate -> 0.5f
                | Strong -> 0.75f
                | Overwhelming -> 1.0f

              let entry = {
                entityId = entityId
                lastSeenTick = currentTick
                lastKnownPosition = cue.position
                confidence = confidence
              }

              mem.Add(entityId, entry)
            | ValueNone -> mem)
          decayedMemories

      let memoryCues =
        updatedMemories
        |> HashMap.toArray
        |> Array.map(fun (entityId, memoryEntry) ->
          let strength =
            if memoryEntry.confidence >= 1.0f then Overwhelming
            elif memoryEntry.confidence >= 0.75f then Strong
            elif memoryEntry.confidence >= 0.5f then Moderate
            else Weak

          {
            cueType = Memory
            strength = strength
            sourceEntityId = ValueSome entityId
            position = memoryEntry.lastKnownPosition
            timestamp = memoryEntry.lastSeenTick
          })

      let allCues = Array.concat [ visualCues; memoryCues ]

      return struct (allCues, updatedMemories)
    }

module Decision =
  open Abilities

  let matchCueToPriority (cue: PerceptionCue) (priorities: CuePriority[]) =
    priorities
    |> Array.tryFind(fun p ->
      p.cueType = cue.cueType && cue.strength >= p.minStrength)

  let selectBestCue (cues: PerceptionCue[]) (priorities: CuePriority[]) =
    cues
    |> Array.choose(fun cue ->
      matchCueToPriority cue priorities
      |> Option.map(fun priority -> struct (cue, priority)))
    |> Array.sortBy(fun struct (_, priority) -> priority.priority)
    |> Array.tryHead

  let selectAbilityForTarget
    (abilityStore: Services.IAbilityStore)
    (entityPosition: Position)
    (targetId: Guid<EntityId> voption)
    (targetPos: Position)
    (abilities: int<AbilityId> HashSet)
    =
    abilities
    |> HashSet.chooseV(fun abilityId ->

      match abilityStore.tryFind abilityId with
      | ValueNone -> ValueNone
      | ValueSome(Passive _) -> ValueNone
      | ValueSome(Active def) ->
        let dist = Perception.distance entityPosition targetPos

        if dist <= def.Range then
          match def.Targeting with
          | Self -> ValueSome struct (abilityId, EntityTargets [||])
          | SingleEnemy
          | SingleAlly ->
            targetId
            |> ValueOption.map(fun id ->
              struct (abilityId, EntityTargets [| id |]))
          | GroundArea _
          | GroundPoint
          | AreaRandomTargets _
          | AreaRandomPoints _ ->
            ValueSome(struct (abilityId, PositionTarget targetPos))
          | ChainTargets _
          | ConeTargets _ ->
            targetId
            |> ValueOption.map(fun id ->
              struct (abilityId, EntityTargets [| id |]))
        else
          ValueNone)

  let generateCommand
    (cue: PerceptionCue)
    (priority: CuePriority)
    (controller: AIController)
    (entity: EntityComponents)
    (abilityStore: Services.IAbilityStore)
    =
    match priority.response with
    | Investigate ->
      ValueSome(
        Navigate {
          actor = controller.controlledEntityId
          destination = cue.position
        }
      )
    | Engage ->
      let abilities =
        entity.Abilities
        |> selectAbilityForTarget
          abilityStore
          entity.Position
          cue.sourceEntityId
          cue.position
        |> HashSet.toArray

      match abilities with
      | [||] ->
        ValueSome(
          Navigate {
            actor = controller.controlledEntityId
            destination = cue.position
          }
        )
      | abilities ->
        let struct (abilityId, target) = abilities |> Array.randomChoice

        ValueSome(
          UseAbility {
            actor = controller.controlledEntityId
            target = target
            abilityId = abilityId
          }
        )
    | Evade ->
      ValueSome(
        Navigate {
          actor = controller.controlledEntityId
          destination = {
            X = controller.spawnPosition.X
            Y = controller.spawnPosition.Y
          }
        }
      )
    | Flee -> ValueNone
    | Ignore -> ValueNone


module AILifecycle =
  let createController
    (entityId: Guid<EntityId>)
    (archetypeId: int<AiArchetypeId>)
    (spawnPosition: Position)
    (relativeWaypoints: Position[] voption)
    (currentTime: TimeSpan)
    : AIController =
    let absoluteWaypoints =
      relativeWaypoints
      |> ValueOption.map(fun waypoints ->
        waypoints
        |> Array.map(fun offset -> {
          X = spawnPosition.X + offset.X
          Y = spawnPosition.Y + offset.Y
        }))

    {
      controlledEntityId = entityId
      archetypeId = archetypeId
      currentState = Idle
      currentTarget = ValueNone
      lastDecisionTime = currentTime
      memories = HashMap.empty
      waypointIndex = 0
      stateEnterTime = currentTime
      spawnPosition = spawnPosition
      absoluteWaypoints = absoluteWaypoints
    }

  let cleanupDeadControllers
    (entities: cmap<Guid<EntityId>, EntityComponents>)
    (controllers: cmap<Guid<EntityId>, AIController>)
    =
    transact(fun _ ->
      for entityId, components in entities do
        if components.Resources.Status.IsDead then
          controllers.Remove entityId |> ignore)

module AISystem =
  let selectNextWaypoint
    (behaviorType: BehaviorType)
    (controller: AIController)
    (entity: EntityComponents)
    (waypoints: Position[])
    =
    match behaviorType with
    | Patrol ->
      // Sequential patrol - cycle through waypoints in order
      let currentIdx = controller.waypointIndex % waypoints.Length
      let targetWaypoint = waypoints[currentIdx]

      let dx = entity.Position.X - targetWaypoint.X
      let dy = entity.Position.Y - targetWaypoint.Y
      let dist = sqrt(dx * dx + dy * dy)
      let hasReached = dist < 64f

      if hasReached then
        // Move to next waypoint when current is reached
        let nextIdx = (controller.waypointIndex + 1) % waypoints.Length
        struct (waypoints[nextIdx], nextIdx)
      else
        // Keep current waypoint and index
        struct (targetWaypoint, currentIdx)

    | Aggressive ->
      // Random waypoint selection - keeps enemies unpredictable
      if Array.isEmpty waypoints then
        struct (controller.spawnPosition, controller.waypointIndex)
      else
        let targetWaypoint = waypoints |> Array.randomChoice
        struct (targetWaypoint, controller.waypointIndex)
    | Defensive
    | Supporter ->
      // Pick closest waypoint to spawn (defensive position)
      let targetWaypoint =
        waypoints
        |> Array.minBy(fun wp ->
          let dx = controller.spawnPosition.X - wp.X
          let dy = controller.spawnPosition.Y - wp.Y
          sqrt(dx * dx + dy * dy))

      struct (targetWaypoint, controller.waypointIndex)

    | Ambusher ->
      // Stay at spawn, or pick random hiding spots
      let targetWaypoint =
        if controller.waypointIndex = 0 then
          controller.spawnPosition
        else
          waypoints |> Array.randomChoice

      struct (targetWaypoint, controller.waypointIndex)
    | Turret ->
      // Never move from spawn
      struct (controller.spawnPosition, controller.waypointIndex)
    | Passive ->
      // Wander randomly between waypoints
      let targetWaypoint = waypoints |> Array.randomChoice
      struct (targetWaypoint, controller.waypointIndex)


  let processAndGenerateCommands
    (controller: AIController)
    (archetype: AIArchetype)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (abilityStore: Services.IAbilityStore)
    (currentTick: TimeSpan aval)
    =
    adaptive {
      let! controllerEntity =
        entities |> AMap.tryFind controller.controlledEntityId

      match controllerEntity with
      | None -> return struct (controller, ValueNone)
      | Some entity ->
        let! struct (cues, updatedMemories) =
          Perception.gatherCues controller archetype entities entity currentTick

        let! currentTick = currentTick
        let timeSinceLastDecision = currentTick - controller.lastDecisionTime

        let struct (command, shouldUpdateTime, newWaypointIndex) =
          if timeSinceLastDecision >= archetype.decisionInterval then
            let bestCue = Decision.selectBestCue cues archetype.cuePriorities

            match bestCue with
            | Some struct (cue, priority) ->
              let cmd =
                Decision.generateCommand
                  cue
                  priority
                  controller
                  entity
                  abilityStore

              struct (cmd, true, controller.waypointIndex)
            | None ->
              let navigateSpawn =
                ValueSome(
                  Navigate {
                    actor = controller.controlledEntityId
                    destination = controller.spawnPosition
                  }
                )

              match controller.absoluteWaypoints with
              | ValueNone
              | ValueSome [||] ->
                match archetype.behaviorType with
                | Patrol -> struct (ValueNone, true, controller.waypointIndex)
                | Aggressive
                | Defensive
                | Supporter
                | Ambusher
                | Turret
                | Passive ->
                  struct (navigateSpawn, true, controller.waypointIndex)
              | ValueSome waypoints ->
                match archetype.behaviorType with
                | Patrol ->
                  let struct (targetWaypoint, nextIdx) =
                    selectNextWaypoint
                      archetype.behaviorType
                      controller
                      entity
                      waypoints

                  let cmd =
                    ValueSome(
                      Navigate {
                        actor = controller.controlledEntityId
                        destination = targetWaypoint
                      }
                    )

                  struct (cmd, true, nextIdx)
                | Aggressive ->
                  let targetWaypoint = waypoints |> Array.randomChoice

                  let cmd =
                    ValueSome(
                      Navigate {
                        actor = controller.controlledEntityId
                        destination = targetWaypoint
                      }
                    )

                  struct (cmd, true, controller.waypointIndex)
                | Defensive
                | Supporter
                | Ambusher
                | Passive ->
                  struct (navigateSpawn, true, controller.waypointIndex)
                | Turret -> struct (ValueNone, true, controller.waypointIndex)
          else
            struct (ValueNone, false, controller.waypointIndex)

        let updatedController = {
          controller with
              memories = updatedMemories
              waypointIndex = newWaypointIndex
              lastDecisionTime =
                if shouldUpdateTime then
                  currentTick
                else
                  controller.lastDecisionTime
        }

        return struct (updatedController, command)
    }

  let processAllControllersAndCommands
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (archetypeStore: Services.IAIArchetypeStore)
    (abilityStore: Services.IAbilityStore)
    (currentTick: TimeSpan aval)
    (controllers: amap<Guid<EntityId>, AIController>)
    : aval<struct (HashMap<Guid<EntityId>, AIController> * Command[])> =
    adaptive {
      let results =
        controllers
        |> AMap.mapA(fun _ controller -> adaptive {
          let found = archetypeStore.tryFind controller.archetypeId

          match found with
          | ValueNone -> return struct (controller, ValueNone)
          | ValueSome archetype ->
            return!
              processAndGenerateCommands
                controller
                archetype
                entities
                abilityStore
                currentTick
        })


      let! struct (updatedControllers, commands) =
        results
        |> AMap.fold
          (fun struct (accCtrls, accCmds) _ struct (ctrl, cmd) ->
            match cmd with
            | ValueNone ->
              struct (HashMap.add ctrl.controlledEntityId ctrl accCtrls,
                      accCmds)
            | ValueSome command ->

            struct (HashMap.add ctrl.controlledEntityId ctrl accCtrls,
                    ResizeArray.add command accCmds))
          (HashMap.empty, ResizeArray.empty())

      return struct (updatedControllers, commands.ToArray())
    }
