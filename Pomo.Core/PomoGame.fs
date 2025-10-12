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
open Pomo.Lib.Content
open Pomo.Lib.Operations.GameStateOperations
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

  do
    base.Services.AddService(
      typeof<GraphicsDeviceManager>,
      graphicsDeviceManager
    )

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

    let struct (playerIdLocal, playerChange) =
      createEntity playerProfession playerStats [ Faction.Player ]

    applyEntityChange state playerChange
    playerId <- playerIdLocal

    let enemyProfession = { Family = Magic; Stage = First }

    let enemyStats = {
      Power = 10
      Magic = 15
      Sense = 10
      Charm = 10
    }

    let struct (enemyIdLocal, enemyChange) =
      createEntity enemyProfession enemyStats [ Faction.Enemy ]

    applyEntityChange state enemyChange
    enemyId <- enemyIdLocal

    // Set initial positions for visibility (Phase 6.1)
    transact(fun _ ->
      let p = state.entities[playerId]

      state.entities[playerId] <-
        {
          p with
              Position = { X = 100f; Y = 140f }
        }

      let e = state.entities[enemyId]

      state.entities[enemyId] <-
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
        let tickChangeAVal = advanceTime deltaTicks state
        let tickChange = AVal.force tickChangeAVal
        applyWithTime state tickChange

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

        match state.entities |> AMap.force |> HashMap.tryFindV playerId with
        | ValueSome comp -> cameraPos <- Position.toVector2 comp.Position
        | ValueNone -> ()

        base.Update(gameTime)
      | ValueNone -> base.Update(gameTime)


  override this.Draw(gameTime) =

    base.GraphicsDevice.Clear(Color.CornflowerBlue)

    match gameState with
    | ValueSome state when not(isNull spriteBatch) && not(isNull pixel) ->
      let entities = state.entities |> AMap.force |> HashMap.toArrayV
      let derived = GameState.getDerivedStats state |> AMap.force

      let vp = this.GraphicsDevice.Viewport
      let halfW = float32 vp.Width / 2.0f
      let halfH = float32 vp.Height / 2.0f
      let view =
        Matrix.CreateTranslation(-cameraPos.X, -cameraPos.Y, 0f)
        * Matrix.CreateScale(zoom)
        * Matrix.CreateTranslation(halfW, halfH, 0f)
      let hudOpt = if isNull hudFont then ValueNone else ValueSome hudFont
      RenderSystem.init this.GraphicsDevice
      RenderSystem.draw spriteBatch pixel hudOpt state view
    | _ -> ()

    base.Draw(gameTime)
