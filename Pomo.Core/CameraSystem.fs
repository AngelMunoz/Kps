namespace Pomo.Core

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Graphics

module CameraSystem =

  type CameraState = {
    Position: Vector2
    Zoom: float32
    PrevScrollValue: int
  }

  let createCamera() = {
    Position = Vector2.Zero
    Zoom = 1.0f
    PrevScrollValue = Mouse.GetState().ScrollWheelValue
  }

  let updateZoom(state: CameraState) =
    let wheel = Mouse.GetState().ScrollWheelValue
    let delta = wheel - state.PrevScrollValue

    if delta <> 0 then
      let dz = float32 delta * 0.001f
      let mutable z = state.Zoom + dz

      if z < 0.5f then
        z <- 0.5f

      if z > 2.0f then
        z <- 2.0f

      {
        state with
            Zoom = z
            PrevScrollValue = wheel
      }
    else
      state

  let setZoom (zoom: float32) (state: CameraState) = { state with Zoom = zoom }

  let setPosition (position: Vector2) (state: CameraState) = {
    state with
        Position = position
  }

  let createViewMatrix (state: CameraState) (viewport: Viewport) =
    let halfW = float32 viewport.Width / 2.0f
    let halfH = float32 viewport.Height / 2.0f

    Matrix.CreateTranslation(-state.Position.X, -state.Position.Y, 0f)
    * Matrix.CreateScale(state.Zoom)
    * Matrix.CreateTranslation(halfW, halfH, 0f)

  let screenToWorld (screenPos: Vector2) (viewMatrix: Matrix) =
    let invertedView = Matrix.Invert(viewMatrix)
    Vector2.Transform(screenPos, invertedView)
