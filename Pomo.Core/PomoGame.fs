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
  let mutable camera: CameraSystem.CameraState = CameraSystem.createCamera()
  let mutable selected: Guid<EntityId> voption = ValueNone

  let mutable actionInputManager: ActionInputManager.State =
    Unchecked.defaultof<_>

  let mutable actionInputMap: InputMap = HashMap.empty

  let mutable inputMode: InputMode = InputMode.Normal

  let mutable showPathfindingGrid: bool = false
  let mutable currentPath: Position[] = Array.empty
  let mutable pathPreview: PathPreview.PathSegment[] = Array.empty
  let mutable uiState: UISystem.UIState = UISystem.createUIState()
  let mutable mouseWorldPos: Vector2 = Vector2.Zero

  let mutable virtualInputState: VirtualInputSystem.VirtualInputState voption =
    ValueNone

  let mutable prevVirtualInputState: VirtualInputSystem.VirtualInputState voption = 
    ValueNone

  let mutable keybindingConfig: KeybindingSystem.KeybindingConfig =
    KeybindingSystem.createDefault()

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

    actionInputManager <-
      ActionInputManager.create(
        Keyboard.GetState(),
        Mouse.GetState(),
        GamePad.GetState(PlayerIndex.One),
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
    let touchState = TouchPanel.GetState()
    actionInputManager <-
      ActionInputManager.update
        actionInputMap
        actionInputManager
        gameTime
        (Keyboard.GetState(),
         Mouse.GetState(),
         GamePad.GetState(PlayerIndex.One),
         touchState)

    match gameState with
    | ValueNone -> base.Update(gameTime)
    | ValueSome state ->

      GameUpdateSystem.updateGameTick state gameTime.ElapsedGameTime

      let scenario = Scenario.ActiveScenario state |> AVal.force
      let view = CameraSystem.createViewMatrix camera this.GraphicsDevice.Viewport
      let mouseState = Mouse.GetState()
      let mouseScreen = Vector2(float32 mouseState.X, float32 mouseState.Y)
      mouseWorldPos <- CameraSystem.screenToWorld mouseScreen view

      match actionInputManager with
      | PressedActions actions ->
        for action in actions do
          match action with
          | GameAction.ToggleCharacterSheet -> uiState <- UISystem.togglePanel UISystem.CharacterSheet uiState
          | GameAction.ToggleAbilities -> uiState <- UISystem.togglePanel UISystem.AbilityList uiState
          | GameAction.ToggleInventory -> uiState <- UISystem.togglePanel UISystem.EquipmentView uiState
          | GameAction.DebugAction4 -> showPathfindingGrid <- not showPathfindingGrid
          | GameAction.DebugAction5 -> 
              let replenishCmd = Rules.ReplenishResources [| { Actor = playerId; ResourceType = ResourceType.MP; Amount = 1000 } |]
              let stateChange = CommandHandler.evaluate state replenishCmd |> AVal.force
              AudioSystem.processAudioChanges state.services.audioStore scenario stateChange.audioChanges
              GameState.apply state stateChange
              Debug.WriteLine("[Debug] MP replenished via new input system")
          | GameAction.SwitchToActionSet1 -> keybindingConfig <- KeybindingSystem.setActiveSet KeybindingSystem.Set1 keybindingConfig
          | GameAction.SwitchToActionSet2 -> keybindingConfig <- KeybindingSystem.setActiveSet KeybindingSystem.Set2 keybindingConfig
          | GameAction.SwitchToActionSet3 -> keybindingConfig <- KeybindingSystem.setActiveSet KeybindingSystem.Set3 keybindingConfig
          | GameAction.SwitchToActionSet4 -> keybindingConfig <- KeybindingSystem.setActiveSet KeybindingSystem.Set4 keybindingConfig
          | GameAction.SwitchToActionSet5 -> keybindingConfig <- KeybindingSystem.setActiveSet KeybindingSystem.Set5 keybindingConfig
          | GameAction.UseQuickSlot1 | GameAction.UseQuickSlot2 | GameAction.UseQuickSlot3 | GameAction.UseQuickSlot4 |
            GameAction.UseQuickSlot5 | GameAction.UseQuickSlot6 | GameAction.UseQuickSlot7 | GameAction.UseQuickSlot8 ->
              let keybindingResult = KeybindingSystem.processSlotAction action keybindingConfig state playerId
              match keybindingResult with
              | KeybindingSystem.EnterAbilityTargeting(abilityId, targetingMode) ->
                inputMode <- InputMode.AbilityTargeting(abilityId, targetingMode)
              | _ -> ()
          | GameAction.PrimaryAction ->
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
                let inside = dist2 <= r * r

                if inside then
                  found <- ValueSome id

              match inputMode with
              | InputMode.Normal ->
                match found with
                | ValueSome sid ->
                  Debug.WriteLine($"[Input] Selected {sid}")
                  selected <- ValueSome sid
                | ValueNone ->
                  Debug.WriteLine("[Input] Selection cleared")
                  selected <- ValueNone
              | InputMode.AbilityTargeting(abilityId, TargetingMode.EntityTargeting) ->
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
                  inputMode <- InputMode.Normal
                | ValueNone ->
                  Debug.WriteLine("[Ability] No target selected.")
                  inputMode <- InputMode.Normal
              | InputMode.AbilityTargeting(abilityId, TargetingMode.GroundTargeting _) ->
                let targetPos = {
                  X = pointerWorldPos.X
                  Y = pointerWorldPos.Y
                }

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

                inputMode <- InputMode.Normal
          | GameAction.SecondaryAction ->
              let mutable pointerWorldPos = mouseWorldPos
              if Platform.IsMobile() && touchState.Count > 0 then
                  let touchPos = touchState[0].Position
                  pointerWorldPos <- CameraSystem.screenToWorld touchPos view

              if inputMode <> InputMode.Normal then
                inputMode <- InputMode.Normal
              else
                let moveCmd =
                  Rules.Navigate {
                    actor = playerId
                    destination = {
                      X = pointerWorldPos.X
                      Y = pointerWorldPos.Y
                    }
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
                    PathPreview.generatePreview scenario.scenario fullPath entityRadius

                  Debug.WriteLine(
                    $"[Pathfinding] Preview generated for {fullPath.Length} points"
                  )

                  currentPath <- fullPath
                  pathPreview <- preview
          | _ -> ()
      | _ -> ()

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
        CameraSystem.setPosition (Position.toVector2 playerComps.Position) camera

      virtualInputState <-
        virtualInputState
        |> ValueOption.map(fun vs -> VirtualInputSystem.update vs touchState)

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

          let stateChange = CommandHandler.evaluate state moveCmd |> AVal.force

          GameState.apply state stateChange
        | ValueNone -> ()

        let prevButtons = 
          prevVirtualInputState
          |> ValueOption.map(fun pvs -> pvs.Buttons)
          |> ValueOption.defaultValue Array.empty

        for i in 0 .. vinput.Buttons.Length - 1 do
          let button = vinput.Buttons[i]
          let prevButton = prevButtons |> Array.tryFind(fun pb -> pb.Action = button.Action)

          let wasPressed =
              prevButton
              |> Option.map (fun pb -> pb.IsPressed)
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
                  inputMode <- InputMode.AbilityTargeting(abilityId, targetingMode)
              | _ -> ()
        )

      prevVirtualInputState <- virtualInputState

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

      let entityCtx = {
        RenderSystem.EntityContext.Entities =
          drawCtx.Entities |> HashMap.toArrayV
        RenderSystem.EntityContext.Derived = drawCtx.DerivedStats
        RenderSystem.EntityContext.Selected = selected
        RenderSystem.EntityContext.Hud = hudOpt
      }

      RenderSystem.drawEntitiesPhase spriteBatch pixel entityCtx

      RenderSystem.drawEffectsPhase spriteBatch pixel {
        RenderSystem.EffectsContext.FloatingTexts = drawCtx.FloatingTexts
        RenderSystem.EffectsContext.Projectiles = drawCtx.Projectiles
        RenderSystem.EffectsContext.Aoes = drawCtx.Aoes
        RenderSystem.EffectsContext.Impacts = drawCtx.Impacts
        RenderSystem.EffectsContext.GameTime = drawCtx.GameTime
        RenderSystem.EffectsContext.Services = state.services
        RenderSystem.EffectsContext.Hud = hudOpt
      }

      RenderSystem.drawInputPhase
        spriteBatch
        pixel
        {
          RenderSystem.InputContext.InputMode = inputMode
          RenderSystem.InputContext.MouseWorldPos = mouseWorldPos
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

    base.Draw(gameTime)