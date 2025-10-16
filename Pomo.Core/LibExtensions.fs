[<AutoOpen>]
module Pomo.Core.LibExtensions


open Microsoft.Xna.Framework
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Visuals

module Position =

  let inline toVector2(pos: Position) : Vector2 = Vector2(pos.X, pos.Y)

  let inline ofVector2(v: Vector2) : Position = { X = v.X; Y = v.Y }


module Color =

  let toMonoGameColor(color: VisualColor) =
    match color with
    | Red -> Color.Red
    | Green -> Color.Green
    | Blue -> Color.Blue
    | Yellow -> Color.Yellow
    | White -> Color.White
    | Purple -> Color.Purple
    | Orange -> Color.Orange
