namespace Pomo.Lib.Pathfinding

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Scenario
open Pomo.Lib.Collision

[<Struct>]
type GridCell = {
  X: int
  Y: int
  IsWalkable: bool
  Cost: float32
}

type PathNode = {
  Position: Position
  GCost: float32
  HCost: float32
  FCost: float32
  Parent: PathNode option
}

[<Struct>]
type PathfindingGrid = {
  Width: int
  Height: int
  CellSize: float32
  Cells: GridCell[,]
  OriginX: float32
  OriginY: float32
}

module Grid =
  let create (scenario: Scenario) (cellSize: float32) : PathfindingGrid =
    let width = int(ceil(scenario.BoundsWidth / cellSize))
    let height = int(ceil(scenario.BoundsHeight / cellSize))

    let cells =
      Array2D.init width height (fun x y ->
        let worldX = float32 x * cellSize + cellSize * 0.5f
        let worldY = float32 y * cellSize + cellSize * 0.5f
        let pos = { X = worldX; Y = worldY }

        let isWalkable = Query.canMoveTo pos (cellSize * 0.4f) scenario

        let cost =
          let terrainObjs =
            Query.queryTerrainObjects pos (cellSize * 0.4f) scenario

          let waterPenalty =
            terrainObjs
            |> Array.tryFind(fun obj -> obj.TerrainType = Water)
            |> function
              | Some _ -> 2.0f
              | None -> 1.0f

          waterPenalty

        {
          X = x
          Y = y
          IsWalkable = isWalkable
          Cost = cost
        })

    {
      Width = width
      Height = height
      CellSize = cellSize
      Cells = cells
      OriginX = 0f
      OriginY = 0f
    }

  let createWithRadius
    (scenario: Scenario)
    (cellSize: float32)
    (entityRadius: float32)
    : PathfindingGrid =
    let width = int(ceil(scenario.BoundsWidth / cellSize))
    let height = int(ceil(scenario.BoundsHeight / cellSize))

    let cells =
      Array2D.init width height (fun x y ->
        let worldX = float32 x * cellSize + cellSize * 0.5f
        let worldY = float32 y * cellSize + cellSize * 0.5f
        let pos = { X = worldX; Y = worldY }

        // Use entity radius for more accurate collision detection
        // Add small buffer to prevent tight squeezes
        let checkRadius = entityRadius + 4.0f
        let isWalkable = Query.canMoveTo pos checkRadius scenario

        let cost =
          let terrainObjs = Query.queryTerrainObjects pos checkRadius scenario

          let waterPenalty =
            terrainObjs
            |> Array.tryFind(fun obj -> obj.TerrainType = Water)
            |> function
              | Some _ -> 2.0f
              | None -> 1.0f

          waterPenalty

        {
          X = x
          Y = y
          IsWalkable = isWalkable
          Cost = cost
        })

    {
      Width = width
      Height = height
      CellSize = cellSize
      Cells = cells
      OriginX = 0f
      OriginY = 0f
    }

  let createWithEntities 
    (scenario: Scenario) 
    (cellSize: float32) 
    (entityRadius: float32)
    (allEntities: struct (Guid<EntityId> * EntityComponents) array)
    (excludeEntityId: Guid<EntityId>)
    : PathfindingGrid =
    let width = int(ceil(scenario.BoundsWidth / cellSize))
    let height = int(ceil(scenario.BoundsHeight / cellSize))

    let cells =
      Array2D.init width height (fun x y ->
        let worldX = float32 x * cellSize + cellSize * 0.5f
        let worldY = float32 y * cellSize + cellSize * 0.5f
        let pos = { X = worldX; Y = worldY }

        // Use entity radius for more accurate collision detection
        // Add larger buffer to prevent tight squeezes around entities
        let checkRadius = entityRadius + 6.0f
        let isTerrainWalkable = Query.canMoveTo pos checkRadius scenario
        
        // Check for entity collisions (excluding the moving entity itself)
        let mutable entityCollision = false
        if isTerrainWalkable then
          for struct (id, entity) in allEntities do
            if id <> excludeEntityId && not entityCollision then
              let otherRadius = 
                match entity.Identity.Stage with
                | Stage.First -> 12f
                | Stage.Second -> 16f
                | Stage.Third -> 20f
              let dx = pos.X - entity.Position.X
              let dy = pos.Y - entity.Position.Y
              let dist2 = dx * dx + dy * dy
              let minDist = checkRadius + otherRadius + 8.0f // Extra buffer for entity avoidance
              if dist2 < minDist * minDist then
                entityCollision <- true

        let isWalkable = isTerrainWalkable && not entityCollision

        let cost =
          if not isWalkable then 1.0f
          else
            let terrainObjs =
              Query.queryTerrainObjects pos checkRadius scenario

            let waterPenalty =
              terrainObjs
              |> Array.tryFind(fun obj -> obj.TerrainType = Water)
              |> function
                | Some _ -> 2.0f
                | None -> 1.0f

            // Add entity proximity penalty to discourage paths too close to entities
            let mutable proximityPenalty = 1.0f
            for struct (id, entity) in allEntities do
              if id <> excludeEntityId then
                let otherRadius = 
                  match entity.Identity.Stage with
                  | Stage.First -> 12f
                  | Stage.Second -> 16f
                  | Stage.Third -> 20f
                let dx = pos.X - entity.Position.X
                let dy = pos.Y - entity.Position.Y
                let dist = sqrt(dx * dx + dy * dy)
                let warningDist = checkRadius + otherRadius + 16.0f // Warning zone
                if dist < warningDist then
                  proximityPenalty <- proximityPenalty + 1.5f

            waterPenalty * proximityPenalty

        {
          X = x
          Y = y
          IsWalkable = isWalkable
          Cost = cost
        })

    {
      Width = width
      Height = height
      CellSize = cellSize
      Cells = cells
      OriginX = 0f
      OriginY = 0f
    }

  let worldToGrid (grid: PathfindingGrid) (worldPos: Position) : int * int =
    let gridX = int((worldPos.X - grid.OriginX) / grid.CellSize)
    let gridY = int((worldPos.Y - grid.OriginY) / grid.CellSize)
    (max 0 (min (grid.Width - 1) gridX), max 0 (min (grid.Height - 1) gridY))

  let gridToWorld (grid: PathfindingGrid) (gridX: int) (gridY: int) : Position = {
    X = grid.OriginX + (float32 gridX * grid.CellSize) + grid.CellSize * 0.5f
    Y = grid.OriginY + (float32 gridY * grid.CellSize) + grid.CellSize * 0.5f
  }

  let getNeighbors (grid: PathfindingGrid) (x: int) (y: int) : (int * int)[] =
    [|
      (x - 1, y) // Left
      (x + 1, y) // Right
      (x, y - 1) // Up
      (x, y + 1) // Down
      (x - 1, y - 1) // Top-left
      (x + 1, y - 1) // Top-right
      (x - 1, y + 1) // Bottom-left
      (x + 1, y + 1) // Bottom-right
    |]
    |> Array.filter(fun (nx, ny) ->
      nx >= 0
      && ny >= 0
      && nx < grid.Width
      && ny < grid.Height
      && grid.Cells.[nx, ny].IsWalkable)

module AStar =
  let private heuristic (a: Position) (b: Position) : float32 =
    let dx = abs(a.X - b.X)
    let dy = abs(a.Y - b.Y)
    sqrt(dx * dx + dy * dy)

  let private reconstructPath(node: PathNode) : Position[] =
    let path = ResizeArray<Position>()
    let mutable current = Some node

    while current.IsSome do
      path.Add(current.Value.Position)
      current <- current.Value.Parent

    path.Reverse()
    path.ToArray()

  let findPath
    (grid: PathfindingGrid)
    (start: Position)
    (goal: Position)
    : Position[] voption =

    let (startX, startY) = Grid.worldToGrid grid start
    let (goalX, goalY) = Grid.worldToGrid grid goal

    if
      not grid.Cells.[startX, startY].IsWalkable
      || not grid.Cells.[goalX, goalY].IsWalkable
    then
      ValueNone
    else
      let openSet =
        System.Collections.Generic.PriorityQueue<PathNode, float32>()

      let closedSet = System.Collections.Generic.HashSet<int * int>()
      let gScore = System.Collections.Generic.Dictionary<int * int, float32>()

      let startPos = Grid.gridToWorld grid startX startY
      let goalPos = Grid.gridToWorld grid goalX goalY

      let startNode = {
        Position = startPos
        GCost = 0f
        HCost = heuristic startPos goalPos
        FCost = heuristic startPos goalPos
        Parent = None
      }

      openSet.Enqueue(startNode, startNode.FCost)
      gScore.[(startX, startY)] <- 0f

      let mutable found = ValueNone

      while openSet.Count > 0 && found.IsNone do
        let current = openSet.Dequeue()
        let (currentX, currentY) = Grid.worldToGrid grid current.Position

        if currentX = goalX && currentY = goalY then
          found <- ValueSome(reconstructPath current)
        else
          closedSet.Add((currentX, currentY)) |> ignore

          let neighbors = Grid.getNeighbors grid currentX currentY

          for (nx, ny) in neighbors do
            if not(closedSet.Contains((nx, ny))) then
              let neighborPos = Grid.gridToWorld grid nx ny

              let moveCost =
                let dx = abs(nx - currentX)
                let dy = abs(ny - currentY)
                if dx = 1 && dy = 1 then 1.414f else 1.0f // Diagonal vs orthogonal

              let cellCost = grid.Cells.[nx, ny].Cost
              let tentativeGScore = current.GCost + (moveCost * cellCost)

              let currentGScore =
                gScore.TryGetValue((nx, ny))
                |> function
                  | (true, score) -> score
                  | (false, _) -> Single.MaxValue

              if tentativeGScore < currentGScore then
                gScore.[(nx, ny)] <- tentativeGScore
                let hCost = heuristic neighborPos goalPos
                let fCost = tentativeGScore + hCost

                let neighborNode = {
                  Position = neighborPos
                  GCost = tentativeGScore
                  HCost = hCost
                  FCost = fCost
                  Parent = Some current
                }

                openSet.Enqueue(neighborNode, fCost)

      found

module PathPreview =
  [<Struct>]
  type PathSegment = {
    From: Position
    To: Position
    IsValid: bool
    TerrainType: TerrainType voption
  }

  let generatePreview
    (scenario: Scenario)
    (path: Position[])
    (entityRadius: float32)
    : PathSegment[] =

    if path.Length < 2 then
      Array.empty
    else
      Array.init (path.Length - 1) (fun i ->
        let from = path.[i]
        let to_ = path.[i + 1]

        let isValid = Query.canMoveTo to_ entityRadius scenario

        let terrainObjects =
          Query.queryTerrainObjects to_ entityRadius scenario

        let terrainType =
          terrainObjects
          |> Array.tryHead
          |> Option.map(fun obj -> obj.TerrainType)
          |> ValueOption.ofOption

        {
          From = from
          To = to_
          IsValid = isValid
          TerrainType = terrainType
        })
