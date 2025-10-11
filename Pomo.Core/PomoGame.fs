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
    let playerStats = { Power = 15; Magic = 10; Sense = 10; Charm = 10 }
    let struct (playerIdLocal, playerChange) = createEntity playerProfession playerStats
    applyEntityChange state playerChange
    playerId <- playerIdLocal

    let enemyProfession = { Family = Magic; Stage = First }
    let enemyStats = { Power = 10; Magic = 15; Sense = 10; Charm = 10 }
    let struct (enemyIdLocal, enemyChange) = createEntity enemyProfession enemyStats
    applyEntityChange state enemyChange
    enemyId <- enemyIdLocal

    gameState <- ValueSome state

    Console.WriteLine("[Phase 6] Game initialized with player and enemy")
    Console.WriteLine($"[Phase 6] Player ID: {playerId}")
    Console.WriteLine($"[Phase 6] Enemy ID: {enemyId}")


  override this.LoadContent() = base.LoadContent()


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
      | ValueNone ->
          base.Update(gameTime)


  override this.Draw(gameTime) =

    base.GraphicsDevice.Clear(Color.MonoGameOrange)

    base.Draw(gameTime)
