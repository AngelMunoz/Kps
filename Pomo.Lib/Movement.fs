namespace Pomo.Lib.Movement

open System
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Scenario
open Pomo.Lib.Collision
open Pomo.Lib.Pathfinding


module Utils =
  let inline radiusOfStage(s: Stage) =
    match s with
    | Stage.First -> 12f
    | Stage.Second -> 16f
    | Stage.Third -> 20f

  // Check if a path would cause a sudden jump from current position
  let isPathContinuous
    (currentPos: Position)
    (path: Position list)
    (maxJumpDistance: float32)
    =
    match path with
    | [] -> true
    | firstWaypoint :: _ ->
      let dx = firstWaypoint.X - currentPos.X
      let dy = firstWaypoint.Y - currentPos.Y
      let dist = sqrt(dx * dx + dy * dy)
      dist <= maxJumpDistance


module TerrainMovement =
  let getTerrainSpeedModifier
    (pos: Position)
    (scenario: Scenario)
    (radius: float32)
    : float32 =
    let terrainObjects = Query.queryTerrainObjects pos radius scenario

    terrainObjects
    |> Array.tryHead
    |> function
      | Some obj ->
        match obj.TerrainType with
        | Water -> 0.5f
        | Hazard -> 0.7f
        | _ -> 1.0f
      | None -> 1.0f

  let applyDexterityModifier (baseSpeed: float32) (dexterity: int) : float32 =
    let dexModifier = 1.0f + (float32 dexterity - 10f) * 0.05f
    baseSpeed * max 0.1f dexModifier

module PathMovement =
  let calculatePath
    (scenario: Scenario)
    (start: Position)
    (goal: Position)
    (entityRadius: float32)
    : Position[] voption =
    // Use larger cell size and account for entity radius in collision detection
    let cellSize = max 24.0f (entityRadius * 2.5f) // Ensure cells are large enough for the entity

    let grid =
      Grid.createWithEntities
        scenario
        cellSize
        entityRadius
        [||]
        (Guid.Empty |> UMX.tag<EntityId>)

    AStar.findPath grid start goal

  let calculatePathWithEntities
    (scenario: Scenario)
    (start: Position)
    (goal: Position)
    (entityRadius: float32)
    (allEntities: struct (Guid<EntityId> * EntityComponents) array)
    (excludeEntityId: Guid<EntityId>)
    : Position[] voption =
    // Use larger cell size and account for entity radius in collision detection
    let cellSize = max 20.0f (entityRadius * 2.0f) // Smaller cells for better entity avoidance

    let grid =
      Grid.createWithEntities
        scenario
        cellSize
        entityRadius
        allEntities
        excludeEntityId

    AStar.findPath grid start goal

  let getNextWaypoint
    (currentPos: Position)
    (path: Position list)
    (entityRadius: float32)
    =
    match path with
    | [] -> struct (ValueNone, [])
    | next :: remaining ->
      let dx = next.X - currentPos.X
      let dy = next.Y - currentPos.Y
      let dist = sqrt(dx * dx + dy * dy)

      // Use dynamic waypoint tolerance based on entity radius and a minimum distance
      let tolerance = max 20.0f (entityRadius * 1.5f)

      if dist <= tolerance then
        match remaining with
        | [] -> struct (ValueNone, [])
        | nextNext :: _ -> struct (ValueSome nextNext, remaining)
      else
        struct (ValueSome next, path)

module PathfindingCommands =

  let setDestinationWithPathfinding
    (scenario: Scenario)
    (start: Position)
    (destination: Position)
    (entityRadius: float32)
    (movement: Movement)
    : Movement =

    match
      PathMovement.calculatePath scenario start destination entityRadius
    with
    | ValueSome path when path.Length > 1 -> {
        movement with
            Destination = ValueSome destination
            Path = List.ofArray path.[1..] // Skip first position (current position)
      }
    | _ ->
        {
          movement with
              Destination = ValueSome destination
              Path = []
        }

  let setDestinationWithEntities
    (scenario: Scenario)
    (start: Position)
    (destination: Position)
    (entityRadius: float32)
    (allEntities: struct (Guid<EntityId> * EntityComponents) array)
    (excludeEntityId: Guid<EntityId>)
    (movement: Movement)
    : Movement =
    let calculated =
      PathMovement.calculatePathWithEntities
        scenario
        start
        destination
        entityRadius
        allEntities
        excludeEntityId
      |> ValueOption.orElseWith(fun _ ->
        PathMovement.calculatePath scenario start destination entityRadius)

    match calculated with
    | ValueSome path when path.Length > 1 ->
      let pathList = List.ofArray path.[1..] // Skip first position (current position)

      {
        movement with
            Destination = ValueSome destination
            Path = pathList
      }
    | _ ->
        {
          movement with
              Destination = ValueSome destination
              Path = []
        }

module Update =

  [<Struct>]
  type MoveTowardsArgs = {
    Position: Position
    Destination: Position
    Speed: float32
    Elapsed: float32
    Scenario: Scenario
    EntityRadius: float32
  }

  let private moveTowards args =
    let {
          Position = position
          Destination = destination
          Speed = speed
          Elapsed = elapsed
          Scenario = scenario
          EntityRadius = entityRadius
        } =
      args

    let dx = destination.X - position.X
    let dy = destination.Y - position.Y
    let dist = sqrt(dx * dx + dy * dy)

    let terrainModifier =
      TerrainMovement.getTerrainSpeedModifier position scenario entityRadius

    let adjustedSpeed = speed * terrainModifier
    let moveAmount = adjustedSpeed * elapsed

    if dist <= moveAmount then
      struct (destination, true)
    else
      let newX = position.X + dx / dist * moveAmount
      let newY = position.Y + dy / dist * moveAmount
      struct ({ X = newX; Y = newY }, false)

  let private checkEntityCollision
    (pos: Position)
    (entityId: Guid<EntityId>)
    (entityRadius: float32)
    (entitiesArray: struct (Guid<EntityId> * EntityComponents) array)
    : bool =
    let mutable collision = false

    for struct (id, other) in entitiesArray do
      if id <> entityId && not collision then
        let otherR = Utils.radiusOfStage other.Identity.Stage
        let dx = pos.X - other.Position.X
        let dy = pos.Y - other.Position.Y
        let dist2 = dx * dx + dy * dy
        let rad = entityRadius + otherR

        if dist2 < rad * rad then
          collision <- true

    collision

  [<Struct>]
  type MovementResult =
    | ContinueMoving of Position * Movement
    | StopMoving of Position * Movement
    | RecalculatePath of Movement
    | ClearPath of Movement

  let private handlePathRecalculation
    (scenario: Scenario)
    (currentPos: Position)
    (finalDest: Position voption)
    (entityRadius: float32)
    (entitiesArray: struct (Guid<EntityId> * EntityComponents) array)
    (entityId: Guid<EntityId>)
    (components: EntityComponents)
    : MovementResult =
    match finalDest with
    | ValueSome dest ->
      let pathResult =
        PathMovement.calculatePathWithEntities
          scenario
          currentPos
          dest
          entityRadius
          entitiesArray
          entityId
        |> ValueOption.orElseWith(fun _ ->
          PathMovement.calculatePath scenario currentPos dest entityRadius)

      match pathResult with
      | ValueSome path when path.Length > 1 ->
        RecalculatePath {
          components.Movement with
              Path = Array.toList path.[1..]
        }
      | _ ->
        ClearPath {
          components.Movement with
              Path = []
              Destination = ValueNone
        }
    | ValueNone -> ClearPath { components.Movement with Path = [] }

  let private processMovementCollisions
    (clampedPos: Position)
    (entityId: Guid<EntityId>)
    (entityRadius: float32)
    (entitiesArray: struct (Guid<EntityId> * EntityComponents) array)
    (scenario: Scenario)
    (components: EntityComponents)
    (arrived: bool)
    (remainingPath: Position list)
    : MovementResult =
    let entityCollision =
      checkEntityCollision clampedPos entityId entityRadius entitiesArray

    let terrainBlocked = not(Query.canMoveTo clampedPos entityRadius scenario)

    if entityCollision then
      handlePathRecalculation
        scenario
        components.Position
        components.Movement.Destination
        entityRadius
        entitiesArray
        entityId
        components
    elif terrainBlocked then
      handlePathRecalculation
        scenario
        components.Position
        components.Movement.Destination
        entityRadius
        entitiesArray
        entityId
        components
    elif arrived && remainingPath.IsEmpty then
      let finalMovement = {
        components.Movement with
            Path = []
            Destination = ValueNone
      }

      StopMoving(clampedPos, finalMovement)
    else
      let continueMovement = {
        components.Movement with
            Path = remainingPath
      }

      ContinueMoving(clampedPos, continueMovement)

  let private clampToBounds
    (pos: Position)
    (bounds: ScenarioBounds)
    : Position =
    let halfW = bounds.Width * 0.5f
    let halfH = bounds.Height * 0.5f
    let minX = bounds.CenterX - halfW
    let maxX = bounds.CenterX + halfW
    let minY = bounds.CenterY - halfH
    let maxY = bounds.CenterY + halfH

    let clampedX =
      if pos.X < minX then minX
      elif pos.X > maxX then maxX
      else pos.X

    let clampedY =
      if pos.Y < minY then minY
      elif pos.Y > maxY then maxY
      else pos.Y

    { X = clampedX; Y = clampedY }

  let updateEntityWithContext
    (time: TimeSpan)
    (bounds: ScenarioBounds)
    (scenario: Scenario)
    (allEntities: HashMap<Guid<EntityId>, EntityComponents>)
    (entityId: Guid<EntityId>)
    (components: EntityComponents)
    =
    let entityRadius = Utils.radiusOfStage components.Identity.Stage
    let elapsedSeconds = time / TimeSpan.FromSeconds 1.0 |> float32
    let entitiesArray = allEntities |> HashMap.toArrayV

    // Handle pathfinding if we have a path
    match components.Movement.Path with
    | [] ->
      // No path - handle direct movement to destination (fallback)
      match components.Movement.Destination with
      | ValueSome dest ->
        let adjustedSpeed =
          TerrainMovement.applyDexterityModifier components.Movement.Speed 10

        let struct (proposedPos, arrived) =
          moveTowards {
            Position = components.Position
            Destination = dest
            Speed = adjustedSpeed
            Elapsed = elapsedSeconds
            Scenario = scenario
            EntityRadius = entityRadius
          }

        let clampedPos = clampToBounds proposedPos bounds

        let entityCollision =
          checkEntityCollision clampedPos entityId entityRadius entitiesArray

        let terrainBlocked =
          not(Query.canMoveTo clampedPos entityRadius scenario)

        if entityCollision || terrainBlocked then
          {
            components with
                Movement = {
                  components.Movement with
                      Destination = ValueNone
                }
          }
        elif arrived then
          {
            components with
                Position = clampedPos
                Movement = {
                  components.Movement with
                      Destination = ValueNone
                }
          }
        else
          {
            components with
                Position = clampedPos
          }
      | ValueNone -> components
    | currentPath ->
      let struct (nextWaypoint, remainingPath) =
        PathMovement.getNextWaypoint
          components.Position
          currentPath
          entityRadius

      match nextWaypoint with
      | ValueSome waypoint ->
        let adjustedSpeed =
          TerrainMovement.applyDexterityModifier components.Movement.Speed 10

        let struct (proposedPos, arrived) =
          moveTowards {
            Position = components.Position
            Destination = waypoint
            Speed = adjustedSpeed
            Elapsed = elapsedSeconds
            Scenario = scenario
            EntityRadius = entityRadius
          }

        let clampedPos = clampToBounds proposedPos bounds

        match
          processMovementCollisions
            clampedPos
            entityId
            entityRadius
            entitiesArray
            scenario
            components
            arrived
            remainingPath
        with
        | ContinueMoving(pos, movement) -> {
            components with
                Position = pos
                Movement = movement
          }
        | StopMoving(pos, movement) -> {
            components with
                Position = pos
                Movement = movement
          }
        | RecalculatePath movement -> { components with Movement = movement }
        | ClearPath movement -> { components with Movement = movement }

      | ValueNone ->
          {
            components with
                Movement = {
                  components.Movement with
                      Path = []
                      Destination = ValueNone
                }
          }
