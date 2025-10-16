namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Pomo.Lib.Domain

module InputManager =
  [<Struct>]
  type InputMode =
    | Normal
    | AbilityTargeting of abilityId: int<AbilityId>

  let inline screenToWorld (screenPos: Vector2) (view: Matrix) =
    Vector2.Transform(screenPos, Matrix.Invert view)

  let inline isLeftClickPressed() =
    Mouse.GetState().LeftButton = ButtonState.Pressed

  let inline isRightClickPressed() =
    Mouse.GetState().RightButton = ButtonState.Pressed

  let inline isLeftClickReleased() =
    Mouse.GetState().LeftButton = ButtonState.Released

  let inline isRightClickReleased() =
    Mouse.GetState().RightButton = ButtonState.Released

  let inline getMousePosition() =
    let ms = Mouse.GetState()
    Vector2(float32 ms.X, float32 ms.Y)

  let inline isKeyPressed(key: Keys) = Keyboard.GetState() |> _.IsKeyDown(key)
