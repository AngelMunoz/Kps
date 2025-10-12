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

    sb.End()
