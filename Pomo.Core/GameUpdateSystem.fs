namespace Pomo.Core

open System
open System.Diagnostics
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Gameplay
open Pomo.Lib.Operations
open Pomo.Lib.Rules
open Pomo.Lib.Scenario
open Pomo.Lib.Pathfinding
open FSharp.Data.Adaptive

module GameUpdateSystem =

  let updateGameTick (state: GameState) (elapsed: TimeSpan) =
    let stateChange = elapsed |> GameState.tick state |> AVal.force
    let scenario = Scenario.ActiveScenario state |> AVal.force

    AudioSystem.processAudioChanges
      state.services.audioStore
      scenario
      stateChange.audioChanges

    GameState.apply state stateChange
    AudioSystem.update()

  let updateNavigationGrid
    (scenario: Scenario)
    (terrainVersion: int64)
    (isGridDirty: bool)
    =
    let computeVersion(s: Scenario) =
      let objs = s.TerrainObjects |> IndexList.toArray

      let hashObjs =
        objs
        |> Array.fold
          (fun acc o ->
            acc + int64(HashCode.Combine(o.Id, o.Position.X, o.Position.Y)))
          0L

      let transHash =
        s.Transitions
        |> Array.fold
          (fun acc t ->
            acc
            + int64(
              HashCode.Combine(
                t.FromPosition.X,
                t.FromPosition.Y,
                t.ToScenarioId
              )
            ))
          0L

      hashObjs + transHash

    let newVersion = computeVersion scenario

    if newVersion <> terrainVersion then
      let grid = Grid.createGrid scenario 32.0f 16.0f
      struct (newVersion, true, ValueSome grid)
    else if isGridDirty then
      let grid = Grid.createGrid scenario 32.0f 16.0f
      struct (terrainVersion, false, ValueSome grid)
    else
      struct (terrainVersion, false, ValueNone)

  let checkScenarioTransitions(scenario: ScenarioState) =
    let detectedTransitions =
      Pomo.Lib.ScenarioTransitions.TransitionDetection.detectTransitions
        scenario.entities
        scenario.scenario
      |> AMap.force

    detectedTransitions
    |> HashMap.iter(fun entityId trigger ->
      Debug.WriteLine(
        $"[Transition] Entity {entityId} triggered transition to scenario {trigger.ToScenarioId}"
      ))
