namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.VisualEffects
open Pomo.Lib.Scenario
open Pomo.Lib.Pathfinding
open Pomo.Lib.Collision

module RenderSystem =
  let mutable smallCircle: Texture2D = null
  let mutable mediumCircle: Texture2D = null
  let mutable largeCircle: Texture2D = null

  let private makeCircle (gd: GraphicsDevice) (radius: int) =
    let size = radius * 2
    let tex = new Texture2D(gd, size, size)
    let data = Array.zeroCreate<Color>(size * size)
    let r2 = float(radius * radius)

    for y in 0 .. size - 1 do
      let dy = float(y - radius)

      for x in 0 .. size - 1 do
        let dx = float(x - radius)
        let inside = dx * dx + dy * dy <= r2
        data[y * size + x] <- if inside then Color.White else Color.Transparent

    tex.SetData<Color>(data)
    tex

  let init(gd: GraphicsDevice) =
    if isNull smallCircle then
      smallCircle <- makeCircle gd 12

    if isNull mediumCircle then
      mediumCircle <- makeCircle gd 16

    if isNull largeCircle then
      largeCircle <- makeCircle gd 20

  let private circleForStage stage =
    match stage with
    | First -> smallCircle
    | Second -> mediumCircle
    | Third -> largeCircle

  let draw
    struct (entities, derived)
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (hud: SpriteFont voption)
    (view: Matrix)
    (selected: Guid<EntityId> voption)
    (bounds: Pomo.Lib.Domain.ScenarioBounds)
    (scenario: Pomo.Lib.Scenario.Scenario)
    (showGrid: bool)
    (pathPreview: Pomo.Lib.Pathfinding.PathPreview.PathSegment[])
    (currentPath: Position[])
    (floatingTexts: Pomo.Lib.Domain.VisualEffects.FloatingText[])
    (gameTime: TimeSpan)
    =
    sb.Begin(
      SpriteSortMode.Deferred,
      BlendState.AlphaBlend,
      SamplerState.PointClamp,
      null,
      null,
      null,
      view
    )

    let halfW = bounds.Width * 0.5f
    let halfH = bounds.Height * 0.5f
    let minX = bounds.CenterX - halfW
    let maxX = bounds.CenterX + halfW
    let minY = bounds.CenterY - halfH
    let maxY = bounds.CenterY + halfH
    let thickness = 2
    let topI = int minY
    let bottomI = int maxY - thickness
    let leftI = int minX
    let rightI = int maxX - thickness
    let widthI = int bounds.Width
    let heightI = int bounds.Height
    let lineColor = Color(255, 255, 0, 160)
    sb.Draw(pixel, Rectangle(leftI, topI, widthI, thickness), lineColor)
    sb.Draw(pixel, Rectangle(leftI, bottomI, widthI, thickness), lineColor)
    sb.Draw(pixel, Rectangle(leftI, topI, thickness, heightI), lineColor)
    sb.Draw(pixel, Rectangle(rightI, topI, thickness, heightI), lineColor)

    // Render terrain objects
    let terrainObjects = scenario.TerrainObjects |> IndexList.toArray

    for terrainObj in terrainObjects do
      let color =
        match terrainObj.TerrainType with
        | TerrainType.Blocked -> Color(139, 69, 19, 180) // Brown for walls
        | TerrainType.Water -> Color(0, 191, 255, 120) // Light blue for water
        | TerrainType.Hazard -> Color(255, 69, 0, 150) // Red-orange for hazards
        | _ -> Color(128, 128, 128, 100) // Gray for other

      match terrainObj.CollisionGeometry with
      | Circle(center, radius) ->
        let diameter = int(radius * 2f)
        let x = int(center.X - radius)
        let y = int(center.Y - radius)
        sb.Draw(pixel, Rectangle(x, y, diameter, diameter), color)

      | Polygon(vertices) when vertices.Length > 0 ->
        let minX = vertices |> Array.map(fun v -> v.X) |> Array.min |> int
        let maxX = vertices |> Array.map(fun v -> v.X) |> Array.max |> int
        let minY = vertices |> Array.map(fun v -> v.Y) |> Array.min |> int
        let maxY = vertices |> Array.map(fun v -> v.Y) |> Array.max |> int
        let width = maxX - minX
        let height = maxY - minY
        sb.Draw(pixel, Rectangle(minX, minY, width, height), color)

      | _ -> ()

    // Render transition points
    for transition in scenario.Transitions do
      let pos = transition.FromPosition
      let size = 64
      let x = int(pos.X - float32 size * 0.5f)
      let y = int(pos.Y - float32 size * 0.5f)

      // Portal border
      sb.Draw(pixel, Rectangle(x - 2, y - 2, size + 4, 4), Color.Purple)
      sb.Draw(pixel, Rectangle(x - 2, y + size - 2, size + 4, 4), Color.Purple)
      sb.Draw(pixel, Rectangle(x - 2, y, 4, size), Color.Purple)
      sb.Draw(pixel, Rectangle(x + size - 2, y, 4, size), Color.Purple)

      // Portal interior
      sb.Draw(pixel, Rectangle(x, y, size, size), Color(128, 0, 128, 60))

    // Render pathfinding grid (if enabled)
    if showGrid then
      let grid =
        Grid.createWithEntities
          scenario
          32.0f
          16.0f
          [||]
          (Guid.Empty |> UMX.tag<EntityId>)

      let cellSize = int grid.CellSize

      for x in 0 .. grid.Width - 1 do
        for y in 0 .. grid.Height - 1 do
          let cell = grid.Cells.[x, y]
          let worldPos = Grid.gridToWorld grid x y
          let cellX = int(worldPos.X - grid.CellSize * 0.5f)
          let cellY = int(worldPos.Y - grid.CellSize * 0.5f)

          let gridColor =
            if not cell.IsWalkable then Color(255, 0, 0, 80) // Red for blocked
            elif cell.Cost > 1.0f then Color(255, 255, 0, 40) // Yellow for higher cost
            else Color(0, 255, 0, 20) // Green for walkable

          // Draw cell border
          sb.Draw(
            pixel,
            Rectangle(cellX, cellY, cellSize, 1),
            Color(255, 255, 255, 100)
          )

          sb.Draw(
            pixel,
            Rectangle(cellX, cellY, 1, cellSize),
            Color(255, 255, 255, 100)
          )

          // Fill cell
          sb.Draw(
            pixel,
            Rectangle(cellX + 1, cellY + 1, cellSize - 2, cellSize - 2),
            gridColor
          )

    // Render path preview
    if pathPreview.Length > 0 then
      for segment in pathPreview do
        let color = if segment.IsValid then Color.LimeGreen else Color.Red
        let fromX = int segment.From.X
        let fromY = int segment.From.Y
        let toX = int segment.To.X
        let toY = int segment.To.Y

        // Draw line between waypoints (simple approximation)
        let dx = toX - fromX
        let dy = toY - fromY
        let distance = sqrt(float(dx * dx + dy * dy)) |> int

        if distance > 0 then
          for i in 0..distance do
            let t = float i / float distance
            let x = int(float fromX + t * float dx)
            let y = int(float fromY + t * float dy)
            sb.Draw(pixel, Rectangle(x - 1, y - 1, 3, 3), color)

    // Render current path waypoints
    if currentPath.Length > 0 then
      for i in 0 .. currentPath.Length - 1 do
        let waypoint = currentPath.[i]
        let x = int(waypoint.X - 4f)
        let y = int(waypoint.Y - 4f)

        let color =
          if i = 0 then Color.White
          elif i = currentPath.Length - 1 then Color.Red
          else Color.Yellow

        sb.Draw(pixel, Rectangle(x, y, 8, 8), color)

    for struct (id, comp: Components.EntityComponents) in entities do
      let pos = comp.Position

      let color =
        if comp.Factions |> HashSet.contains Player then Color.Green
        elif comp.Factions |> HashSet.contains Enemy then Color.Red
        elif comp.Resources.Status = Dead then Color.Gray
        else Color.Blue

      let circle = circleForStage comp.Identity.Stage

      if not(isNull circle) then
        let w = float32 circle.Width
        let h = float32 circle.Height
        let x = pos.X - w * 0.5f
        let y = pos.Y - h * 0.5f
        sb.Draw(circle, Vector2(x, y), color)

        match selected with
        | ValueSome sid when sid = id ->
          let scale = 1.25f
          let sw = w * scale
          let sh = h * scale
          let sx = pos.X - sw * 0.5f
          let sy = pos.Y - sh * 0.5f
          let dest = Rectangle(int sx, int sy, int sw, int sh)
          sb.Draw(circle, dest, Color(255, 255, 0, 120))
        | _ -> ()
      else
        let w, h = 24f, 24f
        sb.Draw(pixel, Rectangle(int pos.X, int pos.Y, int w, int h), color)

      let maxHp =
        match HashMap.tryFindV id derived with
        | ValueSome(stats: DerivedStats) -> stats.HP
        | ValueNone -> comp.Resources.HP

      let currentHp = comp.Resources.HP
      let hpRatio = if maxHp > 0 then float32 currentHp / float32 maxHp else 0f
      let barW, barH = 28f, 4f
      let barX = pos.X - barW * 0.5f
      let barY = pos.Y - 20f
      let backRect = Rectangle(int barX, int barY, int barW, int barH)

      let fillRect =
        Rectangle(int barX, int barY, int(barW * hpRatio), int barH)

      sb.Draw(pixel, backRect, Color(60, 60, 60))
      sb.Draw(pixel, fillRect, Color.LimeGreen)

      match hud with
      | ValueSome font ->
        let label =
          string comp.Identity.Family + "/" + string comp.Identity.Stage

        let textSize = font.MeasureString(label)
        let tx = pos.X - textSize.X * 0.5f
        let ty = float32 backRect.Y - textSize.Y - 2f
        let textPos = Vector2(tx, ty)
        let shadowPos = textPos + Vector2(1f, 1f)
        sb.DrawString(font, label, shadowPos, Color(0, 0, 0, 180))
        sb.DrawString(font, label, textPos, Color.White)
      | _ -> ()

    // Render floating texts
    match hud with
    | ValueSome font ->
      for ft in floatingTexts do
        let age = gameTime - ft.CreationTick
        let lifetime = TimeSpan.FromSeconds(2.5)
        let progress =
          float32(age.TotalMilliseconds / lifetime.TotalMilliseconds)

        if progress <= 1.0f then
          let yOffset = -40f * progress
          let alpha = 1.0f - progress
          let pos = ft.Position
          let text = ft.Text

          let color =
            match ft.Color with
            | Damage -> Color.Red
            | Critical -> Color.Yellow
            | Heal -> Color.LightGreen
            | Evade -> Color.White
            | _ -> Color.White

          let finalColor = color * alpha
          let textSize = font.MeasureString(text)
          let tx = pos.X - textSize.X * 0.5f
          let ty = pos.Y - 45f + yOffset
          let textPos = Vector2(tx, ty)
          let shadowPos = textPos + Vector2(1f, 1f)

          sb.DrawString(font, text, shadowPos, Color(0, 0, 0, 180) * alpha)
          sb.DrawString(font, text, textPos, finalColor)
    | _ -> ()

    sb.End()
