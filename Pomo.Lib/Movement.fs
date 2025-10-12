namespace Pomo.Lib.Movement

open System
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components

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
