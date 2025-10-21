namespace Pomo.Core

open System
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
  let mutable zoom: single = 1.0f
  let mutable prevScroll: int = 0
  let mutable cameraPos: Vector2 = Vector2.Zero
  let mutable selected: Guid<EntityId> voption = ValueNone
  let mutable prevMouseDown: bool = false
  let mutable prevRightMouseDown: bool = false
  let mutable prevKey1Down: bool = false
  let mutable showPathfindingGrid: bool = false
  let mutable prevKey2Down: bool = false
  let mutable prevKey3Down: bool = false
  let mutable prevKey4Down: bool = false
  let mutable prevKey5Down: bool = false
  let mutable prevKeyVDown: bool = false
  let mutable prevKeyEDown: bool = false
  let mutable prevKeyADown: bool = false
  let mutable prevKeyRDown: bool = false
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

  let computeTerrainVersion(scenario: Scenario) =
    let objs = scenario.TerrainObjects |> IndexList.toArray

    let hashObjs =
      objs
      |> Array.fold
        (fun s o ->
          s + int64(HashCode.Combine(o.Id, o.Position.X, o.Position.Y)))
        0L

    let transHash =
      scenario.Transitions
      |> Array.fold
        (fun s t ->
          s
          + int64(
            HashCode.Combine(
              t.FromPosition.X,
              t.FromPosition.Y,
              t.ToScenarioId
            )
          ))
        0L

    hashObjs + transHash

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

    Console.WriteLine("Game initialized")


  override this.LoadContent() =
    base.LoadContent()
    spriteBatch <- new SpriteBatch(this.GraphicsDevice)
    pixel <- new Texture2D(this.GraphicsDevice, 1, 1)
    pixel.SetData<Color>([| Color.White |])
    hudFont <- this.Content.Load<SpriteFont>("Fonts/Hud")
    prevScroll <- Mouse.GetState().ScrollWheelValue
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
        let stateChange =
          gameTime.ElapsedGameTime |> GameState.tick state |> AVal.force

        let scenario = Scenario.ActiveScenario state |> AVal.force

        AudioSystem.processAudioChanges
          state.services.audioStore
          scenario
          stateChange.audioChanges

        GameState.apply state stateChange
        AudioSystem.update()

        let wheel = Mouse.GetState().ScrollWheelValue
        let delta = wheel - prevScroll

        if delta <> 0 then
          let dz = float32 delta * 0.001f
          let mutable z = zoom + dz

          if z < 0.5f then
            z <- 0.5f

          if z > 2.0f then
            z <- 2.0f

          zoom <- z
          prevScroll <- wheel

        let scenario = Scenario.ActiveScenario state |> AVal.force

        let newVersion = computeTerrainVersion scenario.scenario

        if newVersion <> terrainVersion then
          terrainVersion <- newVersion
          isGridDirty <- true

        if isGridDirty then
          navigationDebugGrid <-
            let g = Grid.createGrid scenario.scenario 32.0f 16.0f

            ValueSome g

          isGridDirty <- false

        // Check for scenario transitions
        let detectedTransitions =
          Pomo.Lib.ScenarioTransitions.TransitionDetection.detectTransitions
            scenario.entities
            scenario.scenario
          |> AMap.force

        // Process any detected transitions
        detectedTransitions
        |> HashMap.iter(fun entityId trigger ->
          Console.WriteLine(
            $"[Transition] Entity {entityId} triggered transition to scenario {trigger.ToScenarioId}"
          )
        // For now, just log the transition - full transition execution would require
        // multiple scenarios to be loaded
        )

        match scenario.entities |> AMap.force |> HashMap.tryFindV playerId with
        | ValueSome comp -> cameraPos <- Position.toVector2 comp.Position
        | ValueNone -> ()

        let vp = this.GraphicsDevice.Viewport
        let halfW = float32 vp.Width / 2.0f
        let halfH = float32 vp.Height / 2.0f

        let view =
          Matrix.CreateTranslation(-cameraPos.X, -cameraPos.Y, 0f)
          * Matrix.CreateScale(zoom)
          * Matrix.CreateTranslation(halfW, halfH, 0f)

        let mouseScreen = InputManager.getMousePosition()
        let world = InputManager.screenToWorld mouseScreen view
        mouseWorldPos <- world

        let rightMouseDown = InputManager.isRightClickPressed()

        InputManager.updateThrottle clickThrottle gameTime.ElapsedGameTime

        if rightMouseDown && not prevRightMouseDown then
          if inputMode <> InputManager.InputMode.Normal then
            inputMode <- InputManager.InputMode.Normal
            Console.WriteLine("[Input] Canceled ability targeting mode.")
          else
            match
              InputManager.tryThrottleClick clickThrottle (ValueSome world)
            with
            | ValueSome clickWorld ->
              let moveCmd =
                Rules.Navigate {
                  actor = playerId
                  destination = { X = clickWorld.X; Y = clickWorld.Y }
                }

              let stateChange =
                CommandHandler.evaluate state moveCmd |> AVal.force

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
              | [] ->
                currentPath <- Array.empty
                pathPreview <- Array.empty
              | waypoints ->
                let fullPath =
                  Array.concat [|
                    [| playerComp.Position |]
                    waypoints |> List.toArray
                  |]

                currentPath <- fullPath

                pathPreview <-
                  PathPreview.generatePreview
                    scenario.scenario
                    fullPath
                    entityRadius

                Console.WriteLine(
                  $"[Pathfinding] Preview generated for {fullPath.Length} points"
                )
            | ValueNone -> ()

        prevRightMouseDown <- rightMouseDown

        let mouseDown = InputManager.isLeftClickPressed()

        if (mouseDown && not prevMouseDown) then
          let entities = scenario.entities |> AMap.force |> HashMap.toArrayV

          let inline radiusOfStage s =
            match s with
            | Stage.First -> 12f
            | Stage.Second -> 16f
            | Stage.Third -> 20f

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
            selected <- found

            match selected with
            | ValueSome sid -> Console.WriteLine($"[Input] Selected {sid}")
            | ValueNone -> Console.WriteLine("[Input] Selection cleared")
          | InputManager.InputMode.AbilityTargeting(abilityId,
                                                    InputManager.TargetingMode.EntityTargeting) ->
            match found with
            | ValueSome targetId ->
              let stateChange =
                GameState.activateAbility
                  playerId
                  abilityId
                  [| targetId |]
                  state
                |> AVal.force

              AudioSystem.processAudioChanges
                state.services.audioStore
                scenario
                stateChange.audioChanges

              GameState.apply state stateChange

              Console.WriteLine(
                $"[Ability] Activated {abilityId} on {targetId}"
              )
            | ValueNone -> Console.WriteLine("[Ability] No target selected.")

            inputMode <- InputManager.InputMode.Normal
            Console.WriteLine("[Input] Reverted to normal input mode.")
          | InputManager.InputMode.AbilityTargeting(abilityId,
                                                    InputManager.TargetingMode.GroundTargeting _) ->
            let targetPos = { X = world.X; Y = world.Y }

            let stateChange =
              GameState.activateAbilityAtPosition
                playerId
                abilityId
                targetPos
                state
              |> AVal.force

            AudioSystem.processAudioChanges
              state.services.audioStore
              scenario
              stateChange.audioChanges

            GameState.apply state stateChange

            Console.WriteLine(
              $"[Ability] Activated {abilityId} at position ({targetPos.X}, {targetPos.Y})"
            )

            inputMode <- InputManager.InputMode.Normal
            Console.WriteLine("[Input] Reverted to normal input mode.")


        prevMouseDown <- mouseDown

        let key1 = Keyboard.GetState().IsKeyDown(Keys.D1)

        if key1 && not prevKey1Down then
          inputMode <-
            InputManager.InputMode.AbilityTargeting(
              2<AbilityId>,
              InputManager.TargetingMode.EntityTargeting
            )

          Console.WriteLine(
            "[Input] Entered ability targeting mode for Fireball (ability 2)."
          )

        prevKey1Down <- key1

        let key3 = Keyboard.GetState().IsKeyDown(Keys.D3)

        if key3 && not prevKey3Down then
          inputMode <-
            InputManager.InputMode.AbilityTargeting(
              102<AbilityId>,
              InputManager.TargetingMode.GroundTargeting 32.0f
            )

          Console.WriteLine(
            "[Input] Entered ground targeting mode for Arrow Shot (ability 102)."
          )

        prevKey3Down <- key3

        let key4 = Keyboard.GetState().IsKeyDown(Keys.D4)

        if key4 && not prevKey4Down then
          inputMode <-
            InputManager.InputMode.AbilityTargeting(
              103<AbilityId>,
              InputManager.TargetingMode.GroundTargeting 64.0f
            )

          Console.WriteLine(
            "[Input] Entered ground targeting mode for Meteor Shower (ability 103)."
          )

        prevKey4Down <- key4

        let key5 = Keyboard.GetState().IsKeyDown(Keys.D5)

        if key5 && not prevKey5Down then
          inputMode <-
            InputManager.InputMode.AbilityTargeting(
              104<AbilityId>,
              InputManager.TargetingMode.GroundTargeting 32.0f
            )

          Console.WriteLine(
            "[Input] Entered ground targeting mode for Magic Arrow (ability 104)."
          )

        prevKey5Down <- key5

        let keyF2 = Keyboard.GetState().IsKeyDown(Keys.F2)

        if keyF2 && not prevKey2Down then
          showPathfindingGrid <- not showPathfindingGrid

          Console.WriteLine(
            $"[Debug] showPathfindingGrid toggled: {showPathfindingGrid}"
          )

        prevKey2Down <- keyF2

        let keyV = Keyboard.GetState().IsKeyDown(Keys.V)

        if keyV && not prevKeyVDown then
          uiState <- UISystem.togglePanel UISystem.CharacterSheet uiState

          Console.WriteLine(
            $"[UI] Character sheet toggled: {uiState.ActivePanels |> HashSet.contains UISystem.CharacterSheet}"
          )

        prevKeyVDown <- keyV

        let keyE = Keyboard.GetState().IsKeyDown(Keys.E)

        if keyE && not prevKeyEDown then
          uiState <- UISystem.togglePanel UISystem.EquipmentView uiState

          Console.WriteLine(
            $"[UI] Equipment view toggled: {uiState.ActivePanels |> HashSet.contains UISystem.EquipmentView}"
          )

        prevKeyEDown <- keyE

        let keyA = Keyboard.GetState().IsKeyDown(Keys.A)

        if keyA && not prevKeyADown then
          uiState <- UISystem.togglePanel UISystem.AbilityList uiState

          Console.WriteLine(
            $"[UI] Ability list toggled: {uiState.ActivePanels |> HashSet.contains UISystem.AbilityList}"
          )

        prevKeyADown <- keyA

        let keyR = Keyboard.GetState().IsKeyDown(Keys.R)

        if keyR && not prevKeyRDown then

          let replenishCmd =
            Rules.ReplenishResources [|
              {
                Actor = playerId
                ResourceType = ResourceType.MP
                Amount = 1000
              }
            |]

          let stateChange =
            CommandHandler.evaluate state replenishCmd |> AVal.force

          AudioSystem.processAudioChanges
            state.services.audioStore
            scenario
            stateChange.audioChanges

          GameState.apply state stateChange

          Console.WriteLine("[Debug] Player MP replenished.")

        prevKeyRDown <- keyR

        let keyboardState = Keyboard.GetState()
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
      let vp = this.GraphicsDevice.Viewport
      let halfW = float32 vp.Width / 2.0f
      let halfH = float32 vp.Height / 2.0f

      let view =
        Matrix.CreateTranslation(-cameraPos.X, -cameraPos.Y, 0f)
        * Matrix.CreateScale(zoom)
        * Matrix.CreateTranslation(halfW, halfH, 0f)

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
