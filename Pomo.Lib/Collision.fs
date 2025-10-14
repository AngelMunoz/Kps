namespace Pomo.Lib.Collision

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Scenario

module Geometry =
  let inline private sub (a: Position) (b: Position) = {
    X = a.X - b.X
    Y = a.Y - b.Y
  }

  let inline private dot (a: Position) (b: Position) = a.X * b.X + a.Y * b.Y

  let inline private dist (a: Position) (b: Position) =
    let dx = a.X - b.X
    let dy = a.Y - b.Y
    sqrt(dx * dx + dy * dy)

  let isPointInPolygon (point: Position) (vertices: Position[]) : bool =
    let n = vertices.Length
    let mutable inside = false
    let mutable j = n - 1

    for i in 0 .. n - 1 do
      let xi = vertices.[i].X
      let yi = vertices.[i].Y
      let xj = vertices.[j].X
      let yj = vertices.[j].Y

      if
        yi > point.Y <> (yj > point.Y)
        && point.X < (xj - xi) * (point.Y - yi) / (yj - yi + 0.00001f) + xi
      then
        inside <- not inside

      j <- i

    inside

  let isCircleIntersectPolygon
    (center: Position)
    (radius: float32)
    (vertices: Position[])
    : bool =
    if isPointInPolygon center vertices then
      true
    else
      let n = vertices.Length
      let mutable hit = false
      let mutable i = 0

      while not hit && i < n do
        let a = vertices[i]
        let b = vertices[(i + 1) % n]
        let ab = sub b a
        let ac = sub center a
        let denom = dot ab ab

        let t =
          if denom = 0f then
            0f
          else
            let raw = dot ac ab / denom

            if raw < 0f then 0f
            elif raw > 1f then 1f
            else raw

        let closest = {
          X = a.X + ab.X * t
          Y = a.Y + ab.Y * t
        }

        if dist closest center <= radius then
          hit <- true

        i <- i + 1

      hit

  let inline isCircleIntersectCircle
    (c1: Position)
    (r1: float32)
    (c2: Position)
    (r2: float32)
    : bool =
    dist c1 c2 <= r1 + r2

module Query =
  open Geometry

  [<TailCall>]
  let rec private collectFromIndex src pos radius index (acc: ResizeArray<_>) =
    match src |> IndexList.tryGetV index with
    | ValueNone -> acc
    | ValueSome obj ->
      let keep =
        match obj.CollisionGeometry with
        | Circle(center, r) -> isCircleIntersectCircle pos radius center r
        | Polygon verts -> isCircleIntersectPolygon pos radius verts
        | NoCollision -> false

      let newAcc =
        if keep then
          acc.Add obj
          acc
        else
          acc

      match IndexList.tryGetPrev index src with
      | None -> newAcc
      | Some(nextIndex, _) -> collectFromIndex src pos radius nextIndex newAcc

  let queryTerrainObjects
    (pos: Position)
    (radius: float32)
    (scenario: Scenario)
    =
    let src = scenario.TerrainObjects

    match IndexList.tryLastIndex src with
    | None -> Array.empty
    | Some lastIndex ->
      let acc = ResizeArray()
      (collectFromIndex src pos radius lastIndex acc).ToArray()

  [<TailCall>]
  let rec private checkFromIndex src pos radius index =
    match src |> IndexList.tryGetV index with
    | ValueNone -> true
    | ValueSome obj ->
      let blocked =
        match obj.TerrainType, obj.CollisionGeometry with
        | Blocked, Circle(center, r) ->
          isCircleIntersectCircle pos radius center r
        | Blocked, Polygon verts -> isCircleIntersectPolygon pos radius verts
        | _ -> false

      if blocked then
        false
      else
        match IndexList.tryGetNext index src with
        | None -> true
        | Some(nextIndex, _) -> checkFromIndex src pos radius nextIndex

  let canMoveTo (pos: Position) (radius: float32) (scenario: Scenario) =
    let src = scenario.TerrainObjects

    match IndexList.tryFirstIndex src with
    | None -> true
    | Some firstIndex -> checkFromIndex src pos radius firstIndex
