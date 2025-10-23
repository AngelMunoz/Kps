namespace Pomo.Core

open System
open FSharp.Data.Adaptive
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Pomo.Lib.Domain

[<Struct>]
type TargetingMode =
  | EntityTargeting
  | GroundTargeting of radius: float32

[<Struct>]
type InputMode =
  | Normal
  | AbilityTargeting of abilityId: int<AbilityId> * mode: TargetingMode

[<Struct>]
type GameAction =
  | PrimaryAction
  | SecondaryAction
  | Move
  | View
  | UseQuickSlot1
  | UseQuickSlot2
  | UseQuickSlot3
  | UseQuickSlot4
  | UseQuickSlot5
  | UseQuickSlot6
  | UseQuickSlot7
  | UseQuickSlot8
  | SwitchToActionSet1
  | SwitchToActionSet2
  | SwitchToActionSet3
  | SwitchToActionSet4
  | SwitchToActionSet5
  | ToggleInventory
  | ToggleCharacterSheet
  | ToggleAbilities
  | ToggleJournal
  | Cancel
  | DebugAction1
  | DebugAction2
  | DebugAction3
  | DebugAction4
  | DebugAction5
  | DebugAction6
  | DebugAction7
  | DebugAction8

[<Struct>]
type MouseButton =
  | Left
  | Right
  | Middle

[<Struct>]
type Side =
  | Left
  | Right

[<Struct>]
type RawInput =
  | Key of key: Keys
  | MouseButton of mouseBtn: MouseButton
  | GamePadButton of btn: Buttons
  | GamePadTrigger of PlayerIndex * side: Side
  | GamePadThumbStick of PlayerIndex * side: Side
  | Touch
  | LongPress of duration: float32


[<Struct>]
type ActionState =
  | JustPressed
  | Held
  | JustReleased
  | Analog of Vector2

type InputMap = HashMap<RawInput, GameAction>

module ActionInputManager =
  open Microsoft.Xna.Framework.Input.Touch

  type TouchInfo = {
    StartTime: TimeSpan
    mutable LongPressTriggered: bool
  }

  type State = {
    mutable ActionStates: HashMap<GameAction, ActionState>
    PrevKeyboardState: KeyboardState
    PrevMouseState: MouseState
    PrevGamePadState: GamePadState
    PrevTouchCollection: TouchCollection
    ActiveTouches: HashMap<int, TouchInfo>
  }

  let create
    (
      keyboard: KeyboardState,
      mouse: MouseState,
      gamePad: GamePadState,
      touch: TouchCollection
    ) =
    {
      ActionStates = HashMap.empty
      PrevKeyboardState = keyboard
      PrevMouseState = mouse
      PrevGamePadState = gamePad
      PrevTouchCollection = touch
      ActiveTouches = HashMap.empty
    }

  let private updateActionStates
    (currentStates: HashMap<GameAction, ActionState>)
    =
    let newStates = HashMap.empty

    currentStates
    |> HashMap.fold
      (fun (acc: HashMap<_, _>) key value ->
        match value with
        | JustPressed -> acc.Add(key, Held)
        | Held -> acc.Add(key, Held)
        | Analog v -> acc.Add(key, Analog v)
        | JustReleased -> acc)
      newStates

  let private isKeyJustPressed
    (prev: KeyboardState)
    (curr: KeyboardState)
    (key: Keys)
    =
    curr.IsKeyDown(key) && prev.IsKeyUp(key)

  let private isKeyJustReleased
    (prev: KeyboardState)
    (curr: KeyboardState)
    (key: Keys)
    =
    curr.IsKeyUp(key) && prev.IsKeyDown(key)

  let private isMouseButtonJustPressed
    (prev: MouseState)
    (curr: MouseState)
    (btn: MouseButton)
    =
    match btn with
    | MouseButton.Left ->
      curr.LeftButton = ButtonState.Pressed
      && prev.LeftButton = ButtonState.Released
    | MouseButton.Right ->
      curr.RightButton = ButtonState.Pressed
      && prev.RightButton = ButtonState.Released
    | MouseButton.Middle ->
      curr.MiddleButton = ButtonState.Pressed
      && prev.MiddleButton = ButtonState.Released

  let private isMouseButtonJustReleased
    (prev: MouseState)
    (curr: MouseState)
    (btn: MouseButton)
    =
    match btn with
    | MouseButton.Left ->
      curr.LeftButton = ButtonState.Released
      && prev.LeftButton = ButtonState.Pressed
    | MouseButton.Right ->
      curr.RightButton = ButtonState.Released
      && prev.RightButton = ButtonState.Pressed
    | MouseButton.Middle ->
      curr.MiddleButton = ButtonState.Released
      && prev.MiddleButton = ButtonState.Pressed

  let private isMouseButtonDown (curr: MouseState) (btn: MouseButton) =
    match btn with
    | MouseButton.Left -> curr.LeftButton = ButtonState.Pressed
    | MouseButton.Right -> curr.RightButton = ButtonState.Pressed
    | MouseButton.Middle -> curr.MiddleButton = ButtonState.Pressed

  let private isTouchDown(touchState: TouchCollection) = touchState.Count > 0

  let private isTouchJustPressed
    (prevState: TouchCollection)
    (currState: TouchCollection)
    =
    currState.Count > 0 && prevState.Count = 0

  let private isTouchJustReleased
    (prevState: TouchCollection)
    (currState: TouchCollection)
    =
    currState.Count = 0 && prevState.Count > 0

  let update
    (inputMap: InputMap)
    (state: State)
    (gameTime: GameTime)
    (
      keyboard: KeyboardState,
      mouse: MouseState,
      gamePad: GamePadState,
      touch: TouchCollection
    ) =
    let mutable newStates = updateActionStates state.ActionStates
    let mutable activeTouches = state.ActiveTouches

    for t in touch do
      if not(activeTouches.ContainsKey(t.Id)) then
        activeTouches <-
          activeTouches.Add(
            t.Id,
            {
              StartTime = gameTime.TotalGameTime
              LongPressTriggered = false
            }
          )

    for t in state.PrevTouchCollection do
      if not(touch.Contains t) then
        activeTouches <- activeTouches.Remove t.Id

    for rawInput, gameAction in inputMap do

      let isDown, isJustPressed, isJustReleased =
        match rawInput with
        | Key k ->
          keyboard.IsKeyDown(k),
          isKeyJustPressed state.PrevKeyboardState keyboard k,
          isKeyJustReleased state.PrevKeyboardState keyboard k
        | MouseButton mb ->
          isMouseButtonDown mouse mb,
          isMouseButtonJustPressed state.PrevMouseState mouse mb,
          isMouseButtonJustReleased state.PrevMouseState mouse mb
        | GamePadButton b ->
          gamePad.IsButtonDown(b),
          gamePad.IsButtonDown(b) && state.PrevGamePadState.IsButtonUp(b),
          gamePad.IsButtonUp(b) && state.PrevGamePadState.IsButtonDown(b)
        | Touch ->
          isTouchDown touch,
          isTouchJustPressed state.PrevTouchCollection touch,
          isTouchJustReleased state.PrevTouchCollection touch
        | LongPress duration ->
          let mutable longPressJustTriggered = false

          for _, touchInfo in activeTouches do
            if not touchInfo.LongPressTriggered then
              let elapsed =
                (gameTime.TotalGameTime - touchInfo.StartTime).TotalSeconds

              if elapsed > float duration then
                longPressJustTriggered <- true
                touchInfo.LongPressTriggered <- true

          false, longPressJustTriggered, false
        | _ -> false, false, false

      if isJustPressed then
        newStates <- newStates |> HashMap.add gameAction JustPressed
      elif isJustReleased then
        if newStates |> HashMap.containsKey gameAction |> not then
          newStates <- newStates |> HashMap.add gameAction JustReleased
        else
          ()
      elif isDown then
        if newStates |> HashMap.containsKey gameAction |> not then
          newStates <- newStates |> HashMap.add gameAction Held

    let leftStick = gamePad.ThumbSticks.Left

    if leftStick.LengthSquared() > 0.01f then
      newStates <- newStates |> HashMap.add GameAction.Move (Analog leftStick)

    let rightStick = gamePad.ThumbSticks.Right

    if rightStick.LengthSquared() > 0.01f then
      newStates <- newStates |> HashMap.add GameAction.View (Analog rightStick)

    state.ActionStates <- newStates

    {
      state with
          PrevKeyboardState = keyboard
          PrevMouseState = mouse
          PrevGamePadState = gamePad
          PrevTouchCollection = touch
          ActiveTouches = activeTouches
    }

  let inline getActionState (action: GameAction) (state: State) =
    state.ActionStates |> HashMap.tryFindV(action)

  let isActionPressed (action: GameAction) (state: State) =
    match getActionState action state with
    | ValueSome JustPressed -> true
    | _ -> false

  let isActionHeld (action: GameAction) (state: State) =
    match getActionState action state with
    | ValueSome JustPressed
    | ValueSome Held -> true
    | _ -> false

  let isActionReleased (action: GameAction) (state: State) =
    match getActionState action state with
    | ValueSome JustReleased -> true
    | _ -> false

  let getActionAnalog (action: GameAction) (state: State) =
    match getActionState action state with
    | ValueSome(Analog v) -> Some v
    | _ -> None

  let createDefaultMap() =
    HashMap.ofSeqV [
      if Platform.IsMobile() then
        Touch, PrimaryAction
        LongPress 0.5f, SecondaryAction
      else
        MouseButton MouseButton.Left, PrimaryAction
        MouseButton MouseButton.Right, SecondaryAction
      Key Keys.Q, UseQuickSlot1
      Key Keys.W, UseQuickSlot2
      Key Keys.E, UseQuickSlot3
      Key Keys.R, UseQuickSlot4
      Key Keys.A, UseQuickSlot5
      Key Keys.S, UseQuickSlot6
      Key Keys.D, UseQuickSlot7
      Key Keys.F, UseQuickSlot8
      Key Keys.D1, SwitchToActionSet1
      Key Keys.D2, SwitchToActionSet2
      Key Keys.D3, SwitchToActionSet3
      Key Keys.D4, SwitchToActionSet4
      Key Keys.D5, SwitchToActionSet5
      Key Keys.Z, ToggleInventory
      Key Keys.X, ToggleCharacterSheet
      Key Keys.C, ToggleAbilities
      Key Keys.V, ToggleJournal
      Key Keys.Escape, Cancel
      Key Keys.F1, DebugAction1
      Key Keys.F2, DebugAction2
      Key Keys.F3, DebugAction3
      Key Keys.F4, DebugAction4
      Key Keys.F5, DebugAction5
    ]


module InputActionPatterns =

  let (|PressedActions|)(state: ActionInputManager.State) =
    state.ActionStates
    |> HashMap.chooseV(fun action actionState ->
      match actionState with
      | JustPressed -> ValueSome action
      | _ -> ValueNone)
    |> HashMap.toValueArray
