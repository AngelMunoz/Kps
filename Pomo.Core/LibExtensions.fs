[<AutoOpen>]
module Pomo.Core.LibExtensions


open Microsoft.Xna.Framework
open Pomo.Lib.Domain

module Position =

  let inline toVector2(pos: Position) : Vector2 = Vector2(pos.X, pos.Y)

  let inline ofVector2(v: Vector2) : Position = { X = v.X; Y = v.Y }
