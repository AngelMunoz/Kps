namespace Pomo.Lib.Movement

open System
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Classification

module Update =

  [<Struct>]
  type MoveTowardsArgs = {
    Position: Position
    Destination: Position
    Speed: float32
    Elapsed: float32
  }

  let private moveTowards args =
    let {
          Position = position
          Destination = destination
          Speed = speed
          Elapsed = elapsed
        } =
      args

    let dx = destination.X - position.X
    let dy = destination.Y - position.Y
    let dist = sqrt(dx * dx + dy * dy)
    let moveAmount = speed * elapsed

    if dist <= moveAmount then
      struct (destination, true) // Arrived
    else
      let newX = position.X + dx / dist * moveAmount
      let newY = position.Y + dy / dist * moveAmount
      struct ({ X = newX; Y = newY }, false) // Still moving

  let updateEntity (time: int64<Tick>) (components: EntityComponents) =
    match components.Movement.Destination with
    | ValueSome dest ->
      let elapsedSeconds = float32 time / 10_000_000f

      let struct (newPos, arrived) =
        moveTowards {
          Position = components.Position
          Destination = dest
          Speed = components.Movement.Speed
          Elapsed = elapsedSeconds
        }

      if arrived then
        {
          components with
              Position = newPos
              EntityComponents.Movement.Destination = ValueNone
        }
      else
        { components with Position = newPos }
    | ValueNone -> components

  let inline private radiusOfStage(s: Stage) =
    match s with
    | Stage.First -> 12f
    | Stage.Second -> 16f
    | Stage.Third -> 20f

  let updateEntityWithContext
    (time: int64<Tick>)
    (bounds: ScenarioBounds)
    (allEntities: HashMap<Guid<EntityId>, EntityComponents>)
    (entityId: Guid<EntityId>)
    (components: EntityComponents)
    =
    match components.Movement.Destination with
    | ValueSome dest ->
      let elapsedSeconds = float32 time / 10_000_000f

      let struct (proposedPos, arrived) =
        moveTowards {
          Position = components.Position
          Destination = dest
          Speed = components.Movement.Speed
          Elapsed = elapsedSeconds
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

      let selfR = radiusOfStage components.Identity.Stage
      let mutable collision = false

      if not collision then
        let entitiesArray = allEntities |> HashMap.toArrayV

        for struct (id, other) in entitiesArray do
          if id <> entityId then
            let otherR = radiusOfStage other.Identity.Stage
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
