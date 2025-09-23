[<AutoOpen>]
module Pomo.Lib.Domain.Extensions

open FSharp.Data.Adaptive
open Pomo.Lib.Domain


type Attributes.DerivedStats with

  member inline this.ResistanceValue element =
    this.ElementResistances |> HashMap.tryFindV element

  member inline this.AttributeValue element =
    this.ElementAttributes |> HashMap.tryFindV element

type Effects.Duration with

  member inline this.Ticks =
    match this with
    | Effects.Instant -> ValueNone
    | Effects.Timed d -> ValueSome d
    | Effects.Loop(_, d) -> ValueSome d

  member inline this.Interval =
    match this with
    | Effects.Loop(i, _) -> ValueSome i
    | _ -> ValueNone


module ResourceType =

  let inline asString r =
    match r with
    | Abilities.ResourceType.HP -> "HP"
    | Abilities.ResourceType.MP -> "MP"

module TickResult =
  let Zero: AggregatedEffects.TickResult = { Damage = 0; Healing = 0 }
