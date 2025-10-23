namespace Pomo.Core

open System
open System.Diagnostics
open System.Collections.Generic

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.InputActionPatterns
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.State
open Pomo.Lib.Rules

module GameInput =

  type InputContext = {
    ActionInputManager: ActionInputManager.State
    KeybindingConfig: KeybindingSystem.KeybindingConfig
    GameState: GameState
    PlayerId: Guid<EntityId>
    Scenario: ScenarioState
    MouseWorldPos: Vector2
    TouchState: TouchCollection
    View: Matrix
    InputMode: InputMode
    UiState: UISystem.UIState
    ShowPathfindingGrid: bool
  }

  type InputResult = {
    Commands: List<Rules.Command>
    NewUiState: UISystem.UIState
    ShowPathfindingGrid: bool
    NewKeybindingConfig: KeybindingSystem.KeybindingConfig
    NewInputMode: InputMode
  }

  let processInputs(ctx: InputContext) : InputResult =
    let commandList = List<Rules.Command>()
    let mutable uiState = ctx.UiState
    let mutable showPathfindingGrid = ctx.ShowPathfindingGrid
    let mutable keybindingConfig = ctx.KeybindingConfig
    let mutable inputMode = ctx.InputMode

    match ctx.ActionInputManager with
    | PressedActions actions ->
      for action in actions do
        match action with
        | ToggleCharacterSheet ->
          uiState <- UISystem.togglePanel UISystem.CharacterSheet uiState
        | ToggleAbilities ->
          uiState <- UISystem.togglePanel UISystem.AbilityList uiState
        | ToggleInventory ->
          uiState <- UISystem.togglePanel UISystem.EquipmentView uiState
        | DebugAction4 -> showPathfindingGrid <- not showPathfindingGrid
        | DebugAction5 ->
          let replenishCmd =
            Rules.ReplenishResources [|
              {
                Actor = ctx.PlayerId
                ResourceType = ResourceType.MP
                Amount = 1000
              }
            |]

          commandList.Add replenishCmd
        | SwitchToActionSet1 ->
          keybindingConfig <-
            KeybindingSystem.setActiveSet KeybindingSystem.Set1 keybindingConfig
        | SwitchToActionSet2 ->
          keybindingConfig <-
            KeybindingSystem.setActiveSet KeybindingSystem.Set2 keybindingConfig
        | SwitchToActionSet3 ->
          keybindingConfig <-
            KeybindingSystem.setActiveSet KeybindingSystem.Set3 keybindingConfig
        | SwitchToActionSet4 ->
          keybindingConfig <-
            KeybindingSystem.setActiveSet KeybindingSystem.Set4 keybindingConfig
        | SwitchToActionSet5 ->
          keybindingConfig <-
            KeybindingSystem.setActiveSet KeybindingSystem.Set5 keybindingConfig
        | UseQuickSlot1
        | UseQuickSlot2
        | UseQuickSlot3
        | UseQuickSlot4
        | UseQuickSlot5
        | UseQuickSlot6
        | UseQuickSlot7
        | UseQuickSlot8 ->
          let keybindingResult =
            KeybindingSystem.processSlotAction
              action
              keybindingConfig
              ctx.GameState
              ctx.PlayerId

          match keybindingResult with
          | KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode) ->
            inputMode <- AbilityTargeting(abilityId, targetingMode)
          | _ -> ()
        | PrimaryAction ->
          let mutable pointerWorldPos = ctx.MouseWorldPos

          if Platform.IsMobile() && ctx.TouchState.Count > 0 then
            let touchPos = ctx.TouchState[0].Position
            pointerWorldPos <- CameraSystem.screenToWorld touchPos ctx.View

          let entities = ctx.Scenario.entities |> AMap.force |> HashMap.toArrayV

          let inline radiusOfStage s =
            match s with
            | First -> 12f
            | Second -> 16f
            | Third -> 20f

          let mutable found: Guid<EntityId> voption = ValueNone

          for struct (id, comp) in entities do
            let dx = pointerWorldPos.X - comp.Position.X
            let dy = pointerWorldPos.Y - comp.Position.Y
            let r = radiusOfStage comp.Identity.Stage
            let dist2 = dx * dx + dy * dy

            if dist2 <= r * r then
              found <- ValueSome id

          match inputMode with
          | Normal -> ()
          | AbilityTargeting(abilityId, EntityTargeting) ->
            match found with
            | ValueSome targetId ->
              let cmd =
                Rules.Command.UseAbility {
                  actor = ctx.PlayerId
                  abilityId = abilityId
                  target = Rules.AbilityTarget.EntityTargets [| targetId |]
                }

              commandList.Add cmd
              Debug.WriteLine $"[Ability] Queued {abilityId} on {targetId}"
            | _ -> Debug.WriteLine "[Ability] No target selected."

            inputMode <- Normal
          | AbilityTargeting(abilityId, GroundTargeting _) ->
            let targetPos = {
              X = pointerWorldPos.X
              Y = pointerWorldPos.Y
            }

            let cmd =
              Rules.Command.UseAbility {
                actor = ctx.PlayerId
                abilityId = abilityId
                target = Rules.AbilityTarget.PositionTarget targetPos
              }

            commandList.Add cmd

            Debug.WriteLine
              $"[Ability] Queued {abilityId} at position ({targetPos.X}, {targetPos.Y})"

            inputMode <- Normal
        | SecondaryAction ->
          if inputMode <> Normal then
            inputMode <- Normal
          else
            let mutable pointerWorldPos = ctx.MouseWorldPos

            if Platform.IsMobile() && ctx.TouchState.Count > 0 then
              let touchPos = ctx.TouchState[0].Position
              pointerWorldPos <- CameraSystem.screenToWorld touchPos ctx.View

            let moveCmd =
              Rules.Navigate {
                actor = ctx.PlayerId
                destination = {
                  X = pointerWorldPos.X
                  Y = pointerWorldPos.Y
                }
              }

            commandList.Add moveCmd
        | _ -> ()

    {
      Commands = commandList
      NewUiState = uiState
      ShowPathfindingGrid = showPathfindingGrid
      NewKeybindingConfig = keybindingConfig
      NewInputMode = inputMode
    }
