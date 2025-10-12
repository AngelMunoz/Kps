namespace Pomo.Lib.Scenario

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components

[<Measure>]
type ScenarioId


[<Struct>]
type Scenario = {
  Id: Guid<ScenarioId>
  Name: string
  Bounds: ScenarioBounds
}

[<Struct>]
type ScenarioState = {
  scenario: Scenario
  entities: cmap<Guid<EntityId>, EntityComponents>
  gameTime: cval<int64<Tick>>
}

module ScenarioState =
  let create id name bounds : ScenarioState = {
    scenario = {
      Id = id
      Name = name
      Bounds = bounds
    }
    entities = cmap()
    gameTime = cval 0L<Tick>
  }
