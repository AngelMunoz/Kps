namespace Pomo.Lib.Tests

open Xunit
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Scenario
open Pomo.Lib.Pathfinding
open Pomo.Lib.Tests.TestHelpers
open System

type ``Pathfinding Tests``() =

  let createTestScenario() =
    let scenario = {
      Id = %Guid.NewGuid()
      Name = "Test Pathfinding Scenario"
      BoundsWidth = 400f
      BoundsHeight = 400f
      BattleEnabled = false
      CombatType = PvE
      TerrainObjects =
        IndexList.ofList [
          {
            Id = %Guid.NewGuid()
            Position = { X = 200f; Y = 150f }
            CollisionGeometry = Circle({ X = 200f; Y = 150f }, 30f)
            TerrainType = Blocked
            DepthLayer = 0.5f
            SpriteId = ValueSome "wall"
          }
          {
            Id = %Guid.NewGuid()
            Position = { X = 100f; Y = 250f }
            CollisionGeometry =
              Polygon(
                [|
                  { X = 80f; Y = 230f }
                  { X = 120f; Y = 230f }
                  { X = 120f; Y = 270f }
                  { X = 80f; Y = 270f }
                |]
              )
            TerrainType = Water
            DepthLayer = 0.3f
            SpriteId = ValueSome "water"
          }
        ]
      VisualLayers = Array.empty
      Transitions = Array.empty
    }

    scenario

  [<Fact>]
  member _.``Grid creation works correctly``() =
    let scenario = createTestScenario()
    let grid = Grid.create scenario 32f

    Assert.Equal(13, grid.Width) // ceil(400/32) = 13
    Assert.Equal(13, grid.Height)
    Assert.Equal(32f, grid.CellSize)
    Assert.Equal(0f, grid.OriginX)
    Assert.Equal(0f, grid.OriginY)

  [<Fact>]
  member _.``World to grid conversion works correctly``() =
    let scenario = createTestScenario()
    let grid = Grid.create scenario 32f

    let (gridX, gridY) = Grid.worldToGrid grid { X = 100f; Y = 100f }

    Assert.Equal(3, gridX) // floor(100/32) = 3
    Assert.Equal(3, gridY)

  [<Fact>]
  member _.``Grid to world conversion works correctly``() =
    let scenario = createTestScenario()
    let grid = Grid.create scenario 32f

    let worldPos = Grid.gridToWorld grid 3 3

    Assert.Equal(112f, worldPos.X) // 3 * 32 + 16 = 112
    Assert.Equal(112f, worldPos.Y)

  [<Fact>]
  member _.``Grid cells detect blocked terrain correctly``() =
    let scenario = createTestScenario()
    let grid = Grid.create scenario 32f

    // The blocked circle at (200, 150) with radius 30 should affect nearby cells
    let (blockedX, blockedY) = Grid.worldToGrid grid { X = 200f; Y = 150f }
    let blockedCell = grid.Cells.[blockedX, blockedY]

    Assert.False(blockedCell.IsWalkable)

  [<Fact>]
  member _.``Grid cells detect water terrain with higher cost``() =
    let scenario = createTestScenario()
    let grid = Grid.create scenario 32f

    // The water area at (100, 250) should have higher cost
    let (waterX, waterY) = Grid.worldToGrid grid { X = 100f; Y = 250f }
    let waterCell = grid.Cells.[waterX, waterY]

    Assert.True(waterCell.IsWalkable)
    Assert.True(waterCell.Cost > 1.0f)

  [<Fact>]
  member _.``AStar finds path around obstacles``() =
    let scenario = createTestScenario()
    let grid = Grid.create scenario 32f

    let start = { X = 50f; Y = 50f }
    let goal = { X = 350f; Y = 350f }

    let pathResult = AStar.findPath grid start goal

    match pathResult with
    | ValueSome path ->
      Assert.True(path.Length > 2) // Should have multiple waypoints
      // Check that path starts and ends reasonably close to the intended positions
      let startDistance =
        sqrt((path.[0].X - start.X) ** 2f + (path.[0].Y - start.Y) ** 2f)

      let endDistance =
        sqrt(
          (path.[path.Length - 1].X - goal.X) ** 2f
          + (path.[path.Length - 1].Y - goal.Y) ** 2f
        )

      Assert.True(
        startDistance < 32f,
        $"Start distance too far: {startDistance}"
      )

      Assert.True(endDistance < 32f, $"End distance too far: {endDistance}")
    | ValueNone -> Assert.True(false, "Expected to find a path")

  [<Fact>]
  member _.``AStar returns None for impossible paths``() =
    let scenario = {
      createTestScenario() with
          TerrainObjects =
            IndexList.ofList [
              // Create a wall that completely blocks the scenario
              {
                Id = %Guid.NewGuid()
                Position = { X = 200f; Y = 200f }
                CollisionGeometry = Circle({ X = 200f; Y = 200f }, 300f)
                TerrainType = Blocked
                DepthLayer = 0.5f
                SpriteId = ValueSome "big_wall"
              }
            ]
    }

    let grid = Grid.create scenario 32f

    let start = { X = 50f; Y = 50f }
    let goal = { X = 350f; Y = 350f }

    let pathResult = AStar.findPath grid start goal

    Assert.Equal(ValueNone, pathResult)

  [<Fact>]
  member _.``Path preview generates correct segments``() =
    let scenario = createTestScenario()

    let path = [|
      { X = 50f; Y = 50f }
      { X = 100f; Y = 100f }
      { X = 150f; Y = 150f }
    |]

    let preview = PathPreview.generatePreview scenario path 16f

    Assert.Equal(2, preview.Length) // 3 points = 2 segments
    Assert.Equal(50f, preview.[0].From.X)
    Assert.Equal(50f, preview.[0].From.Y)
    Assert.Equal(100f, preview.[0].To.X)
    Assert.Equal(100f, preview.[0].To.Y)
    Assert.True(preview.[0].IsValid) // Should be valid in open area

  [<Fact>]
  member _.``Path preview detects invalid segments``() =
    let scenario = createTestScenario()

    let path = [|
      { X = 50f; Y = 50f }
      { X = 200f; Y = 150f } // This goes through the blocked area
    |]

    let preview = PathPreview.generatePreview scenario path 16f

    Assert.Equal(1, preview.Length)
    Assert.False(preview.[0].IsValid) // Should be invalid due to collision

  [<Fact>]
  member _.``Path preview identifies terrain types``() =
    let scenario = createTestScenario()

    let path = [|
      { X = 50f; Y = 50f }
      { X = 100f; Y = 250f } // This goes to water area
    |]

    let preview = PathPreview.generatePreview scenario path 16f

    Assert.Equal(1, preview.Length)

    match preview.[0].TerrainType with
    | ValueSome Water -> Assert.True(true)
    | _ -> Assert.True(false, "Expected Water terrain type")
