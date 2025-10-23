namespace Pomo.Core

open System
open System.Diagnostics
open System.Collections.Generic
open System.Globalization

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input

open FSharp.UMX
open FSharp.Data.Adaptive

open Pomo.Core.Localization
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
  let mutable camera: CameraSystem.CameraState = CameraSystem.createCamera()
  let mutable selected: Guid<EntityId> voption = ValueNone

  let mutable inputState: InputManager.InputState =
    InputManager.createInputState()

  let mutable showPathfindingGrid: bool = false
  let mutable currentPath: Position[] = Array.empty
  let mutable pathPreview: PathPreview.PathSegment[] = Array.empty
  let mutable uiState: UISystem.UIState = UISystem.createUIState()
  let mutable inputMode: InputManager.InputMode = InputManager.InputMode.Normal
  let mutable mouseWorldPos: Vector2 = Vector2.Zero

  let mutable virtualInputState: VirtualInputSystem.VirtualInputState voption =
    ValueNone

  let mutable keybindingConfig: KeybindingSystem.KeybindingConfig =
    KeybindingSystem.createDefault()

  let mutable playerInputState: InputManager.PlayerInputState =
    InputManager.createInitialState()

  let clickThrottle = InputManager.createThrottleState()

  let mutable navigationDebugGrid: Pomo.Lib.Pathfinding.PathfindingGrid voption =
    ValueNone

  let mutable terrainVersion: int64 = 0L
  let mutable isGridDirty: bool = true

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


  override this.Initialize() =
    base.Initialize()

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
            scenario.EngagementMode = EngagementMode.AlwaysOn
      })

    let scenarios = cmap [ initialScenarioId, initialScenarioState ]
    let services = ServiceFactory.createServices scenarios
    let state = GameState.create' services (initialScenarioId, scenarios)

    let testData = TestScenarioBuilder.createDefaultScenario state
    playerId <- testData.PlayerId
    enemyIds <- testData.Enemies
    navigationDebugGrid <- ValueSome testData.NavigationGrid

    gameState <- ValueSome state

    Debug.WriteLine("Game initialized")


  override this.LoadContent() =
    base.LoadContent()
    spriteBatch <- new SpriteBatch(this.GraphicsDevice)
    pixel <- new Texture2D(this.GraphicsDevice, 1, 1)
    pixel.SetData<Color>([| Color.White |])
    hudFont <- this.Content.Load<SpriteFont>("Fonts/Hud")
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

    let exitRequested =
      GamePad.GetState(PlayerIndex.One).Buttons.Back = ButtonState.Pressed
      || Keyboard.GetState().IsKeyDown(Keys.Escape)

    if exitRequested then
      this.Exit()
    else
      match gameState with
      | ValueNone -> base.Update(gameTime)
      | ValueSome state ->
        GameUpdateSystem.updateGameTick state gameTime.ElapsedGameTime

        let scenario = Scenario.ActiveScenario state |> AVal.force

        let struct (updatedControllers, aiCommands) =
          AISystem.processAllControllersAndCommands
            scenario.entities
            state.services.aiArchetypeStore
            state.services.abilityStore
            scenario.gameTime
            scenario.aiControllers
          |> AVal.force

        let controllerChange = {
          StateChange.empty with
              aiControllers = updatedControllers
        }

        GameState.apply state controllerChange

        for cmd in aiCommands do
          match cmd with
          | Rules.Command.UseAbility ability ->
            Debug.WriteLine(
              $"AI {ability.actor} using ability {ability.abilityId} against {ability.target}"
            )
          | Rules.Command.Navigate nav ->
            Debug.WriteLine($"AI {nav.actor} navigating to %A{nav.destination}")
          | others -> Debug.WriteLine($"AI using command: %A{others}")



          let stateChange = CommandHandler.evaluate state cmd |> AVal.force

          AudioSystem.processAudioChanges
            state.services.audioStore
            scenario
            stateChange.audioChanges

          GameState.apply state stateChange

        camera <- CameraSystem.updateZoom camera

        let struct (newVer, newDirty, gridOpt) =
          GameUpdateSystem.updateNavigationGrid
            scenario.scenario
            terrainVersion
            isGridDirty

        terrainVersion <- newVer
        isGridDirty <- newDirty

        gridOpt |> ValueOption.iter(fun g -> navigationDebugGrid <- ValueSome g)

        GameUpdateSystem.checkScenarioTransitions scenario

        let playerComps = scenario.entities[playerId]

        camera <-
          CameraSystem.setPosition
            (Position.toVector2 playerComps.Position)
            camera

        let view =
          CameraSystem.createViewMatrix camera this.GraphicsDevice.Viewport

        let mouseScreen = InputManager.getMousePosition()
        let world = InputManager.screenToWorld mouseScreen view
        mouseWorldPos <- world

        virtualInputState <-
          virtualInputState
          |> ValueOption.map(fun vs ->
            let touchState =
              Microsoft.Xna.Framework.Input.Touch.TouchPanel.GetState()

            VirtualInputSystem.update vs touchState)

        let mutable tempInputState = inputState
        let mutable tempInputMode = inputMode
        let mutable virtualButtonPressed = false

        virtualInputState
        |> ValueOption.iter(fun vinput ->
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

            let stateChange =
              CommandHandler.evaluate state moveCmd |> AVal.force

            GameState.apply state stateChange
          | ValueNone -> ()

          let struct (newInputState, vbuttonResult) =
            InputHandlerSystem.handleVirtualButtonInput
              state
              playerId
              scenario
              vinput.Buttons
              tempInputState
              keybindingConfig

          tempInputState <- newInputState

          match vbuttonResult with
          | KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode) ->
            tempInputMode <-
              InputManager.InputMode.AbilityTargeting(abilityId, targetingMode)

            virtualButtonPressed <- true
          | _ -> ())

        inputState <- tempInputState
        inputMode <- tempInputMode

        let inputResult =
          InputHandlerSystem.handleAllInput
            state
            playerId
            scenario
            world
            gameTime
            inputState
            uiState
            keybindingConfig
            inputMode
            selected
            showPathfindingGrid
            currentPath
            pathPreview
            clickThrottle
            virtualInputState
            virtualButtonPressed

        inputState <- inputResult.InputState
        uiState <- inputResult.UIState
        keybindingConfig <- inputResult.KeybindingConfig
        selected <- inputResult.Selected
        showPathfindingGrid <- inputResult.ShowPathfindingGrid
        currentPath <- inputResult.CurrentPath
        pathPreview <- inputResult.PathPreview

        inputResult.InputMode |> ValueOption.iter(fun mode -> inputMode <- mode)

    match selected with
    | ValueSome entityId when not(uiState.SelectedEntity = ValueSome entityId) ->
      uiState <- UISystem.setSelectedEntity entityId uiState
    | ValueNone when not(uiState.SelectedEntity = ValueSome playerId) ->
      uiState <- UISystem.setSelectedEntity playerId uiState
    | _ -> ()

    base.Update(gameTime)


  override this.Draw(gameTime) =
    base.GraphicsDevice.Clear(Color.CornflowerBlue)

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
        RenderSystem.WorldContext.Bounds = {
          Width = drawCtx.Scenario.BoundsWidth
          Height = drawCtx.Scenario.BoundsHeight
          CenterX = drawCtx.Scenario.BoundsWidth * 0.5f
          CenterY = drawCtx.Scenario.BoundsHeight * 0.5f
        }
        RenderSystem.WorldContext.TerrainScenario = drawCtx.Scenario
      }

      RenderSystem.drawNavigation spriteBatch pixel {
        RenderSystem.NavigationContext.ShowGrid = showPathfindingGrid
        RenderSystem.NavigationContext.PathPreview = pathPreview
        RenderSystem.NavigationContext.CurrentPath = currentPath
        RenderSystem.NavigationContext.Grid = navigationDebugGrid
      }

      RenderSystem.drawEntitiesPhase spriteBatch pixel {
        RenderSystem.EntityContext.Entities =
          drawCtx.Entities |> HashMap.toArrayV
        RenderSystem.EntityContext.Derived = drawCtx.DerivedStats
        RenderSystem.EntityContext.Selected = selected
        RenderSystem.EntityContext.Hud = hudOpt
      }

      RenderSystem.drawEffectsPhase spriteBatch pixel {
        RenderSystem.EffectsContext.FloatingTexts = drawCtx.FloatingTexts
        RenderSystem.EffectsContext.Projectiles = drawCtx.Projectiles
        RenderSystem.EffectsContext.Aoes = drawCtx.Aoes
        RenderSystem.EffectsContext.Impacts = drawCtx.Impacts
        RenderSystem.EffectsContext.GameTime = drawCtx.GameTime
        RenderSystem.EffectsContext.Services = state.services
        RenderSystem.EffectsContext.Hud = hudOpt
      }

      RenderSystem.drawInputPhase spriteBatch pixel {
        RenderSystem.InputContext.InputMode = inputMode
        RenderSystem.InputContext.MouseWorldPos = mouseWorldPos
      }

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
          VirtualInputSystem.draw spriteBatch pixel font vinput)

        spriteBatch.End())
    | _ -> ()

    base.Draw(gameTime)
