namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input.Touch
open Pomo.Lib.Domain

module VirtualInputSystem =

  [<Struct>]
  type VirtualJoystick = {
    Center: Vector2
    ThumbPosition: Vector2
    Radius: float32
    IsActive: bool
    TouchId: int voption
  }

  [<Struct>]
  type VirtualButton = {
    Bounds: Rectangle
    IsPressed: bool
    Action: GameAction
  }

  type VirtualInputState = {
    Joystick: VirtualJoystick
    Buttons: VirtualButton[]
  }

  let create (viewport: Viewport) (scale: float32) =
    let joystickRadius = 80f * scale
    let joystickPadding = 50f * scale

    let joystickCenter =
      Vector2(
        float32 viewport.X + joystickRadius + joystickPadding,
        float32 viewport.Height - joystickRadius - joystickPadding
      )

    let joystick = {
      Center = joystickCenter
      ThumbPosition = joystickCenter
      Radius = joystickRadius
      IsActive = false
      TouchId = ValueNone
    }

    let buttonSize = int(80f * scale)
    let buttonPadding = int(25f * scale)
    let screenWidth = viewport.Width
    let screenHeight = viewport.Height

    let buttons =
      [|
        GameAction.UseQuickSlot1, 0
        GameAction.UseQuickSlot2, 1
        GameAction.UseQuickSlot3, 2
        GameAction.UseQuickSlot4, 3
      |]
      |> Array.map(fun (action, index) ->
        let x = screenWidth - (buttonSize + buttonPadding) * (index + 1)
        let y = screenHeight - (buttonSize + buttonPadding)

        {
          Bounds = Rectangle(x, y, buttonSize, buttonSize)
          IsPressed = false
          Action = action
        })

    {
      Joystick = joystick
      Buttons = buttons
    }

  let update (state: VirtualInputState) (touchState: TouchCollection) =
    let mutable joystick = state.Joystick

    // Update joystick
    joystick <-
      match joystick.TouchId with
      | ValueSome id ->
        match touchState |> Seq.tryFind(fun t -> t.Id = id) with
        | Some touch when
          touch.State = TouchLocationState.Moved
          || touch.State = TouchLocationState.Pressed
          ->
          let mutable thumbPos = touch.Position
          let delta = thumbPos - joystick.Center

          if delta.LengthSquared() > joystick.Radius * joystick.Radius then
            let d = Vector2.Normalize(delta)
            thumbPos <- joystick.Center + d * joystick.Radius

          {
            joystick with
                ThumbPosition = thumbPos
                IsActive = true
          }
        | _ ->
            // Released or invalid
            {
              joystick with
                  ThumbPosition = joystick.Center
                  IsActive = false
                  TouchId = ValueNone
            }
      | ValueNone ->
        // Find a new touch for the joystick
        touchState
        |> Seq.tryFind(fun touch ->
          let dist2 = Vector2.DistanceSquared(touch.Position, joystick.Center)

          touch.State = TouchLocationState.Pressed
          && dist2 < joystick.Radius * joystick.Radius * 4.0f)
        |> function
          | Some touch -> {
              joystick with
                  TouchId = ValueSome touch.Id
                  IsActive = true
            }
          | None -> joystick

    // Update buttons
    let buttons =
      state.Buttons
      |> Array.map(fun button ->
        let isPressed =
          touchState
          |> Seq.exists(fun touch -> button.Bounds.Contains(touch.Position))

        { button with IsPressed = isPressed })

    {
      Joystick = joystick
      Buttons = buttons
    }

  let getJoystickDirection(state: VirtualInputState) =
    if state.Joystick.IsActive then
      let delta = state.Joystick.ThumbPosition - state.Joystick.Center

      if delta.LengthSquared() > 0.01f then
        ValueSome(Vector2.Normalize(delta))
      else
        ValueNone
    else
      ValueNone

  let getPressedButtons(state: VirtualInputState) =
    state.Buttons
    |> Array.filter(fun b -> b.IsPressed)
    |> Array.map(fun b -> b.Action)

  let private gameActionToLabel (action: GameAction) =
      match action with
      | GameAction.UseQuickSlot1 -> "Q"
      | GameAction.UseQuickSlot2 -> "W"
      | GameAction.UseQuickSlot3 -> "E"
      | GameAction.UseQuickSlot4 -> "R"
      | _ -> ""

  let draw
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (font: SpriteFont)
    (state: VirtualInputState)
    (scale: float32)
    (inputMode: InputMode)
    (keybindingConfig: KeybindingSystem.KeybindingConfig)
    =
    let joystickColor = Color(128, 128, 128, 150)
    let thumbColor = Color(200, 200, 200, 200)
    let buttonColor = Color(80, 80, 80, 180)
    let buttonPressedColor = Color(120, 120, 120, 220)
    let buttonTargetingColor = Color(200, 160, 0, 220)
    let textColor = Color.White
    // Draw joystick
    let rect =
      Rectangle(
        int(state.Joystick.Center.X - state.Joystick.Radius),
        int(state.Joystick.Center.Y - state.Joystick.Radius),
        int(state.Joystick.Radius * 2f),
        int(state.Joystick.Radius * 2f)
      )

    sb.Draw(pixel, rect, joystickColor)

    let thumbRadius = state.Joystick.Radius * 0.6f

    let thumbRect =
      Rectangle(
        int(state.Joystick.ThumbPosition.X - thumbRadius),
        int(state.Joystick.ThumbPosition.Y - thumbRadius),
        int(thumbRadius * 2f),
        int(thumbRadius * 2f)
      )

    sb.Draw(pixel, thumbRect, thumbColor)

    // Draw buttons
    for button in state.Buttons do
      let mutable color =
        if button.IsPressed then
          buttonPressedColor
        else
          buttonColor

      match inputMode with
      | InputMode.AbilityTargeting(abilityId, _) ->
        let slotAction =
          KeybindingSystem.getSlotAction button.Action keybindingConfig

        match slotAction with
        | KeybindingSystem.ActivateAbility aId when aId = abilityId ->
          color <- buttonTargetingColor
        | _ -> ()
      | _ -> ()


      sb.Draw(pixel, button.Bounds, color)
      let label = gameActionToLabel button.Action
      let labelSize = font.MeasureString(label) * scale
      let labelPos = button.Bounds.Center.ToVector2() - labelSize * 0.5f
      sb.DrawString(font, label, labelPos, textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f)
