namespace Pomo.Lib.Tests

open Xunit
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Gameplay
open System

module MovementPhase3Tests =

  let makeEntity stage x y speed dest = {
    AbilityCooldowns = cmap()
    Effects = clist []
    Identity = { Family = Family.Power; Stage = stage }
    BaseStats = {
      Power = 10
      Magic = 10
      Sense = 10
      Charm = 10
    }
    Resources = {
      HP = 100
      MP = 100
      Status = Status.Alive
    }
    Position = { X = x; Y = y }
    Movement = {
      Speed = speed
      Destination = dest
      Path = []
    }
    Factions = HashSet.ofList [ Player ]
    Abilities = HashSet.empty
    Equipment = HashMap.empty
  }

  [<Fact>]
  let ``Entity movement clamps to bounds``() =
    let state = GameState.create()

    let bounds = {
      Width = 100f
      Height = 100f
      CenterX = 0f
      CenterY = 0f
    }

    let state = { state with bounds = bounds }
    let id = Guid.NewGuid() |> UMX.tag<EntityId>

    let e = makeEntity First 0f 0f 500f (ValueSome { X = 1000f; Y = 1000f })

    transact(fun _ -> state.entities.Add(id, e) |> ignore)

    // Large delta time to attempt to move beyond bounds
    let changeAVal = GameState.tick state (5_000_000L<Tick>)
    let change = AVal.force changeAVal

    transact(fun _ ->
      change.updates |> HashMap.iter(fun i comp -> state.entities.[i] <- comp))

    let final = state.entities |> AMap.force |> (fun m -> m[id])
    let halfW = bounds.Width * 0.5f
    let halfH = bounds.Height * 0.5f

    Assert.InRange(
      final.Position.X,
      bounds.CenterX - halfW,
      bounds.CenterX + halfW
    )

    Assert.InRange(
      final.Position.Y,
      bounds.CenterY - halfH,
      bounds.CenterY + halfH
    )

  [<Fact>]
  let ``Entities do not overlap after movement``() =
    let state = GameState.create()

    let bounds = {
      Width = 500f
      Height = 500f
      CenterX = 0f
      CenterY = 0f
    }

    let state = { state with bounds = bounds }
    let idA = Guid.NewGuid() |> UMX.tag<EntityId>
    let idB = Guid.NewGuid() |> UMX.tag<EntityId>

    let eA = makeEntity First -50f 0f 100f (ValueSome { X = 0f; Y = 0f })

    let eB = makeEntity Second 0f 0f 0f ValueNone

    transact(fun _ ->
      state.entities.Add(idA, eA) |> ignore
      state.entities.Add(idB, eB) |> ignore)

    // Tick multiple times
    for _ in 1..10 do
      let changeAVal = GameState.tick state 500_000L<Tick>
      let change = AVal.force changeAVal

      transact(fun _ ->
        change.updates
        |> HashMap.iter(fun i comp -> state.entities.[i] <- comp))

    let finalA = state.entities |> AMap.force |> (fun m -> m[idA])
    let finalB = state.entities |> AMap.force |> (fun m -> m[idB])

    let rA =
      match finalA.Identity.Stage with
      | First -> 12f
      | Second -> 16f
      | Third -> 20f

    let rB =
      match finalB.Identity.Stage with
      | First -> 12f
      | Second -> 16f
      | Third -> 20f

    let dx = finalA.Position.X - finalB.Position.X
    let dy = finalA.Position.Y - finalB.Position.Y
    let dist2 = dx * dx + dy * dy
    let minDist = rA + rB
    Assert.True(dist2 >= minDist * minDist)
