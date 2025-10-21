namespace Pomo.Core

open System
open System.Collections.Generic
open System.Globalization
open type System.Net.Mime.MediaTypeNames

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

  let _ = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()

  let _ =
    OperatingSystem.IsWindows()
    || OperatingSystem.IsLinux()
    || OperatingSystem.IsMacOS()

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

    let services = {
      effectStore =
        { new IEffectStore with
            member _.tryFind effectId =
              EffectStore.definitions
              |> Map.tryFind effectId
              |> ValueOption.ofOption

            member _.find effectId =
              EffectStore.definitions |> Map.find effectId
        }
      abilityStore =
        { new IAbilityStore with
            member _.tryFind abilityId =
              AbilityStore.definitions
              |> Map.tryFind abilityId
              |> ValueOption.ofOption

            member _.find abilityId =
              AbilityStore.definitions |> Map.find abilityId
        }
      formulaStore =
        { new IFormulaStore with
            member _.tryFind formulaId =
              FormulaStore.definitions
              |> Map.tryFind formulaId
              |> ValueOption.ofOption

            member _.find formulaId =
              FormulaStore.definitions |> Map.find formulaId
        }
      projectileStore =
        { new IProjectileStore with
            member _.tryFind projectileId =
              ProjectileStore.definitions
              |> Map.tryFind projectileId
              |> ValueOption.ofOption

            member _.find projectileId =
              ProjectileStore.definitions |> Map.find projectileId
        }
      aoeStore =
        { new IAoeStore with
            member _.tryFind aoeId =
              AoeStore.definitions |> Map.tryFind aoeId |> ValueOption.ofOption

            member _.find aoeId = AoeStore.definitions |> Map.find aoeId
        }
      impactStore =
        { new IImpactStore with
            member _.tryFind impactId =
              ImpactStore.definitions
              |> Map.tryFind impactId
              |> ValueOption.ofOption

            member _.find impactId =
              ImpactStore.definitions |> Map.find impactId
        }
      audioStore =
        { new IAudioStore with
            member _.tryFind clipId =
              AudioStore.definitions
              |> Map.tryFind clipId
              |> ValueOption.ofOption

            member _.find clipId =
              AudioStore.definitions |> Map.find clipId

            member _.findByTrigger trigger =
              AudioStore.triggerMap
              |> Map.tryFind trigger
              |> Option.defaultValue Array.empty

            member _.findMusicForScenario scenarioId =
              let scenario =
                scenarios.Value
                |> HashMap.tryFindV scenarioId
                |> ValueOption.map _.scenario

              scenario
              |> ValueOption.bind(fun s ->
                AudioStore.scenarioMusicMap
                |> Map.tryFind s.Name
                |> ValueOption.ofOption)
        }
      rng = fun () -> Random.Shared.NextDouble()
    }

    let state = GameState.create' services (initialScenarioId, scenarios)

    let playerProfession = { Family = Magic; Stage = Second }

    let starterKit =
      Pomo.Lib.Content.CharacterKitStore.definitions[playerProfession]

    let playerChange =
      starterKit
      |> GameState.createEntity(fun stats -> {
        stats with
            Factions = HashSet.ofList [ Player ]
      })

    let _playerId = playerChange.additions |> HashMap.toKeySeq |> Seq.head
    GameState.apply state playerChange
    playerId <- _playerId

    let enemyProfession = { Family = Charm; Stage = First }


    let enemyKit = CharacterKitStore.definitions[enemyProfession]

    let enemyChange =
      enemyKit
      |> GameState.createEntity(fun stats -> {
        stats with
            Factions = HashSet.ofList [ Enemy ]
      })

    let enemyIdLocal = enemyChange.additions |> HashMap.toKeySeq |> Seq.head

    GameState.apply state enemyChange
    enemyId <- enemyIdLocal

    // Set initial positions for visibility (Phase 6.1) and give player a basic ability (Phase 6.2)
    // Add terrain objects and transitions for Phase 6.5 & 6.6 visualization
    transact(fun _ ->
      let scenario = Scenario.ActiveScenario state |> AVal.force
      let p = scenario.entities[playerId]
      let fireballAbilityId = 2<AbilityId>
      let arrowShotAbilityId = 102<AbilityId>
      let meteorShowerAbilityId = 103<AbilityId>
      let magicArrowAbilityId = 104<AbilityId>
      let abilities = HashSet.ofList [ fireballAbilityId; arrowShotAbilityId; meteorShowerAbilityId; magicArrowAbilityId ]

      scenario.entities[playerId] <-
        {
          p with
              Position = { X = 100f; Y = 140f }
              Abilities = abilities
              AbilityCooldowns = HashMap.empty
        }

      let e = scenario.entities[enemyId]

      scenario.entities[enemyId] <-
        {
          e with
              Position = { X = 220f; Y = 140f }
        }

      // Add terrain objects for pathfinding visualization
      let terrainObjects = [
        // Blocked wall
        {
          Id = %Guid.NewGuid()
          Position = { X = 300f; Y = 200f }
          CollisionGeometry =
            Polygon(
              [|
                { X = 280f; Y = 180f }
                { X = 320f; Y = 180f }
                { X = 320f; Y = 220f }
                { X = 280f; Y = 220f }
              |]
            )
          TerrainType = TerrainType.Blocked
          DepthLayer = 0.6f
          SpriteId = ValueSome "wall"
        }
        // Water area
        {
          Id = %Guid.NewGuid()
          Position = { X = 500f; Y = 300f }
          CollisionGeometry = Circle({ X = 500f; Y = 300f }, 40f)
          TerrainType = TerrainType.Water
          DepthLayer = 0.4f
          SpriteId = ValueSome "water"
        }
        // Hazard area
        {
          Id = %Guid.NewGuid()
          Position = { X = 700f; Y = 150f }
          CollisionGeometry = Circle({ X = 700f; Y = 150f }, 30f)
          TerrainType = TerrainType.Hazard
          DepthLayer = 0.5f
          SpriteId = ValueSome "hazard"
        }
      ]

      // Create updated scenario with terrain objects and transitions
      let transitions = [|
        {
          FromPosition = { X = 50f; Y = 300f }
          ToScenarioId = %Guid.NewGuid()
          ToPosition = { X = 750f; Y = 300f }
        }
        {
          FromPosition = { X = 750f; Y = 100f }
          ToScenarioId = %Guid.NewGuid()
          ToPosition = { X = 100f; Y = 100f }
        }
      |]

      let updatedTerrainObjects =
        terrainObjects
        |> List.fold
          (fun acc obj -> IndexList.add obj acc)
          scenario.scenario.TerrainObjects

      let updatedScenario = {
        scenario.scenario with
            TerrainObjects = updatedTerrainObjects
            Transitions = transitions
      }

      // Update the scenario state
      let updatedScenarioState = {
        scenario with
            scenario = updatedScenario
      }

      // Precompute navigation debug grid sharing scenario origin
      let debugGrid = Grid.createGrid updatedScenario 32.0f 16.0f

      navigationDebugGrid <- ValueSome debugGrid

      // Update the scenario in the game state
      let activeScenarioId = state.activeScenarioId |> AVal.force
      state.scenarios[activeScenarioId] <- updatedScenarioState

      AudioSystem.updateScenarioMusic services.audioStore activeScenarioId)

    gameState <- ValueSome state

    Console.WriteLine("[Phase 6] Game initialized with player and enemy")
    Console.WriteLine($"[Phase 6] Player ID: {playerId}")
    Console.WriteLine($"[Phase 6] Enemy ID: {enemyId}")
    Console.WriteLine("")
    Console.WriteLine("=== PHASE 6.8 VISUAL CONTROLS ===")
    Console.WriteLine("Right Click: Move with pathfinding (shows path preview)")
    Console.WriteLine("Key 2: Toggle pathfinding grid visualization")
    Console.WriteLine("Left Click: Select entity")
    Console.WriteLine("Key 1: Use Fireball on selected target (entity-targeted)")
    Console.WriteLine("Key 3: Use Arrow Shot (ground-targeted, 32px radius, blocked by terrain)")
    Console.WriteLine("Key 4: Use Meteor Shower (ground-targeted, 64px radius, ignores terrain)")
    Console.WriteLine("Key 5: Use Magic Arrow (ground-targeted, 32px radius, magical damage)")
    Console.WriteLine("Key V: Toggle Character Sheet")
    Console.WriteLine("Key E: Toggle Equipment View")
    Console.WriteLine("Key A: Toggle Ability List")
    Console.WriteLine("Key R: Replenish MP")
    Console.WriteLine("")
    Console.WriteLine("Visual Elements:")
    Console.WriteLine("- Brown rectangles: Blocked terrain (walls)")
    Console.WriteLine("- Light blue circles: Water terrain (slower movement)")
    Console.WriteLine("- Red-orange circles: Hazard terrain (damage over time)")
    Console.WriteLine("- Purple squares: Transition points (portals)")
    Console.WriteLine("- Green lines: Valid path preview")
    Console.WriteLine("- Red lines: Invalid path preview")
    Console.WriteLine("- Yellow borders: Scenario bounds")

    Console.WriteLine(
      "- Grid overlay: Pathfinding navigation grid (toggle with Key 2)"
    )

    Console.WriteLine(
      "- UI Panels: Character sheet (V), Equipment (E), Abilities (A)"
    )

    Console.WriteLine("=======================================")
    Console.WriteLine("")


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
          | InputManager.InputMode.AbilityTargeting(abilityId, InputManager.TargetingMode.EntityTargeting) ->
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
          | InputManager.InputMode.AbilityTargeting(abilityId, InputManager.TargetingMode.GroundTargeting _) ->
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
          inputMode <- InputManager.InputMode.AbilityTargeting(2<AbilityId>, InputManager.TargetingMode.EntityTargeting)

          Console.WriteLine(
            "[Input] Entered ability targeting mode for Fireball (ability 2)."
          )

        prevKey1Down <- key1

        let key3 = Keyboard.GetState().IsKeyDown(Keys.D3)

        if key3 && not prevKey3Down then
          inputMode <- InputManager.InputMode.AbilityTargeting(102<AbilityId>, InputManager.TargetingMode.GroundTargeting 32.0f)

          Console.WriteLine(
            "[Input] Entered ground targeting mode for Arrow Shot (ability 102)."
          )

        prevKey3Down <- key3

        let key4 = Keyboard.GetState().IsKeyDown(Keys.D4)

        if key4 && not prevKey4Down then
          inputMode <- InputManager.InputMode.AbilityTargeting(103<AbilityId>, InputManager.TargetingMode.GroundTargeting 64.0f)

          Console.WriteLine(
            "[Input] Entered ground targeting mode for Meteor Shower (ability 103)."
          )

        prevKey4Down <- key4

        let key5 = Keyboard.GetState().IsKeyDown(Keys.D5)

        if key5 && not prevKey5Down then
          inputMode <- InputManager.InputMode.AbilityTargeting(104<AbilityId>, InputManager.TargetingMode.GroundTargeting 32.0f)

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
