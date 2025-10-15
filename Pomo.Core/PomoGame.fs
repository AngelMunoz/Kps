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
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.Services
open Pomo.Lib.Rules
open Pomo.Lib.Content
open Pomo.Lib.Operations
open Pomo.Lib.Scenario
open Pomo.Lib.Pathfinding
open Pomo.Lib.ScenarioTransitions
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
  let mutable prevKeyVDown: bool = false
  let mutable prevKeyEDown: bool = false
  let mutable prevKeyADown: bool = false
  let mutable currentPath: Position[] = Array.empty
  let mutable pathPreview: PathPreview.PathSegment[] = Array.empty
  let mutable uiState: UISystem.UIState = UISystem.createUIState()

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
      rng = fun () -> Random.Shared.NextDouble()
    }

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

    let state =
      GameState.create'
        services
        (initialScenarioId, cmap [ initialScenarioId, initialScenarioState ])

    let playerProfession = { Family = Power; Stage = First }

    let playerStats = {
      Power = 15
      Magic = 10
      Sense = 10
      Charm = 10
    }

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

    let enemyProfession = { Family = Magic; Stage = First }

    let enemyStats = {
      Power = 10
      Magic = 15
      Sense = 10
      Charm = 10
    }

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
      let scenario = GameState.getActiveScenario state |> AVal.force

      let p = scenario.entities[playerId]
      let abilityId = 8<AbilityId>
      let abilities = HashSet.ofList [ abilityId ]

      let cooldowns: cmap<int<AbilityId>, int64<Tick>> =
        cmap [ (abilityId, 0L<Tick>) ]

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

      // Update the scenario in the game state
      let activeScenarioId = state.activeScenarioId |> AVal.force
      state.scenarios.[activeScenarioId] <- updatedScenarioState)

    gameState <- ValueSome state

    Console.WriteLine("[Phase 6] Game initialized with player and enemy")
    Console.WriteLine($"[Phase 6] Player ID: {playerId}")
    Console.WriteLine($"[Phase 6] Enemy ID: {enemyId}")
    Console.WriteLine("")
    Console.WriteLine("=== PHASE 6.8 VISUAL CONTROLS ===")
    Console.WriteLine("Right Click: Move with pathfinding (shows path preview)")
    Console.WriteLine("Key 2: Toggle pathfinding grid visualization")
    Console.WriteLine("Left Click: Select entity")
    Console.WriteLine("Key 1: Use ability on selected target")
    Console.WriteLine("Key V: Toggle Character Sheet")
    Console.WriteLine("Key E: Toggle Equipment View")
    Console.WriteLine("Key A: Toggle Ability List")
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
    Console.WriteLine("- UI Panels: Character sheet (V), Equipment (E), Abilities (A)")

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


  override this.Update(gameTime) =

    let exitRequested =
      GamePad.GetState(PlayerIndex.One).Buttons.Back = ButtonState.Pressed
      || Keyboard.GetState().IsKeyDown(Keys.Escape)

    if exitRequested then
      this.Exit()
    else
      match gameState with
      | ValueSome state ->
        let deltaTicks = int64 gameTime.ElapsedGameTime.Ticks * 1L<Tick>

        deltaTicks |> GameState.tick state |> GameState.forceAndApply state

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

        let scenario = GameState.getActiveScenario state |> AVal.force

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

        let rightMouseDown = InputManager.isRightClickPressed()

        if rightMouseDown && not prevRightMouseDown then
          let mouseScreen = InputManager.getMousePosition()
          let world = InputManager.screenToWorld mouseScreen view

          // Generate pathfinding preview
          match
            scenario.entities |> AMap.force |> HashMap.tryFindV playerId
          with
          | ValueSome playerComp ->
            let entityRadius =
              match playerComp.Identity.Stage with
              | Stage.First -> 12f
              | Stage.Second -> 16f
              | Stage.Third -> 20f

            let cellSize = max 20.0f (entityRadius * 2.0f)

            let allEntities =
              scenario.entities |> AMap.force |> HashMap.toArrayV

            let grid =
              Grid.createWithEntities
                scenario.scenario
                cellSize
                entityRadius
                allEntities
                playerId

            match
              AStar.findPath grid playerComp.Position {
                X = world.X
                Y = world.Y
              }
            with
            | ValueSome path ->
              currentPath <- path

              pathPreview <-
                PathPreview.generatePreview scenario.scenario path entityRadius

              Console.WriteLine(
                $"[Pathfinding] Generated path with {path.Length} waypoints"
              )
            | ValueNone ->
              currentPath <- Array.empty
              pathPreview <- Array.empty
              Console.WriteLine("[Pathfinding] No valid path found")
          | ValueNone -> ()

          let moveCmd =
            Rules.Move {
              actor = playerId
              destination = { X = world.X; Y = world.Y }
            }

          Resolution.evaluate state moveCmd |> GameState.forceAndApply state

        prevRightMouseDown <- rightMouseDown

        let mouseDown = InputManager.isLeftClickPressed()

        if (mouseDown && not prevMouseDown) then
          let mouseScreen = InputManager.getMousePosition()
          let world = InputManager.screenToWorld mouseScreen view

          Console.WriteLine(
            $"[Input] World {world.X},{world.Y} Zoom {zoom} Camera {cameraPos.X},{cameraPos.Y}"
          )

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

            Console.WriteLine(
              $"[Input] Check {id} Pos {comp.Position.X},{comp.Position.Y} Dist2 {dist2} R2 {r * r} Inside {inside}"
            )

            if inside then
              found <- ValueSome id

          selected <- found

          match selected with
          | ValueSome sid -> Console.WriteLine($"[Input] Selected {sid}")
          | ValueNone -> Console.WriteLine("[Input] Selection cleared")

        prevMouseDown <- mouseDown

        let key1 = Keyboard.GetState().IsKeyDown(Keys.D1)

        if key1 && not prevKey1Down then
          match selected with
          | ValueSome targetId ->
            GameState.activateAbility playerId 8<AbilityId> [| targetId |] state
            |> GameState.forceAndApply state

            Console.WriteLine($"[Ability] Activated 8 on {targetId}")
          | ValueNone ->
            Console.WriteLine("[Ability] No target selected for ability 8")

        prevKey1Down <- key1

        let key2 = Keyboard.GetState().IsKeyDown(Keys.D2)

        if key2 && not prevKey2Down then
          showPathfindingGrid <- not showPathfindingGrid

          Console.WriteLine(
            $"[Debug] Pathfinding grid visibility: {showPathfindingGrid}"
          )

        prevKey2Down <- key2

        let keyV = Keyboard.GetState().IsKeyDown(Keys.V)
        if keyV && not prevKeyVDown then
          uiState <- UISystem.togglePanel UISystem.CharacterSheet uiState
          Console.WriteLine($"[UI] Character sheet toggled: {uiState.ActivePanels |> HashSet.contains UISystem.CharacterSheet}")
        prevKeyVDown <- keyV

        let keyE = Keyboard.GetState().IsKeyDown(Keys.E)
        if keyE && not prevKeyEDown then
          uiState <- UISystem.togglePanel UISystem.EquipmentView uiState
          Console.WriteLine($"[UI] Equipment view toggled: {uiState.ActivePanels |> HashSet.contains UISystem.EquipmentView}")
        prevKeyEDown <- keyE

        let keyA = Keyboard.GetState().IsKeyDown(Keys.A)
        if keyA && not prevKeyADown then
          uiState <- UISystem.togglePanel UISystem.AbilityList uiState
          Console.WriteLine($"[UI] Ability list toggled: {uiState.ActivePanels |> HashSet.contains UISystem.AbilityList}")
        prevKeyADown <- keyA

        match selected with
        | ValueSome entityId -> uiState <- UISystem.setSelectedEntity entityId uiState
        | ValueNone -> uiState <- UISystem.clearSelectedEntity uiState

        base.Update(gameTime)
      | ValueNone -> base.Update(gameTime)


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

      let scenario = GameState.getActiveScenario state |> AVal.force

      let entities = scenario.entities |> AMap.force |> HashMap.toArrayV

      let derived = GameState.getDerivedStats state |> AVal.force |> AMap.force

      let bounds = {
        Width = scenario.scenario.BoundsWidth
        Height = scenario.scenario.BoundsHeight
        CenterX = 0f
        CenterY = 0f
      }

      RenderSystem.draw
        struct (entities, derived)
        spriteBatch
        pixel
        hudOpt
        view
        selected
        bounds
        scenario.scenario
        showPathfindingGrid
        pathPreview
        currentPath

      match hudOpt with
      | ValueSome font ->
        UISystem.draw spriteBatch pixel font uiState state this.GraphicsDevice.Viewport
      | ValueNone -> ()
    | _ -> ()

    base.Draw(gameTime)
