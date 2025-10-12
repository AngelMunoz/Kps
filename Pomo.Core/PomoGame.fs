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

  do
    base.Services.AddService(
      typeof<GraphicsDeviceManager>,
      graphicsDeviceManager
    )

    this.IsMouseVisible <- true

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

    let state = GameState.create' services

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
              AbilityCooldowns = (cooldowns :> amap<_, _>)
        }

      let e = scenario.entities[enemyId]

      scenario.entities[enemyId] <-
        {
          e with
              Position = { X = 220f; Y = 140f }
        })

    gameState <- ValueSome state

    Console.WriteLine("[Phase 6] Game initialized with player and enemy")
    Console.WriteLine($"[Phase 6] Player ID: {playerId}")
    Console.WriteLine($"[Phase 6] Enemy ID: {enemyId}")


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
    | _ -> ()

    base.Draw(gameTime)
