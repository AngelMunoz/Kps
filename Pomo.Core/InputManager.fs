namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input

module InputManager =
  let screenToWorld (screenPos: Vector2) (view: Matrix) =
    let inv = Matrix.Invert view
    Vector2.Transform(screenPos, inv)

  let isLeftClickPressed() =
    Mouse.GetState().LeftButton = ButtonState.Pressed

  let isRightClickPressed() =
    Mouse.GetState().RightButton = ButtonState.Pressed

  let isLeftClickReleased() =
    Mouse.GetState().LeftButton = ButtonState.Released

  let isRightClickReleased() =
    Mouse.GetState().RightButton = ButtonState.Released

  let getMousePosition() =
    let ms = Mouse.GetState()
    Vector2(float32 ms.X, float32 ms.Y)

  let isKeyPressed(key: Keys) =
    let ks = Keyboard.GetState()
    ks.IsKeyDown(key)
