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

  let gatherVisualCues
    (controllerPos: Position)
    (config: PerceptionConfig)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (currentTick: TimeSpan)
    =
    adaptive {
      let cues =
        entities
        |> AMap.choose(fun entityId entity ->
          let dist = distance controllerPos entity.Position

          if dist <= config.visualRange then
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
          archetype.perceptionConfig
          entities
          currentTick

      let updatedMemories =
        decayMemories
          controller.memories
          currentTick
          archetype.perceptionConfig.memoryDuration

      return struct (visualCues, updatedMemories)
    }

module Decision =
  let matchCueToPriority (cue: PerceptionCue) (priorities: CuePriority[]) =
    priorities
    |> Array.tryFind(fun p ->
      p.cueType = cue.cueType
      && (cue.strength = p.minStrength
          || cue.strength = Strong
          || cue.strength = Overwhelming))

  let selectBestCue (cues: PerceptionCue[]) (priorities: CuePriority[]) =
    cues
    |> Array.choose(fun cue ->
      matchCueToPriority cue priorities
      |> Option.map(fun priority -> struct (cue, priority)))
    |> Array.sortBy(fun struct (_, priority) -> priority.priority)
    |> Array.tryHead

  let generateCommand
    (cue: PerceptionCue)
    (priority: CuePriority)
    (controller: AIController)
    (entity: EntityComponents)
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
      match cue.sourceEntityId with
      | ValueSome targetId ->
        let abilities = entity.Abilities |> HashSet.toArray

        if Array.isEmpty abilities then
          ValueNone
        else
          let selected = abilities |> Array.randomChoice

          ValueSome(
            UseAbility {
              actor = controller.controlledEntityId
              target = EntityTargets [| targetId |]
              abilityId = selected
            }
          )
      | ValueNone -> ValueNone
    | Evade -> ValueNone
    | Flee -> ValueNone
    | Ignore -> ValueNone


module AILifecycle =
  let createController
    (entityId: Guid<EntityId>)
    (archetypeId: int<AiArchetypeId>)
    (currentTime: TimeSpan)
    : AIController =
    {
      controlledEntityId = entityId
      archetypeId = archetypeId
      currentState = Idle
      currentTarget = ValueNone
      lastDecisionTime = currentTime
      memories = HashMap.empty
      waypointIndex = 0
      stateEnterTime = currentTime
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

  let processAndGenerateCommands
    (controller: AIController)
    (archetype: AIArchetype)
    (entities: amap<Guid<EntityId>, EntityComponents>)
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

        let struct (command, shouldUpdateTime) =
          if timeSinceLastDecision >= archetype.decisionInterval then

            let bestCue = Decision.selectBestCue cues archetype.cuePriorities

            let cmd =
              match bestCue with
              | None -> ValueNone
              | Some struct (cue, priority) ->

                Decision.generateCommand cue priority controller entity

            struct (cmd, true)
          else
            struct (ValueNone, false)

        let updatedController = {
          controller with
              memories = updatedMemories
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
