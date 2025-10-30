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
open Pomo.Core.GameInput
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

  let mutable keybindingConfig = KeybindingSystem.createDefault []

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
    keybindingConfig <- KeybindingSystem.createDefault testData.KeyBindings

    gameState <- ValueSome state

    uiState <- {
      uiState with
          SelectedEntity = ValueSome playerId
    }

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


  override _.Update gameTime =
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

    let view = CameraSystem.createViewMatrix camera this.GraphicsDevice.Viewport

    let mouseState = Mouse.GetState()
    let mouseScreen = Vector2(float32 mouseState.X, float32 mouseState.Y)
    mouseWorldPos <- CameraSystem.screenToWorld mouseScreen view

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

      // Virtual Input processing (generates commands)
      let derivedStats = DerivedStats.byGameState state |> AVal.force |> AMap.force
      let virtualInputCtx: VirtualInputSystem.VirtualInputContext = {
        VirtualInputState = virtualInputState
        PrevVirtualInputState = prevVirtualInputState
        TouchState = touchState
        Scenario = scenario
        PlayerId = playerId
        GameTime = gameTime
        KeybindingConfig = keybindingConfig
        State = state
        InputMode = inputMode
        DerivedStats = derivedStats
      }

      let virtualInputResult = VirtualInputSystem.processInput virtualInputCtx
      commandList.AddRange virtualInputResult.Commands
      inputMode <- virtualInputResult.NewInputMode
      prevVirtualInputState <- virtualInputState
      virtualInputState <- virtualInputResult.NewVirtualInputState

      // Keyboard/Mouse Input processing (generates commands or updates local UI state)
      let inputCtx = {
        ActionInputManager = actionInputManager
        KeybindingConfig = keybindingConfig
        GameState = state
        PlayerId = playerId
        Scenario = scenario
        MouseWorldPos = mouseWorldPos
        TouchState = touchState
        View = view
        InputMode = inputMode
        UiState = uiState
        ShowPathfindingGrid = showPathfindingGrid
      }

      let inputResult = GameInput.processInputs inputCtx
      commandList.AddRange inputResult.Commands
      uiState <- inputResult.NewUiState
      showPathfindingGrid <- inputResult.ShowPathfindingGrid
      keybindingConfig <- inputResult.NewKeybindingConfig
      inputMode <- inputResult.NewInputMode

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
        let struct (newPath, newPreview) =
          PathPreview.updatePlayerPathPreview finalScenario playerId

        currentPath <- newPath
        pathPreview <- newPreview

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
        UISystem.draw spriteBatch pixel font uiState drawCtx)

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
