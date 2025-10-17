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

  type PlayerInputState = {
    MoveDirection: Vector2
    Velocity: Vector2
    IsAccelerating: bool
    TimeSinceLastInput: float32
  }

  let createInitialState () = {
    MoveDirection = Vector2.Zero
    Velocity = Vector2.Zero
    IsAccelerating = false
    TimeSinceLastInput = 0.0f
  }

  let updateMovement (state: PlayerInputState) (keyboard: KeyboardState) (gameTime: GameTime) (maxSpeed: float32) =
    let mutable moveDirection = Vector2.Zero
    if keyboard.IsKeyDown(Keys.Up) then moveDirection.Y <- moveDirection.Y - 1.0f
    if keyboard.IsKeyDown(Keys.Down) then moveDirection.Y <- moveDirection.Y + 1.0f
    if keyboard.IsKeyDown(Keys.Left) then moveDirection.X <- moveDirection.X - 1.0f
    if keyboard.IsKeyDown(Keys.Right) then moveDirection.X <- moveDirection.X + 1.0f

    let isAccelerating = moveDirection.LengthSquared() > 0.0f
    let deltaTime = float32 gameTime.ElapsedGameTime.TotalSeconds

    let newState = 
      if isAccelerating then
        moveDirection.Normalize()
        { state with 
            MoveDirection = moveDirection
            IsAccelerating = true 
            TimeSinceLastInput = 0.0f }
      else
        { state with 
            IsAccelerating = false
            TimeSinceLastInput = state.TimeSinceLastInput + deltaTime }

    // Acceleration and Deceleration logic
    let mutable velocity = state.Velocity
    let acceleration = maxSpeed * 1.5f
    let deceleration = maxSpeed * 2.0f

    if newState.IsAccelerating then
      velocity <- velocity + newState.MoveDirection * acceleration * deltaTime
      if velocity.LengthSquared() > maxSpeed * maxSpeed then
        velocity.Normalize()
        velocity <- velocity * maxSpeed
    else
      if velocity.LengthSquared() > 0.0f then
        let mutable decel = deceleration
        // Faster deceleration for quick taps
        if state.TimeSinceLastInput < 0.1f then
            decel <- decel * 3.0f

        let currentSpeed = velocity.Length()
        let newSpeed = max 0.0f (currentSpeed - decel * deltaTime)
        if newSpeed > 0.0f then
            velocity.Normalize()
            velocity <- velocity * newSpeed
        else
            velocity <- Vector2.Zero
      
    { newState with Velocity = velocity }

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
