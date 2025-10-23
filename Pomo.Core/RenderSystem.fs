namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.VisualEffects
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Pathfinding
open Pomo.Lib.Domain.Visuals
open Pomo.Lib.Domain.Services

module RenderSystem =
  let mutable smallCircle: Texture2D = null
  let mutable mediumCircle: Texture2D = null
  let mutable largeCircle: Texture2D = null

  let mutable effectIcon: Texture2D = null

  [<Struct>]
  type WorldContext = {
    Bounds: Pomo.Lib.Domain.ScenarioBounds
    TerrainScenario: Scenario
  }

  [<Struct>]
  type NavigationContext = {
    ShowGrid: bool
    PathPreview: Pomo.Lib.Pathfinding.PathPreview.PathSegment[]
    CurrentPath: Position[]
    Grid: Pomo.Lib.Pathfinding.PathfindingGrid voption
  }

  [<Struct>]
  type EntityContext = {
    Entities: struct (Guid<EntityId> * Components.EntityComponents) array
    Derived: HashMap<Guid<EntityId>, DerivedStats>
    Selected: Guid<EntityId> voption
    Hud: SpriteFont voption
  }

  [<Struct>]
  type EffectsContext = {
    FloatingTexts: Pomo.Lib.Domain.VisualEffects.FloatingText[]
    Projectiles: ActiveProjectile[]
    Aoes: ActiveAoe[]
    Impacts: ActiveImpact[]
    GameTime: TimeSpan
    Services: EngineServices
    Hud: SpriteFont voption
  }

  [<Struct>]
  type InputContext = {
    InputMode: InputManager.InputMode
    MouseWorldPos: Vector2
  }


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

    if isNull effectIcon then
      effectIcon <- new Texture2D(gd, 10, 10)
      let data = Array.create (10 * 10) Color.White
      effectIcon.SetData(data)

  let private circleForStage stage =
    match stage with
    | First -> smallCircle
    | Second -> mediumCircle
    | Third -> largeCircle

  let private drawScenarioBounds
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (bounds: Pomo.Lib.Domain.ScenarioBounds)
    =
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

  let private drawTerrainObjects
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (scenario: Scenario)
    =
    let terrainObjects = scenario.TerrainObjects |> IndexList.toArray

    for terrainObj in terrainObjects do
      let color =
        match terrainObj.TerrainType with
        | TerrainType.Blocked -> Color(139, 69, 19, 180)
        | TerrainType.Water -> Color(0, 191, 255, 120)
        | TerrainType.Hazard -> Color(255, 69, 0, 150)
        | _ -> Color(128, 128, 128, 100)

      match terrainObj.CollisionGeometry with
      | CollisionGeometry.Circle(center, radius) ->
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

  let private drawTransitions
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (scenario: Scenario)
    =
    for transition in scenario.Transitions do
      let pos = transition.FromPosition
      let size = 64
      let x = int(pos.X - float32 size * 0.5f)
      let y = int(pos.Y - float32 size * 0.5f)
      sb.Draw(pixel, Rectangle(x - 2, y - 2, size + 4, 4), Color.Purple)
      sb.Draw(pixel, Rectangle(x - 2, y + size - 2, size + 4, 4), Color.Purple)
      sb.Draw(pixel, Rectangle(x - 2, y, 4, size), Color.Purple)
      sb.Draw(pixel, Rectangle(x + size - 2, y, 4, size), Color.Purple)
      sb.Draw(pixel, Rectangle(x, y, size, size), Color(128, 0, 128, 60))

  let private drawGrid
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (scenario: Scenario)
    showGrid
    =
    if showGrid then
      let grid = Grid.createGrid scenario 32.0f 16.0f

      let cellSize = int grid.CellSize

      for x in 0 .. grid.Width - 1 do
        for y in 0 .. grid.Height - 1 do
          let cell = grid.Cells.[x, y]
          let worldPos = Grid.gridToWorld grid x y
          let cellX = int(worldPos.X - grid.CellSize * 0.5f)
          let cellY = int(worldPos.Y - grid.CellSize * 0.5f)

          let gridColor =
            if not cell.IsWalkable then Color(255, 0, 0, 80)
            elif cell.Cost > 1.0f then Color(255, 255, 0, 40)
            else Color(0, 255, 0, 20)

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

          sb.Draw(
            pixel,
            Rectangle(cellX + 1, cellY + 1, cellSize - 2, cellSize - 2),
            gridColor
          )

  let private drawPathPreview
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (pathPreview: Pomo.Lib.Pathfinding.PathPreview.PathSegment[])
    =
    if pathPreview.Length > 0 then
      for segment in pathPreview do
        let color = if segment.IsValid then Color.LimeGreen else Color.Red
        let fromX = int segment.From.X
        let fromY = int segment.From.Y
        let toX = int segment.To.X
        let toY = int segment.To.Y
        let dx = toX - fromX
        let dy = toY - fromY
        let distance = sqrt(float(dx * dx + dy * dy)) |> int

        if distance > 0 then
          for i in 0..distance do
            let t = float i / float distance
            let x = int(float fromX + t * float dx)
            let y = int(float fromY + t * float dy)
            sb.Draw(pixel, Rectangle(x - 1, y - 1, 3, 3), color)

  let private drawCurrentPath
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (currentPath: Position[])
    =
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

  let private drawEntity
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (selected: Guid<EntityId> voption)
    (derived: HashMap<Guid<EntityId>, DerivedStats>)
    (id: Guid<EntityId>)
    (comp: Components.EntityComponents)
    =
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
    let hpBarX = pos.X - barW * 0.5f
    let hpBarY = pos.Y - 20f
    let hpBackRect = Rectangle(int hpBarX, int hpBarY, int barW, int barH)

    let hpFillRect =
      Rectangle(int hpBarX, int hpBarY, int(barW * hpRatio), int barH)

    sb.Draw(pixel, hpBackRect, Color(60, 60, 60))
    sb.Draw(pixel, hpFillRect, Color.LimeGreen)

    let maxMp =
      match HashMap.tryFindV id derived with
      | ValueSome(stats: DerivedStats) -> stats.MP
      | ValueNone -> comp.Resources.MP

    let currentMp = comp.Resources.MP
    let mpRatio = if maxMp > 0 then float32 currentMp / float32 maxMp else 0f
    let mpBarY = hpBarY + barH + 1f
    let mpBackRect = Rectangle(int hpBarX, int mpBarY, int barW, int barH)

    let mpFillRect =
      Rectangle(int hpBarX, int mpBarY, int(barW * mpRatio), int barH)

    sb.Draw(pixel, mpBackRect, Color(80, 80, 20))
    sb.Draw(pixel, mpFillRect, Color.Yellow)

    if not(isNull effectIcon) && not(comp.Effects.IsEmpty) then
      let effects = comp.Effects |> HashMap.toValueArray
      let iconSize = 10f
      let padding = 2f
      let totalW = float32 effects.Length * (iconSize + padding) - padding
      let startX = pos.X - totalW * 0.5f
      let circleH = if isNull circle then 24f else float32 circle.Height
      let startY = pos.Y + circleH * 0.5f + 4f

      for i in 0 .. effects.Length - 1 do
        let effect = effects.[i]
        let x = startX + float32 i * (iconSize + padding)

        let effectColor =
          match effect.Definition.Kind with
          | Effects.EffectKind.Buff -> Color.LightGreen
          | Effects.EffectKind.ResourceOverTime -> Color.LightGreen
          | Effects.EffectKind.Debuff -> Color.IndianRed
          | Effects.EffectKind.DamageOverTime -> Color.IndianRed
          | Effects.EffectKind.Stun -> Color.DarkOrange
          | Effects.EffectKind.Silence -> Color.MediumPurple
          | Effects.EffectKind.Taunt -> Color.Gold

        sb.Draw(
          effectIcon,
          Rectangle(int x, int startY, int iconSize, int iconSize),
          effectColor
        )

  let private drawFloatingTexts
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (hud: SpriteFont voption)
    (floatingTexts: Pomo.Lib.Domain.VisualEffects.FloatingText[])
    (gameTime: TimeSpan)
    =
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
            | SystemMessage -> Color.Goldenrod
            | MPRecovery -> Color.SkyBlue

          let finalColor = color * alpha
          let textSize = font.MeasureString(text)
          let tx = pos.X - textSize.X * 0.5f
          let ty = pos.Y - 45f + yOffset
          let textPos = Vector2(tx, ty)
          let shadowPos = textPos + Vector2(1f, 1f)
          sb.DrawString(font, text, shadowPos, Color(0, 0, 0, 180) * alpha)
          sb.DrawString(font, text, textPos, finalColor)
    | _ -> ()

  let private drawProjectiles
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (projectiles: ActiveProjectile[])
    (services: EngineServices)
    (gameTime: TimeSpan)
    =
    for proj in projectiles do
      let def = services.projectileStore.find proj.DefinitionId
      let color = Color.toMonoGameColor def.Color
      let size = def.Size
      let x = int(proj.CurrentPosition.X - size * 0.5f)
      let y = int(proj.CurrentPosition.Y - size * 0.5f)
      sb.Draw(pixel, Rectangle(x, y, int size, int size), color)

  let private drawAoes
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (aoes: ActiveAoe[])
    (services: EngineServices)
    =
    for aoe in aoes do
      let def = services.aoeStore.find aoe.DefinitionId
      let color = Color.toMonoGameColor def.Color
      let radius = def.Radius
      let x = int(aoe.Position.X - radius)
      let y = int(aoe.Position.Y - radius)
      let diameter = int(radius * 2.0f)
      sb.Draw(pixel, Rectangle(x, y, diameter, diameter), color * 0.5f)

  let private drawImpacts
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (impacts: ActiveImpact[])
    (services: EngineServices)
    (gameTime: TimeSpan)
    =
    for impact in impacts do
      let def = services.impactStore.find impact.DefinitionId
      let age = gameTime - impact.CreationTick

      if age < def.Duration then
        let color = Color.toMonoGameColor def.Color
        let size = def.Size
        let x = int(impact.Position.X - size * 0.5f)
        let y = int(impact.Position.Y - size * 0.5f)
        sb.Draw(pixel, Rectangle(x, y, int size, int size), color)

  let private drawTargetingIndicator
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (inputMode: InputManager.InputMode)
    (mouseWorldPos: Vector2)
    (entityCtx: EntityContext)
    =
    match inputMode with
    | InputManager.InputMode.AbilityTargeting(_,
                                              InputManager.TargetingMode.EntityTargeting) ->
      if Platform.IsMobile() then
        // On mobile, highlight all valid targets
        for struct (id, comp) in entityCtx.Entities do
          if comp.Factions |> HashSet.contains Enemy then
            let circle = circleForStage comp.Identity.Stage

            if not(isNull circle) then
              let w = float32 circle.Width
              let h = float32 circle.Height
              let scale = 1.25f
              let sw = w * scale
              let sh = h * scale
              let sx = comp.Position.X - sw * 0.5f
              let sy = comp.Position.Y - sh * 0.5f
              let dest = Rectangle(int sx, int sy, int sw, int sh)
              sb.Draw(circle, dest, Color(255, 255, 255, 100))
      else
        // On desktop, show indicator at mouse position
        let radius = 16f
        let w = radius * 2f
        let h = radius * 2f
        let x = mouseWorldPos.X - w * 0.5f
        let y = mouseWorldPos.Y - h * 0.5f
        let indicatorColor = Color(255, 0, 0, 100)

        if not(isNull mediumCircle) then
          sb.Draw(mediumCircle, Vector2(x, y), indicatorColor)
    | InputManager.InputMode.AbilityTargeting(_,
                                              InputManager.TargetingMode.GroundTargeting radius) ->
      let diameter = int(radius * 2f)
      let x = int(mouseWorldPos.X - radius)
      let y = int(mouseWorldPos.Y - radius)
      let indicatorColor = Color(255, 165, 0, 80)
      sb.Draw(pixel, Rectangle(x, y, diameter, diameter), indicatorColor)
    | _ -> ()

  let drawWorld sb pixel (ctx: WorldContext) =
    drawScenarioBounds sb pixel ctx.Bounds
    drawTerrainObjects sb pixel ctx.TerrainScenario
    drawTransitions sb pixel ctx.TerrainScenario

  let drawNavigation (sb: SpriteBatch) pixel (ctx: NavigationContext) =
    match ctx.Grid with
    | ValueSome g when ctx.ShowGrid ->
      let cellSize = int g.CellSize

      for x in 0 .. g.Width - 1 do
        for y in 0 .. g.Height - 1 do
          let cell = g.Cells.[x, y]
          let worldPos = Pomo.Lib.Pathfinding.Grid.gridToWorld g x y
          let cellX = int(worldPos.X - g.CellSize * 0.5f)
          let cellY = int(worldPos.Y - g.CellSize * 0.5f)

          let gridColor =
            if not cell.IsWalkable then Color(255, 0, 0, 80)
            elif cell.Cost > 1.0f then Color(255, 255, 0, 40)
            else Color(0, 255, 0, 20)

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

          sb.Draw(
            pixel,
            Rectangle(cellX + 1, cellY + 1, cellSize - 2, cellSize - 2),
            gridColor
          )
    | _ -> ()

    drawPathPreview sb pixel ctx.PathPreview
    drawCurrentPath sb pixel ctx.CurrentPath

  let drawEntitiesPhase sb pixel (ctx: EntityContext) =
    for struct (id, comp) in ctx.Entities do
      drawEntity sb pixel ctx.Selected ctx.Derived id comp

  let drawEffectsPhase sb pixel (ctx: EffectsContext) =
    drawFloatingTexts sb pixel ctx.Hud ctx.FloatingTexts ctx.GameTime
    drawProjectiles sb pixel ctx.Projectiles ctx.Services ctx.GameTime
    drawAoes sb pixel ctx.Aoes ctx.Services
    drawImpacts sb pixel ctx.Impacts ctx.Services ctx.GameTime

  let drawInputPhase sb pixel (ctx: InputContext) (entityCtx: EntityContext) =
    drawTargetingIndicator sb pixel ctx.InputMode ctx.MouseWorldPos entityCtx
