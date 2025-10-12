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
    // Phase 6.1: Basic rendering setup
    spriteBatch <- new SpriteBatch(this.GraphicsDevice)
    pixel <- new Texture2D(this.GraphicsDevice, 1, 1)
    pixel.SetData<Color>([| Color.White |])


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

        base.Update(gameTime)
      | ValueNone -> base.Update(gameTime)


  override this.Draw(gameTime) =

    base.GraphicsDevice.Clear(Color.CornflowerBlue)

    match gameState with
    | ValueSome state when not(isNull spriteBatch) && not(isNull pixel) ->
      let entities = state.entities |> AMap.force |> HashMap.toArrayV
      let derived = GameState.getDerivedStats state |> AMap.force

      spriteBatch.Begin()

      for struct (id, comp) in entities do
        let pos = comp.Position
        let w, h = 24f, 24f
        let rect = Rectangle(int pos.X, int pos.Y, int w, int h)

        // Color by faction
        let color =
          if comp.Factions |> HashSet.contains Faction.Player then
            Color.Green
          elif comp.Factions |> HashSet.contains Faction.Enemy then
            Color.Red
          elif comp.Resources.Status = Attributes.Status.Dead then
            Color.Gray
          else
            Color.Blue

        // Body
        spriteBatch.Draw(pixel, rect, color)

        // Health bar (simple)
        let maxHp =
          match HashMap.tryFindV id derived with
          | ValueSome stats -> stats.HP
          | ValueNone -> comp.Resources.HP

        let currentHp = comp.Resources.HP

        let hpRatio =
          if maxHp > 0 then float32 currentHp / float32 maxHp else 0f

        let barW, barH = w, 4f
        let barX, barY = pos.X, pos.Y - (barH + 2f)
        let backRect = Rectangle(int barX, int barY, int barW, int barH)

        let fillRect =
          Rectangle(int barX, int barY, int(barW * hpRatio), int barH)

        spriteBatch.Draw(pixel, backRect, Color(60, 60, 60))
        spriteBatch.Draw(pixel, fillRect, Color.LimeGreen)

      spriteBatch.End()
    | _ -> ()

    base.Draw(gameTime)
