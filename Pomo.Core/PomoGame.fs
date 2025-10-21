namespace Pomo.Core

open System
open System.Diagnostics
open System.Collections.Generic
open System.Globalization

open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input

open FSharp.UMX
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
open FSharp.Data.Adaptive

type PomoGame() as this =
  inherit Game()

  let graphicsDeviceManager = new GraphicsDeviceManager(this)

  let mutable gameState: GameState voption = ValueNone
  let mutable playerId: Guid<EntityId> = Guid.Empty |> UMX.tag<EntityId>
  let mutable enemyId: Guid<EntityId> = Guid.Empty |> UMX.tag<EntityId>

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
    enemyId <- testData.EnemyId
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

        camera <- CameraSystem.updateZoom camera

        let scenario = Scenario.ActiveScenario state |> AVal.force

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

        let rightMouseDown = InputManager.isRightClickPressed()

        InputManager.updateThrottle clickThrottle gameTime.ElapsedGameTime

        if rightMouseDown && not inputState.PrevRightMouseDown then
          if inputMode <> InputManager.InputMode.Normal then
            inputMode <- InputManager.InputMode.Normal
          else
            match
              InputManager.tryThrottleClick clickThrottle (ValueSome world)
            with
            | ValueSome clickWorld ->
              let navResult =
                InputHandlerSystem.handleRightClick
                  state
                  playerId
                  clickWorld
                  scenario

              currentPath <- navResult.CurrentPath
              pathPreview <- navResult.PathPreview
            | ValueNone -> ()

        inputState <- {
          inputState with
              PrevRightMouseDown = rightMouseDown
        }

        let mouseDown = InputManager.isLeftClickPressed()

        if mouseDown && not inputState.PrevMouseDown then
          let clickResult =
            InputHandlerSystem.handleLeftClick
              state
              playerId
              world
              scenario
              inputMode

          match clickResult with
          | InputHandlerSystem.EntitySelected entityId ->
            selected <- ValueSome entityId
          | InputHandlerSystem.SelectionCleared -> selected <- ValueNone
          | InputHandlerSystem.AbilityActivatedOnEntity _
          | InputHandlerSystem.AbilityActivatedAtPosition _ ->
            inputMode <- InputManager.InputMode.Normal
            Debug.WriteLine("[Input] Reverted to normal input mode.")
          | InputHandlerSystem.AbilityTargetMissed ->
            inputMode <- InputManager.InputMode.Normal
            Debug.WriteLine("[Input] Reverted to normal input mode.")
          | InputHandlerSystem.NoAction -> ()


        inputState <- {
          inputState with
              PrevMouseDown = mouseDown
        }

        let keyboardState = Keyboard.GetState()

        let struct (newInputModeOpt, newInputState1) =
          InputHandlerSystem.handleAbilityKeys keyboardState inputState

        inputState <- newInputState1

        newInputModeOpt |> ValueOption.iter(fun mode -> inputMode <- mode)

        let struct (toggleGrid, newInputState2, newUIState) =
          InputHandlerSystem.handleUIKeys keyboardState inputState uiState

        inputState <- newInputState2
        uiState <- newUIState

        if toggleGrid then
          showPathfindingGrid <- not showPathfindingGrid

        inputState <-
          InputHandlerSystem.handleDebugKeys
            state
            playerId
            scenario
            keyboardState
            inputState

        let enemyEntity = scenario.entities |> AMap.find enemyId |> AVal.force
        let baseSpeed = enemyEntity.Movement.Speed

        playerInputState <-
          InputManager.updateMovement
            playerInputState
            keyboardState
            gameTime
            baseSpeed

        if playerInputState.Velocity.LengthSquared() > 0.0f then
          let moveCmd =
            Rules.AdvancePosition {
              actor = enemyId
              velocity = {
                X = playerInputState.Velocity.X
                Y = playerInputState.Velocity.Y
              }
              elapsed = gameTime.ElapsedGameTime.TotalSeconds |> float32
            }

          let stateChange = CommandHandler.evaluate state moveCmd |> AVal.force

          AudioSystem.processAudioChanges
            state.services.audioStore
            scenario
            stateChange.audioChanges

          GameState.apply state stateChange

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

      let hudOpt = if isNull hudFont then ValueNone else ValueSome hudFont

      let scenario = Scenario.ActiveScenario state |> AVal.force

      let entities = scenario.entities |> AMap.force |> HashMap.toArrayV

      let derived =
        adaptive {
          let! derived = DerivedStats.byGameState state
          return! derived |> AMap.toAVal
        }
        |> AVal.force

      let bounds = {
        Width = scenario.scenario.BoundsWidth
        Height = scenario.scenario.BoundsHeight
        CenterX = scenario.scenario.BoundsWidth * 0.5f
        CenterY = scenario.scenario.BoundsHeight * 0.5f
      }

      let floatingTexts =
        scenario.floatingTexts |> AMap.force |> HashMap.toValueArray

      let projectiles =
        scenario.projectiles |> AMap.force |> HashMap.toValueArray

      let aoes = scenario.aoes |> AMap.force |> HashMap.toValueArray
      let impacts = scenario.impacts |> AMap.force |> HashMap.toValueArray
      let gameTime = scenario.gameTime |> AVal.force

      spriteBatch.Begin(
        SpriteSortMode.Deferred,
        BlendState.AlphaBlend,
        SamplerState.PointClamp,
        null,
        null,
        null,
        view
      )

      let worldCtx = {
        Pomo.Core.RenderSystem.WorldContext.Bounds = bounds
        Pomo.Core.RenderSystem.WorldContext.TerrainScenario = scenario.scenario
      }

      let navCtx = {
        Pomo.Core.RenderSystem.NavigationContext.ShowGrid = showPathfindingGrid
        Pomo.Core.RenderSystem.NavigationContext.PathPreview = pathPreview
        Pomo.Core.RenderSystem.NavigationContext.CurrentPath = currentPath
        Pomo.Core.RenderSystem.NavigationContext.Grid = navigationDebugGrid
      }

      let entityCtx = {
        Pomo.Core.RenderSystem.EntityContext.Entities = entities
        Pomo.Core.RenderSystem.EntityContext.Derived = derived
        Pomo.Core.RenderSystem.EntityContext.Selected = selected
        Pomo.Core.RenderSystem.EntityContext.Hud = hudOpt
      }

      let effectsCtx = {
        Pomo.Core.RenderSystem.EffectsContext.FloatingTexts = floatingTexts
        Pomo.Core.RenderSystem.EffectsContext.Projectiles = projectiles
        Pomo.Core.RenderSystem.EffectsContext.Aoes = aoes
        Pomo.Core.RenderSystem.EffectsContext.Impacts = impacts
        Pomo.Core.RenderSystem.EffectsContext.GameTime = gameTime
        Pomo.Core.RenderSystem.EffectsContext.Services = state.services
        Pomo.Core.RenderSystem.EffectsContext.Hud = hudOpt
      }

      let inputCtx = {
        Pomo.Core.RenderSystem.InputContext.InputMode = inputMode
        Pomo.Core.RenderSystem.InputContext.MouseWorldPos = mouseWorldPos
      }

      RenderSystem.drawWorld spriteBatch pixel worldCtx
      RenderSystem.drawNavigation spriteBatch pixel navCtx
      RenderSystem.drawEntitiesPhase spriteBatch pixel entityCtx
      RenderSystem.drawEffectsPhase spriteBatch pixel effectsCtx
      RenderSystem.drawInputPhase spriteBatch pixel inputCtx
      spriteBatch.End()

      hudOpt
      |> ValueOption.iter(fun font ->
        UISystem.draw
          spriteBatch
          pixel
          font
          uiState
          state
          this.GraphicsDevice.Viewport)

    | _ -> ()

    base.Draw(gameTime)
