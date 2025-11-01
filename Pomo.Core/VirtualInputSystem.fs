namespace Pomo.Core

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input.Touch
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.Scenario
open FSharp.Data.Adaptive

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

  type VirtualInputContext = {
    VirtualInputState: VirtualInputState voption
    PrevVirtualInputState: VirtualInputState voption
    TouchState: TouchCollection
    Scenario: ScenarioState
    PlayerId: Guid<EntityId>
    GameTime: GameTime
    KeybindingConfig: KeybindingSystem.KeybindingConfig
    State: GameState
    InputMode: InputMode
    DerivedStats: HashMap<Guid<EntityId>, DerivedStats>
  }

  type VirtualInputResult = {
    Commands: Rules.Command[]
    NewInputMode: InputMode
    NewVirtualInputState: VirtualInputState voption
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
        UseQuickSlot1, 0
        UseQuickSlot2, 1
        UseQuickSlot3, 2
        UseQuickSlot4, 3
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
            let d = Vector2.Normalize delta
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
          |> Seq.exists(fun touch -> button.Bounds.Contains touch.Position)

        { button with IsPressed = isPressed })

    {
      Joystick = joystick
      Buttons = buttons
    }

  let getJoystickDirection(state: VirtualInputState) =
    if state.Joystick.IsActive then
      let delta = state.Joystick.ThumbPosition - state.Joystick.Center

      if delta.LengthSquared() > 0.01f then
        ValueSome(Vector2.Normalize delta)
      else
        ValueNone
    else
      ValueNone

  let getPressedButtons(state: VirtualInputState) =
    state.Buttons
    |> Array.filter(fun b -> b.IsPressed)
    |> Array.map(fun b -> b.Action)

  let private gameActionToLabel(action: GameAction) =
    match action with
    | UseQuickSlot1 -> "Q"
    | UseQuickSlot2 -> "W"
    | UseQuickSlot3 -> "E"
    | UseQuickSlot4 -> "R"
    | _ -> ""

  let processInput(ctx: VirtualInputContext) : VirtualInputResult =
    let newVirtualInputState =
      ctx.VirtualInputState
      |> ValueOption.map(fun vs -> update vs ctx.TouchState)

    let commandList = ResizeArray<Rules.Command>()
    let mutable inputMode = ctx.InputMode

    newVirtualInputState
    |> ValueOption.iter(fun vinput ->
      // Joystick movement
      match getJoystickDirection vinput with
      | ValueSome direction ->
        let playerStats = ctx.DerivedStats |> HashMap.find ctx.PlayerId
        let movementSpeed = float32 playerStats.MovementSpeed
        let velocity = direction * movementSpeed
        let elapsed = float32 ctx.GameTime.ElapsedGameTime.TotalSeconds

        let moveCmd =
          Rules.AdvancePosition {
            actor = ctx.PlayerId
            velocity = { X = velocity.X; Y = velocity.Y }
            elapsed = elapsed
          }

        commandList.Add moveCmd
      | ValueNone -> ()

      // Virtual buttons
      let prevButtons =
        ctx.PrevVirtualInputState
        |> ValueOption.map(fun pvs -> pvs.Buttons)
        |> ValueOption.defaultValue Array.empty

      for i in 0 .. vinput.Buttons.Length - 1 do
        let button = vinput.Buttons[i]

        let prevButton =
          prevButtons |> Array.tryFind(fun pb -> pb.Action = button.Action)

        let wasPressed =
          prevButton
          |> Option.map(fun pb -> pb.IsPressed)
          |> Option.defaultValue false

        if button.IsPressed && not wasPressed then
          let kbResult =
            KeybindingSystem.processSlotAction
              button.Action
              ctx.KeybindingConfig
              ctx.State
              ctx.PlayerId

          match kbResult with
          | KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode) ->
            inputMode <- AbilityTargeting(abilityId, targetingMode)
          | _ -> ())

    {
      Commands = commandList.ToArray()
      NewInputMode = inputMode
      NewVirtualInputState = newVirtualInputState
    }

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
        if button.IsPressed then buttonPressedColor else buttonColor

      match inputMode with
      | AbilityTargeting(abilityId, _) ->
        let slotAction =
          KeybindingSystem.getSlotAction button.Action keybindingConfig

        match slotAction with
        | KeybindingSystem.ActivateAbility aId when aId = abilityId ->
          color <- buttonTargetingColor
        | _ -> ()
      | _ -> ()


      sb.Draw(pixel, button.Bounds, color)
      let label = gameActionToLabel button.Action
      let labelSize = font.MeasureString label * scale
      let labelPos = button.Bounds.Center.ToVector2() - labelSize * 0.5f

      sb.DrawString(
        font,
        label,
        labelPos,
        textColor,
        0f,
        Vector2.Zero,
        scale,
        SpriteEffects.None,
        0f
      )
