namespace Pomo.Core

open System.Diagnostics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.State
open Pomo.Lib.Gameplay
open Pomo.Lib.Operations
open Pomo.Lib.Rules
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Scenario
open Pomo.Lib.Pathfinding
open FSharp.Data.Adaptive

module InputHandlerSystem =

  type MouseClickResult =
    | NoAction
    | EntitySelected of Guid<EntityId>
    | SelectionCleared
    | AbilityActivatedOnEntity of
      abilityId: int<AbilityId> *
      targetId: Guid<EntityId>
    | AbilityActivatedAtPosition of
      abilityId: int<AbilityId> *
      position: Position
    | AbilityTargetMissed

  type NavigationResult = {
    CurrentPath: Position[]
    PathPreview: PathPreview.PathSegment[]
  }

  let handleRightClick
    (state: GameState)
    (playerId: Guid<EntityId>)
    (clickWorld: Vector2)
    (scenario: ScenarioState)
    =
    let moveCmd =
      Rules.Navigate {
        actor = playerId
        destination = { X = clickWorld.X; Y = clickWorld.Y }
      }

    let stateChange = CommandHandler.evaluate state moveCmd |> AVal.force

    AudioSystem.processAudioChanges
      state.services.audioStore
      scenario
      stateChange.audioChanges

    GameState.apply state stateChange

    let playerComp = scenario.entities[playerId]

    let entityRadius =
      match playerComp.Identity.Stage with
      | Stage.First -> 12f
      | Stage.Second -> 16f
      | Stage.Third -> 20f

    match playerComp.Movement.Path with
    | [] -> {
        CurrentPath = Array.empty
        PathPreview = Array.empty
      }
    | waypoints ->
      let fullPath =
        Array.concat [| [| playerComp.Position |]; waypoints |> List.toArray |]

      let preview =
        PathPreview.generatePreview scenario.scenario fullPath entityRadius

      Debug.WriteLine(
        $"[Pathfinding] Preview generated for {fullPath.Length} points"
      )

      {
        CurrentPath = fullPath
        PathPreview = preview
      }

  let handleLeftClick
    (state: GameState)
    (playerId: Guid<EntityId>)
    (world: Vector2)
    (scenario: ScenarioState)
    (inputMode: InputManager.InputMode)
    =
    let entities = scenario.entities |> AMap.force |> HashMap.toArrayV

    let inline radiusOfStage s =
      match s with
      | First -> 12f
      | Second -> 16f
      | Third -> 20f

    let mutable found: Guid<EntityId> voption = ValueNone

    for struct (id, comp) in entities do
      let dx = world.X - comp.Position.X
      let dy = world.Y - comp.Position.Y
      let r = radiusOfStage comp.Identity.Stage
      let dist2 = dx * dx + dy * dy
      let inside = dist2 <= r * r

      if inside then
        found <- ValueSome id

    match inputMode with
    | InputManager.InputMode.Normal ->
      match found with
      | ValueSome sid ->
        Debug.WriteLine($"[Input] Selected {sid}")
        EntitySelected sid
      | ValueNone ->
        Debug.WriteLine("[Input] Selection cleared")
        SelectionCleared
    | InputManager.InputMode.AbilityTargeting(abilityId,
                                              InputManager.TargetingMode.EntityTargeting) ->
      match found with
      | ValueSome targetId ->
        let stateChange =
          GameState.activateAbility playerId abilityId [| targetId |] state
          |> AVal.force

        AudioSystem.processAudioChanges
          state.services.audioStore
          scenario
          stateChange.audioChanges

        GameState.apply state stateChange

        Debug.WriteLine($"[Ability] Activated {abilityId} on {targetId}")
        AbilityActivatedOnEntity(abilityId, targetId)
      | ValueNone ->
        Debug.WriteLine("[Ability] No target selected.")
        AbilityTargetMissed
    | InputManager.InputMode.AbilityTargeting(abilityId,
                                              InputManager.TargetingMode.GroundTargeting _) ->
      let targetPos = { X = world.X; Y = world.Y }

      let stateChange =
        GameState.activateAbilityAtPosition playerId abilityId targetPos state
        |> AVal.force

      AudioSystem.processAudioChanges
        state.services.audioStore
        scenario
        stateChange.audioChanges

      GameState.apply state stateChange

      Debug.WriteLine(
        $"[Ability] Activated {abilityId} at position ({targetPos.X}, {targetPos.Y})"
      )

      AbilityActivatedAtPosition(abilityId, targetPos)

  let handleUIKeys
    (keyboardState: KeyboardState)
    (inputState: InputManager.InputState)
    (uiState: UISystem.UIState)
    =
    let keyF1 = keyboardState.IsKeyDown(Keys.F1)
    let keyF2 = keyboardState.IsKeyDown(Keys.F2)
    let keyF3 = keyboardState.IsKeyDown(Keys.F3)
    let keyF4 = keyboardState.IsKeyDown(Keys.F4)

    let mutable newInputState = inputState
    let mutable newUIState = uiState
    let mutable toggleGrid = false

    if keyF1 && not inputState.PrevKey2Down then
      newUIState <- UISystem.togglePanel UISystem.CharacterSheet newUIState
      Debug.WriteLine("[UI] Character sheet toggled")

    newInputState <- {
      newInputState with
          PrevKey2Down = keyF1
    }

    if keyF2 && not inputState.PrevKeyVDown then
      newUIState <- UISystem.togglePanel UISystem.EquipmentView newUIState
      Debug.WriteLine("[UI] Equipment view toggled")

    newInputState <- {
      newInputState with
          PrevKeyVDown = keyF2
    }

    if keyF3 && not inputState.PrevKeyEDown then
      newUIState <- UISystem.togglePanel UISystem.AbilityList newUIState
      Debug.WriteLine("[UI] Ability list toggled")

    newInputState <- {
      newInputState with
          PrevKeyEDown = keyF3
    }

    if keyF4 && not inputState.PrevKeyADown then
      toggleGrid <- true
      Debug.WriteLine("[Debug] showPathfindingGrid toggled")

    newInputState <- {
      newInputState with
          PrevKeyADown = keyF4
    }

    struct (toggleGrid, newInputState, newUIState)

  let handleDebugKeys
    (state: GameState)
    (playerId: Guid<EntityId>)
    (scenario: ScenarioState)
    (keyboardState: KeyboardState)
    (inputState: InputManager.InputState)
    =
    let keyF5 = keyboardState.IsKeyDown(Keys.F5)
    let mutable newInputState = inputState

    if keyF5 && not inputState.PrevKeyRDown then
      let replenishCmd =
        Rules.ReplenishResources [|
          {
            Actor = playerId
            ResourceType = ResourceType.MP
            Amount = 1000
          }
        |]

      let stateChange = CommandHandler.evaluate state replenishCmd |> AVal.force

      AudioSystem.processAudioChanges
        state.services.audioStore
        scenario
        stateChange.audioChanges

      GameState.apply state stateChange
      Debug.WriteLine("[Debug] MP replenished")

    newInputState <- {
      newInputState with
          PrevKeyRDown = keyF5
    }

    newInputState

  let handleKeybindingInput
    (state: GameState)
    (playerId: Guid<EntityId>)
    (scenario: ScenarioState)
    (keyboardState: KeyboardState)
    (inputState: InputManager.InputState)
    (keybindingConfig: KeybindingSystem.KeybindingConfig)
    =
    let mutable newInputState = inputState
    let mutable newConfig = keybindingConfig
    let mutable result = KeybindingSystem.NoAction

    for set in KeybindingSystem.allSets do
      let key = KeybindingSystem.setToKey set
      let isDown = keyboardState.IsKeyDown(key)

      let wasDown =
        match set with
        | KeybindingSystem.Set1 -> inputState.PrevKey1Down
        | KeybindingSystem.Set2 -> inputState.PrevKey2Down
        | KeybindingSystem.Set3 -> inputState.PrevKey3Down
        | KeybindingSystem.Set4 -> inputState.PrevKey4Down
        | KeybindingSystem.Set5 -> inputState.PrevKey5Down

      if isDown && not wasDown then
        newConfig <- KeybindingSystem.setActiveSet set newConfig
        Debug.WriteLine($"[Keybinding] Switched to action set {set}")

    for slot in KeybindingSystem.allSlots do
      let key = KeybindingSystem.slotToKey slot
      let isDown = keyboardState.IsKeyDown(key)

      let wasDown =
        match slot with
        | KeybindingSystem.Q -> inputState.PrevKeyQDown
        | KeybindingSystem.W -> inputState.PrevKeyWDown
        | KeybindingSystem.E -> inputState.PrevKeyEKeyDown
        | KeybindingSystem.R -> inputState.PrevKeyRKeyDown
        | KeybindingSystem.A -> inputState.PrevKeyAKeyDown
        | KeybindingSystem.S -> inputState.PrevKeySDown
        | KeybindingSystem.D -> inputState.PrevKeyDDown
        | KeybindingSystem.F -> inputState.PrevKeyFDown

      if isDown && not wasDown then
        let action = KeybindingSystem.getSlotAction slot keybindingConfig

        match action with
        | KeybindingSystem.ActivateAbility abilityId ->
          match
            KeybindingSystem.getAbilityTargetingMode
              state.services.abilityStore
              abilityId
          with
          | ValueSome targetingMode ->
            result <-
              KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode)

            Debug.WriteLine(
              $"[Keybinding] Slot {slot} entering targeting for ability {abilityId}"
            )
          | ValueNone ->
            result <- KeybindingSystem.ExecuteSelfAbility abilityId

            Debug.WriteLine(
              $"[Keybinding] Slot {slot} executing self ability {abilityId}"
            )
        | KeybindingSystem.UseItem itemId ->
          result <- KeybindingSystem.UseItemAction itemId
          Debug.WriteLine($"[Keybinding] Slot {slot} used item {itemId}")
        | KeybindingSystem.Empty -> ()

      newInputState <-
        match slot with
        | KeybindingSystem.Q -> {
            newInputState with
                PrevKeyQDown = isDown
          }
        | KeybindingSystem.W -> {
            newInputState with
                PrevKeyWDown = isDown
          }
        | KeybindingSystem.E -> {
            newInputState with
                PrevKeyEKeyDown = isDown
          }
        | KeybindingSystem.R -> {
            newInputState with
                PrevKeyRKeyDown = isDown
          }
        | KeybindingSystem.A -> {
            newInputState with
                PrevKeyAKeyDown = isDown
          }
        | KeybindingSystem.S -> {
            newInputState with
                PrevKeySDown = isDown
          }
        | KeybindingSystem.D -> {
            newInputState with
                PrevKeyDDown = isDown
          }
        | KeybindingSystem.F ->
            {
              newInputState with
                  PrevKeyFDown = isDown
            }

    match result with
    | KeybindingSystem.ExecuteSelfAbility abilityId ->
      let stateChange =
        GameState.activateAbility playerId abilityId [| playerId |] state
        |> AVal.force

      AudioSystem.processAudioChanges
        state.services.audioStore
        scenario
        stateChange.audioChanges

      GameState.apply state stateChange
    | _ -> ()

    struct (newInputState, newConfig, result)

  type InputUpdateResult = {
    InputState: InputManager.InputState
    UIState: UISystem.UIState
    KeybindingConfig: KeybindingSystem.KeybindingConfig
    InputMode: InputManager.InputMode voption
    Selected: Guid<EntityId> voption
    ShowPathfindingGrid: bool
    CurrentPath: Position[]
    PathPreview: PathPreview.PathSegment[]
  }

  let handleAllInput
    (state: GameState)
    (playerId: Guid<EntityId>)
    (scenario: ScenarioState)
    (world: Vector2)
    (gameTime: GameTime)
    (inputState: InputManager.InputState)
    (uiState: UISystem.UIState)
    (keybindingConfig: KeybindingSystem.KeybindingConfig)
    (inputMode: InputManager.InputMode)
    (selected: Guid<EntityId> voption)
    (showPathfindingGrid: bool)
    (currentPath: Position[])
    (pathPreview: PathPreview.PathSegment[])
    (clickThrottle: InputManager.ClickThrottleState)
    =
    let mutable result = {
      InputState = inputState
      UIState = uiState
      KeybindingConfig = keybindingConfig
      InputMode = ValueNone
      Selected = selected
      ShowPathfindingGrid = showPathfindingGrid
      CurrentPath = currentPath
      PathPreview = pathPreview
    }

    let rightMouseDown = InputManager.isRightClickPressed()
    let mouseDown = InputManager.isLeftClickPressed()
    let keyboardState = Keyboard.GetState()

    InputManager.updateThrottle clickThrottle gameTime.ElapsedGameTime

    if rightMouseDown && not inputState.PrevRightMouseDown then
      if inputMode <> InputManager.InputMode.Normal then
        result <- {
          result with
              InputMode = ValueSome InputManager.InputMode.Normal
        }
      else
        match InputManager.tryThrottleClick clickThrottle (ValueSome world) with
        | ValueSome clickWorld ->
          let navResult = handleRightClick state playerId clickWorld scenario

          result <- {
            result with
                CurrentPath = navResult.CurrentPath
                PathPreview = navResult.PathPreview
          }
        | ValueNone -> ()

    result <- {
      result with
          InputState = {
            result.InputState with
                PrevRightMouseDown = rightMouseDown
          }
    }

    if mouseDown && not inputState.PrevMouseDown then
      let clickResult = handleLeftClick state playerId world scenario inputMode

      match clickResult with
      | EntitySelected entityId ->
        result <- {
          result with
              Selected = ValueSome entityId
        }
      | SelectionCleared -> result <- { result with Selected = ValueNone }
      | AbilityActivatedOnEntity _
      | AbilityActivatedAtPosition _
      | AbilityTargetMissed ->
        result <- {
          result with
              InputMode = ValueSome InputManager.InputMode.Normal
        }
      | NoAction -> ()

    result <- {
      result with
          InputState = {
            result.InputState with
                PrevMouseDown = mouseDown
          }
    }

    let struct (toggleGrid, newInputState2, newUIState) =
      handleUIKeys keyboardState result.InputState result.UIState

    result <- {
      result with
          InputState = newInputState2
          UIState = newUIState
          ShowPathfindingGrid =
            if toggleGrid then
              not result.ShowPathfindingGrid
            else
              result.ShowPathfindingGrid
    }

    result <- {
      result with
          InputState =
            handleDebugKeys
              state
              playerId
              scenario
              keyboardState
              result.InputState
    }

    let struct (newInputState3, newKeybindingConfig, keybindingResult) =
      handleKeybindingInput
        state
        playerId
        scenario
        keyboardState
        result.InputState
        result.KeybindingConfig

    result <- {
      result with
          InputState = newInputState3
          KeybindingConfig = newKeybindingConfig
    }

    match keybindingResult with
    | KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode) ->
      result <- {
        result with
            InputMode =
              ValueSome(
                InputManager.InputMode.AbilityTargeting(
                  abilityId,
                  targetingMode
                )
              )
      }
    | _ -> ()

    result
