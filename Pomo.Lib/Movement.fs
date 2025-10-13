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

module TerrainMovement =
  let getTerrainSpeedModifier (pos: Position) (scenario: Scenario) (radius: float32) : float32 =
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
    baseSpeed * (max 0.1f dexModifier)

module PathMovement =
  let calculatePath (scenario: Scenario) (start: Position) (goal: Position) : Position[] voption =
    let grid = Grid.create scenario 32.0f
    AStar.findPath grid start goal

  let getNextWaypoint (currentPos: Position) (path: Position list) : Position voption * Position list =
    match path with
    | [] -> ValueNone, []
    | next :: remaining ->
      let dx = next.X - currentPos.X
      let dy = next.Y - currentPos.Y
      let dist = sqrt(dx * dx + dy * dy)
      
      if dist <= 16.0f then
        match remaining with
        | [] -> ValueNone, []
        | nextNext :: _ -> ValueSome nextNext, remaining
      else
        ValueSome next, path

module PathfindingCommands =
  let setDestinationWithPathfinding 
    (scenario: Scenario)
    (start: Position) 
    (destination: Position)
    (movement: Movement) 
    : Movement =
    
    match PathMovement.calculatePath scenario start destination with
    | ValueSome path when path.Length > 1 ->
      {
        movement with
            Destination = ValueSome destination
            Path = Array.toList path.[1..] // Skip first position (current position)
      }
    | _ ->
      {
        movement with
            Destination = ValueSome destination
            Path = []
      }

module Utils =
  let inline radiusOfStage(s: Stage) =
    match s with
    | Stage.First -> 12f
    | Stage.Second -> 16f  
    | Stage.Third -> 20f

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
    
    let terrainModifier = TerrainMovement.getTerrainSpeedModifier position scenario entityRadius
    let adjustedSpeed = speed * terrainModifier
    let moveAmount = adjustedSpeed * elapsed

    if dist <= moveAmount then
      struct (destination, true)
    else
      let newX = position.X + dx / dist * moveAmount
      let newY = position.Y + dy / dist * moveAmount
      struct ({ X = newX; Y = newY }, false)

  let updateEntity (time: int64<Tick>) (scenario: Scenario) (components: EntityComponents) =
    let entityRadius = Utils.radiusOfStage components.Identity.Stage
    
    match components.Movement.Path with
    | [] ->
      match components.Movement.Destination with
      | ValueSome dest ->
        let elapsedSeconds = float32 time / 10_000_000f
        let adjustedSpeed = TerrainMovement.applyDexterityModifier components.Movement.Speed 10

        let struct (newPos, arrived) =
          moveTowards {
            Position = components.Position
            Destination = dest
            Speed = adjustedSpeed
            Elapsed = elapsedSeconds
            Scenario = scenario
            EntityRadius = entityRadius
          }

        let validPos = 
          if Query.canMoveTo newPos entityRadius scenario then newPos
          else components.Position

        if arrived then
          {
            components with
                Position = validPos
                Movement = {
                  components.Movement with
                      Destination = ValueNone
                }
          }
        else
          { components with Position = validPos }
      | ValueNone -> components
      
    | path ->
      let (nextWaypoint, remainingPath) = PathMovement.getNextWaypoint components.Position path
      
      match nextWaypoint with
      | ValueSome waypoint ->
        let elapsedSeconds = float32 time / 10_000_000f
        let adjustedSpeed = TerrainMovement.applyDexterityModifier components.Movement.Speed 10

        let struct (newPos, arrived) =
          moveTowards {
            Position = components.Position
            Destination = waypoint
            Speed = adjustedSpeed
            Elapsed = elapsedSeconds
            Scenario = scenario
            EntityRadius = entityRadius
          }

        let validPos = 
          if Query.canMoveTo newPos entityRadius scenario then newPos
          else components.Position

        if arrived && remainingPath.IsEmpty then
          {
            components with
                Position = validPos
                Movement = {
                  components.Movement with
                      Path = []
                      Destination = ValueNone
                }
          }
        else
          {
            components with
                Position = validPos
                Movement = {
                  components.Movement with
                      Path = remainingPath
                }
          }
      | ValueNone ->
        {
          components with
              Movement = {
                components.Movement with
                    Path = []
                    Destination = ValueNone
              }
        }

  let updateEntityWithContext
    (time: int64<Tick>)
    (bounds: ScenarioBounds)
    (scenario: Scenario)
    (allEntities: HashMap<Guid<EntityId>, EntityComponents>)
    (entityId: Guid<EntityId>)
    (components: EntityComponents)
    =
    match components.Movement.Destination with
    | ValueSome dest ->
      let elapsedSeconds = float32 time / 10_000_000f

      let entityRadius = Utils.radiusOfStage components.Identity.Stage
      let struct (proposedPos, arrived) =
        moveTowards {
          Position = components.Position
          Destination = dest
          Speed = components.Movement.Speed
          Elapsed = elapsedSeconds
          Scenario = scenario
          EntityRadius = entityRadius
        }

      let halfW = bounds.Width * 0.5f
      let halfH = bounds.Height * 0.5f
      let minX = bounds.CenterX - halfW
      let maxX = bounds.CenterX + halfW
      let minY = bounds.CenterY - halfH
      let maxY = bounds.CenterY + halfH

      let clampedX =
        if proposedPos.X < minX then minX
        elif proposedPos.X > maxX then maxX
        else proposedPos.X

      let clampedY =
        if proposedPos.Y < minY then minY
        elif proposedPos.Y > maxY then maxY
        else proposedPos.Y

      let clampedPos = { X = clampedX; Y = clampedY }

      let selfR = Utils.radiusOfStage components.Identity.Stage
      let mutable collision = false

      if not collision then
        let entitiesArray = allEntities |> HashMap.toArrayV

        for struct (id, other) in entitiesArray do
          if id <> entityId then
            let otherR = Utils.radiusOfStage other.Identity.Stage
            let dx = clampedPos.X - other.Position.X
            let dy = clampedPos.Y - other.Position.Y
            let dist2 = dx * dx + dy * dy
            let rad = selfR + otherR

            if dist2 < rad * rad then
              collision <- true

      if collision then
        {
          components with
              Movement = {
                components.Movement with
                    Destination = ValueNone
              }
        }
      else if arrived || (clampedPos.X = dest.X && clampedPos.Y = dest.Y) then
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
