namespace Pomo.Core

open System
open System.Diagnostics
open System.Collections.Generic
open System.Globalization

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open Microsoft.Xna.Framework.Input.Touch

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Localization
open Pomo.Core.InputActionPatterns
open Pomo.Lib.Gameplay
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.Services
open Pomo.Lib.Domain.State
open Pomo.Lib.Rules
open Pomo.Lib.Content
open Pomo.Lib.Operations
open Pomo.Lib.Scenario
open Pomo.Lib.Pathfinding
open Pomo.Lib.EnemyAI

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let mutable gameState: GameState voption = ValueNone
  let mutable playerId: Guid<EntityId> = Guid.Empty |> UMX.tag<EntityId>
  let mutable enemyIds: Guid<EntityId>[] = Array.empty

  let mutable spriteBatch: SpriteBatch = null
  let mutable pixel: Texture2D = null
  let mutable hudFont: SpriteFont = null
  let mutable camera = CameraSystem.createCamera()

  let mutable actionInputManager = Unchecked.defaultof<ActionInputManager.State>

  let mutable actionInputMap: InputMap = HashMap.empty

  let mutable inputMode = Normal

  let mutable showPathfindingGrid = false
  let mutable currentPath: Position[] = Array.empty
  let mutable pathPreview: PathPreview.PathSegment[] = Array.empty
  let mutable uiState: UISystem.UIState = UISystem.createUIState()
  let mutable mouseWorldPos: Vector2 = Vector2.Zero

  let mutable virtualInputState: VirtualInputSystem.VirtualInputState voption =
    ValueNone

  let mutable prevVirtualInputState
    : VirtualInputSystem.VirtualInputState voption =
    ValueNone

  let mutable keybindingConfig = KeybindingSystem.createDefault()

  let mutable navigationDebugGrid: Pomo.Lib.Pathfinding.PathfindingGrid voption =
    ValueNone

  let mutable terrainVersion = 0L
  let mutable isGridDirty = true

  do
    base.Services.AddService(
      typeof<GraphicsDeviceManager>,
      graphicsDeviceManager
    )

    this.IsMouseVisible <- true
    this.Window.AllowUserResizing <- true

    base.Content.RootDirectory <- "Content"

    graphicsDeviceManager.SupportedOrientations <-
      DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight


  override _.Initialize() =
    base.Initialize()

    actionInputManager <-
      ActionInputManager.create(
        Keyboard.GetState(),
        Mouse.GetState(),
        GamePad.GetState PlayerIndex.One,
        TouchPanel.GetState()
      )

    actionInputMap <- ActionInputManager.createDefaultMap()

    LocalizationManager.DefaultCultureCode |> LocalizationManager.SetCulture

    let initialScenarioId = %Guid.NewGuid()

    let initialScenarioState =
      {
        Id = initialScenarioId
        Name = "Test Scenario"
        BoundsWidth = 2000f
        BoundsHeight = 2000f
      }
      |> ScenarioState.create(fun sc -> {
        sc with
            scenario.EngagementMode = AlwaysOn
      })

    let scenarios = cmap [ initialScenarioId, initialScenarioState ]
    let services = ServiceFactory.createServices scenarios
    let state = GameState.create' services (initialScenarioId, scenarios)

    let testData = TestScenarioBuilder.createDefaultScenario state
    playerId <- testData.PlayerId
    enemyIds <- testData.Enemies
    navigationDebugGrid <- ValueSome testData.NavigationGrid

    gameState <- ValueSome state

    Debug.WriteLine "Game initialized"


  override this.LoadContent() =
    base.LoadContent()
    spriteBatch <- new SpriteBatch(this.GraphicsDevice)
    pixel <- new Texture2D(this.GraphicsDevice, 1, 1)
    pixel.SetData<Color> [| Color.White |]
    hudFont <- this.Content.Load<SpriteFont> "Fonts/Hud"
    camera <- CameraSystem.createCamera()
    RenderSystem.init this.GraphicsDevice
    AudioSystem.load this.Content

    if Platform.IsMobile() then
      camera <- CameraSystem.setZoom 2.5f camera
      let uiScale = 2.0f

      virtualInputState <-
        ValueSome(
          VirtualInputSystem.create this.GraphicsDevice.Viewport uiScale
        )


  override this.Update gameTime =
    let touchState = TouchPanel.GetState()

    actionInputManager <-
      ActionInputManager.update
        actionInputMap
        actionInputManager
        gameTime
        (Keyboard.GetState(),
         Mouse.GetState(),
         GamePad.GetState PlayerIndex.One,
         touchState)

    match gameState with
    | ValueNone -> base.Update gameTime
    | ValueSome state ->

      // PHASE 1: AUTOMATED & PRE-UPDATE SYSTEMS
      GameUpdateSystem.updateGameTick state gameTime.ElapsedGameTime

      let mutable scenario = Scenario.ActiveScenario state |> AVal.force

      // Update navigation grid based on state at start of frame
      let struct (newVer, newDirty, gridOpt) =
        GameUpdateSystem.updateNavigationGrid
          scenario.scenario
          terrainVersion
          isGridDirty

      terrainVersion <- newVer
      isGridDirty <- newDirty
      gridOpt |> ValueOption.iter(fun g -> navigationDebugGrid <- ValueSome g)

      // Update camera zoom
      camera <- CameraSystem.updateZoom camera

      // PHASE 2: COMMAND GENERATION
      let commandList = ResizeArray<Rules.Command>()

      // --- AI Commands ---
      // AI runs first, based on the state after the game tick.
      let struct (updatedControllers, aiCommands) =
        AISystem.processAllControllersAndCommands
          scenario.entities
          state.services.aiArchetypeStore
          state.services.abilityStore
          scenario.gameTime
          scenario.aiControllers
        |> AVal.force

      // Apply AI controller changes immediately, as they are not command-based.
      let controllerChange = {
        StateChange.empty with
            aiControllers = updatedControllers
      }

      GameState.apply state controllerChange
      // Refresh scenario after this change so user input processing has latest state
      scenario <- Scenario.ActiveScenario state |> AVal.force

      commandList.AddRange aiCommands

      // --- User Commands ---
      // User input is processed based on the state after AI controller updates.
      let view =
        CameraSystem.createViewMatrix camera this.GraphicsDevice.Viewport

      let mouseState = Mouse.GetState()
      let mouseScreen = Vector2(float32 mouseState.X, float32 mouseState.Y)
      mouseWorldPos <- CameraSystem.screenToWorld mouseScreen view

      // Virtual Input processing (generates commands)
      virtualInputState <-
        virtualInputState
        |> ValueOption.map(fun vs -> VirtualInputSystem.update vs touchState)

      virtualInputState
      |> ValueOption.iter(fun vinput ->
        // Joystick movement
        match VirtualInputSystem.getJoystickDirection vinput with
        | ValueSome direction ->
          let playerComp = scenario.entities[playerId]
          let velocity = direction * playerComp.Movement.Speed
          let elapsed = float32 gameTime.ElapsedGameTime.TotalSeconds

          let moveCmd =
            Rules.AdvancePosition {
              actor = playerId
              velocity = { X = velocity.X; Y = velocity.Y }
              elapsed = elapsed
            }

          commandList.Add moveCmd
        | ValueNone -> ()

        // Virtual buttons
        let prevButtons =
          prevVirtualInputState
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
                keybindingConfig
                state
                playerId

            match kbResult with
            | KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode) ->
              inputMode <- AbilityTargeting(abilityId, targetingMode)
            // For now, assuming quick slots generate commands via the main input handler below
            | _ -> ())

      prevVirtualInputState <- virtualInputState

      // Keyboard/Mouse Input processing (generates commands or updates local UI state)
      match actionInputManager with
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
                  Actor = playerId
                  ResourceType = ResourceType.MP
                  Amount = 1000
                }
              |]

            commandList.Add replenishCmd
          | SwitchToActionSet1 ->
            keybindingConfig <-
              KeybindingSystem.setActiveSet
                KeybindingSystem.Set1
                keybindingConfig
          | SwitchToActionSet2 ->
            keybindingConfig <-
              KeybindingSystem.setActiveSet
                KeybindingSystem.Set2
                keybindingConfig
          | SwitchToActionSet3 ->
            keybindingConfig <-
              KeybindingSystem.setActiveSet
                KeybindingSystem.Set3
                keybindingConfig
          | SwitchToActionSet4 ->
            keybindingConfig <-
              KeybindingSystem.setActiveSet
                KeybindingSystem.Set4
                keybindingConfig
          | SwitchToActionSet5 ->
            keybindingConfig <-
              KeybindingSystem.setActiveSet
                KeybindingSystem.Set5
                keybindingConfig
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
                state
                playerId

            match keybindingResult with
            | KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode) ->
              inputMode <- AbilityTargeting(abilityId, targetingMode)
            | _ -> ()
          | PrimaryAction ->
            let mutable pointerWorldPos = mouseWorldPos

            if Platform.IsMobile() && touchState.Count > 0 then
              let touchPos = touchState[0].Position
              pointerWorldPos <- CameraSystem.screenToWorld touchPos view

            let entities = scenario.entities |> AMap.force |> HashMap.toArrayV

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
                    actor = playerId
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
                  actor = playerId
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
              let mutable pointerWorldPos = mouseWorldPos

              if Platform.IsMobile() && touchState.Count > 0 then
                let touchPos = touchState[0].Position
                pointerWorldPos <- CameraSystem.screenToWorld touchPos view

              let moveCmd =
                Rules.Navigate {
                  actor = playerId
                  destination = {
                    X = pointerWorldPos.X
                    Y = pointerWorldPos.Y
                  }
                }

              commandList.Add moveCmd
          | _ -> ()

      // PHASE 3: COMMAND EXECUTION
      // All commands from AI and Player are executed here sequentially.
      let mutable playerNavigated = false

      for cmd in commandList do
        // Track if player navigated to update path preview later
        match cmd with
        | Rules.Command.Navigate nav when nav.actor = playerId ->
          playerNavigated <- true
        | _ -> ()

        let stateChange = CommandHandler.evaluate state cmd |> AVal.force

        let currentScenario = Scenario.ActiveScenario state |> AVal.force

        AudioSystem.processAudioChanges
          state.services.audioStore
          currentScenario
          stateChange.audioChanges

        GameState.apply state stateChange

      // PHASE 4: POST-UPDATE & FINALIZATION
      // All state changes are done. Get the final state for this frame.
      let finalScenario = Scenario.ActiveScenario state |> AVal.force

      // Update path preview if the player navigated
      if playerNavigated then
        let playerComp = finalScenario.entities[playerId]

        let entityRadius =
          match playerComp.Identity.Stage with
          | First -> 12f
          | Second -> 16f
          | Third -> 20f

        match playerComp.Movement.Path with
        | [] ->
          currentPath <- Array.empty
          pathPreview <- Array.empty
        | waypoints ->
          let fullPath =
            Array.concat [|
              [| playerComp.Position |]
              waypoints |> List.toArray
            |]

          let preview =
            PathPreview.generatePreview
              finalScenario.scenario
              fullPath
              entityRadius

          Debug.WriteLine
            $"[Pathfinding] Preview generated for {fullPath.Length} points"

          currentPath <- fullPath
          pathPreview <- preview

      // Update scenario transitions
      GameUpdateSystem.checkScenarioTransitions finalScenario

      // Update camera position to follow player
      let playerComps = finalScenario.entities[playerId]

      camera <-
        CameraSystem.setPosition
          (Position.toVector2 playerComps.Position)
          camera

      base.Update gameTime


  override this.Draw gameTime =
    base.GraphicsDevice.Clear Color.CornflowerBlue

    match gameState with
    | ValueSome state when not(isNull spriteBatch) && not(isNull pixel) ->
      let view =
        CameraSystem.createViewMatrix camera this.GraphicsDevice.Viewport

      let drawCtx =
        state
        |> Scenario.ActiveScenario
        |> GameState.GetDrawingContext state.services
        |> AVal.force

      let hudOpt = if isNull hudFont then ValueNone else ValueSome hudFont

      spriteBatch.Begin(
        SpriteSortMode.Deferred,
        BlendState.AlphaBlend,
        SamplerState.PointClamp,
        null,
        null,
        null,
        view
      )

      RenderSystem.drawWorld spriteBatch pixel {
        Bounds = {
          Width = drawCtx.Scenario.BoundsWidth
          Height = drawCtx.Scenario.BoundsHeight
          CenterX = drawCtx.Scenario.BoundsWidth * 0.5f
          CenterY = drawCtx.Scenario.BoundsHeight * 0.5f
        }
        TerrainScenario = drawCtx.Scenario
      }

      RenderSystem.drawNavigation spriteBatch pixel {
        ShowGrid = showPathfindingGrid
        PathPreview = pathPreview
        CurrentPath = currentPath
        Grid = navigationDebugGrid
      }

      let entityCtx: RenderSystem.EntityContext = {
        Entities = drawCtx.Entities |> HashMap.toArrayV
        Derived = drawCtx.DerivedStats
        Hud = hudOpt
      }

      RenderSystem.drawEntitiesPhase spriteBatch pixel entityCtx

      RenderSystem.drawEffectsPhase spriteBatch pixel {
        FloatingTexts = drawCtx.FloatingTexts
        Projectiles = drawCtx.Projectiles
        Aoes = drawCtx.Aoes
        Impacts = drawCtx.Impacts
        GameTime = drawCtx.GameTime
        Services = state.services
        Hud = hudOpt
      }

      RenderSystem.drawInputPhase
        spriteBatch
        pixel
        {
          InputMode = inputMode
          MouseWorldPos = mouseWorldPos
        }
        entityCtx

      spriteBatch.End()

      hudOpt
      |> ValueOption.iter(fun font ->
        UISystem.draw
          spriteBatch
          pixel
          font
          uiState
          drawCtx
          this.GraphicsDevice.Viewport)

      virtualInputState
      |> ValueOption.iter(fun vinput ->
        spriteBatch.Begin()

        hudOpt
        |> ValueOption.iter(fun font ->
          let uiScale = if Platform.IsMobile() then 2.0f else 1.0f

          VirtualInputSystem.draw
            spriteBatch
            pixel
            font
            vinput
            uiScale
            inputMode
            keybindingConfig)

        spriteBatch.End())
    | _ -> ()

    base.Draw gameTime
