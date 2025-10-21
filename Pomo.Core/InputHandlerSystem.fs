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

  let handleAbilityKeys
    (keyboardState: KeyboardState)
    (inputState: InputManager.InputState)
    =
    let key1 = keyboardState.IsKeyDown(Keys.D1)
    let key3 = keyboardState.IsKeyDown(Keys.D3)
    let key4 = keyboardState.IsKeyDown(Keys.D4)
    let key5 = keyboardState.IsKeyDown(Keys.D5)

    let mutable newInputMode = ValueNone
    let mutable newInputState = inputState

    if key1 && not inputState.PrevKey1Down then
      newInputMode <-
        ValueSome(
          InputManager.InputMode.AbilityTargeting(
            2<AbilityId>,
            InputManager.TargetingMode.EntityTargeting
          )
        )

      Debug.WriteLine(
        "[Input] Entered ability targeting mode for Fireball (ability 2)."
      )

    newInputState <- {
      newInputState with
          PrevKey1Down = key1
    }

    if key3 && not inputState.PrevKey3Down then
      newInputMode <-
        ValueSome(
          InputManager.InputMode.AbilityTargeting(
            102<AbilityId>,
            InputManager.TargetingMode.GroundTargeting 32.0f
          )
        )

      Debug.WriteLine(
        "[Input] Entered ground targeting mode for Arrow Shot (ability 102)."
      )

    newInputState <- {
      newInputState with
          PrevKey3Down = key3
    }

    if key4 && not inputState.PrevKey4Down then
      newInputMode <-
        ValueSome(
          InputManager.InputMode.AbilityTargeting(
            103<AbilityId>,
            InputManager.TargetingMode.GroundTargeting 64.0f
          )
        )

      Debug.WriteLine(
        "[Input] Entered ground targeting mode for Meteor Shower (ability 103)."
      )

    newInputState <- {
      newInputState with
          PrevKey4Down = key4
    }

    if key5 && not inputState.PrevKey5Down then
      newInputMode <-
        ValueSome(
          InputManager.InputMode.AbilityTargeting(
            104<AbilityId>,
            InputManager.TargetingMode.GroundTargeting 32.0f
          )
        )

      Debug.WriteLine(
        "[Input] Entered ground targeting mode for Magic Arrow (ability 104)."
      )

    newInputState <- {
      newInputState with
          PrevKey5Down = key5
    }

    struct (newInputMode, newInputState)

  let handleUIKeys
    (keyboardState: KeyboardState)
    (inputState: InputManager.InputState)
    (uiState: UISystem.UIState)
    =
    let keyF2 = keyboardState.IsKeyDown(Keys.F2)
    let keyV = keyboardState.IsKeyDown(Keys.V)
    let keyE = keyboardState.IsKeyDown(Keys.E)
    let keyA = keyboardState.IsKeyDown(Keys.A)

    let mutable newInputState = inputState
    let mutable newUIState = uiState
    let mutable toggleGrid = false

    if keyF2 && not inputState.PrevKey2Down then
      toggleGrid <- true
      Debug.WriteLine("[Debug] showPathfindingGrid toggled")

    newInputState <- {
      newInputState with
          PrevKey2Down = keyF2
    }

    if keyV && not inputState.PrevKeyVDown then
      newUIState <- UISystem.togglePanel UISystem.CharacterSheet newUIState

      Debug.WriteLine(
        $"[UI] Character sheet toggled: {newUIState.ActivePanels |> HashSet.contains UISystem.CharacterSheet}"
      )

    newInputState <- {
      newInputState with
          PrevKeyVDown = keyV
    }

    if keyE && not inputState.PrevKeyEDown then
      newUIState <- UISystem.togglePanel UISystem.EquipmentView newUIState

      Debug.WriteLine(
        $"[UI] Equipment view toggled: {newUIState.ActivePanels |> HashSet.contains UISystem.EquipmentView}"
      )

    newInputState <- {
      newInputState with
          PrevKeyEDown = keyE
    }

    if keyA && not inputState.PrevKeyADown then
      newUIState <- UISystem.togglePanel UISystem.AbilityList newUIState

      Debug.WriteLine(
        $"[UI] Ability list toggled: {newUIState.ActivePanels |> HashSet.contains UISystem.AbilityList}"
      )

    newInputState <- {
      newInputState with
          PrevKeyADown = keyA
    }

    struct (toggleGrid, newInputState, newUIState)

  let handleDebugKeys
    (state: GameState)
    (playerId: Guid<EntityId>)
    (scenario: ScenarioState)
    (keyboardState: KeyboardState)
    (inputState: InputManager.InputState)
    =
    let keyR = keyboardState.IsKeyDown(Keys.R)
    let mutable newInputState = inputState

    if keyR && not inputState.PrevKeyRDown then
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

    newInputState <- {
      newInputState with
          PrevKeyRDown = keyR
    }

    newInputState
