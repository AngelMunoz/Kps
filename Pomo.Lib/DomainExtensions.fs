[<AutoOpen>]
module Pomo.Lib.Domain.Extensions

open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain


type Attributes.DerivedStats with

  member inline this.ResistanceValue element =
    this.ElementResistances |> HashMap.tryFindV element

  member inline this.AttributeValue element =
    this.ElementAttributes |> HashMap.tryFindV element

  static member inline Zero: Attributes.DerivedStats = {
    AP = 0
    AC = 0
    DX = 0
    MP = 0
    MA = 0
    MD = 0
    WT = 0
    DA = 0
    LK = 0
    HP = 0
    DP = 0
    HV = 0
    ElementAttributes = HashMap.empty
    ElementResistances = HashMap.empty
  }

type Effects.Duration with

  member inline this.Ticks =
    match this with
    | Effects.Instant
    | Effects.PermanentLoop _
    | Effects.Permanent -> ValueNone
    | Effects.Timed d -> ValueSome d
    | Effects.Loop(_, d) -> ValueSome d

  member inline this.Interval =
    match this with
    | Effects.Loop(i, _) -> ValueSome i
    | Effects.PermanentLoop i -> ValueSome i
    | Effects.Permanent -> ValueNone
    | Effects.Timed _
    | Effects.Instant -> ValueNone


module Position =
  let zero: Position = { X = 0f; Y = 0f }

module ResourceType =

  let inline asString r =
    match r with
    | ResourceType.HP -> "HP"
    | ResourceType.MP -> "MP"

module Resources =

  let zero: Attributes.Resources = {
    HP = 0
    MP = 0
    Status = Attributes.Status.Alive
  }

module TickResult =
  let Zero: AggregatedEffects.TickResult = {
    Damage = 0
    Resources = Resources.zero
  }

module DamageResult =

  let Zero: Abilities.DamageResult = {
    BaseDamage = 0
    DamageType = Abilities.DamageType.Neutral
    Element = Attributes.Neutral
    ElementalDamage = 0
  }


module StateChange =
  let empty: State.StateChange = {
    updates = HashMap.empty
    additions = HashMap.empty
    removals = Array.empty
    gameTime = ValueNone
    scenarioChanges = Array.empty
    teleports = Array.empty
    visualEffects = Array.empty
    audioChanges = Array.empty
    aiControllers = HashMap.empty
  }


module Scenario =

  let empty: Scenario.Scenario = {
    Id = System.Guid.Empty |> UMX.tag<ScenarioId>
    Name = System.String.Empty
    BoundsWidth = 0f
    BoundsHeight = 0f
    BattleEnabled = false
    CombatType = PvE
    EngagementMode = Peaceful
    TerrainObjects = IndexList.empty
    VisualLayers = Array.empty
    Transitions = Array.empty
  }


module ResizeArray =

  let empty<'T>() = ResizeArray<'T>()

  let inline add (item: 'T) (arr: ResizeArray<'T>) =
    arr.Add item
    arr

  let inline addRange (items: seq<'T>) (arr: ResizeArray<'T>) =
    arr.AddRange items
    arr

  let inline toArray(arr: ResizeArray<'T>) = arr.ToArray()


module ValueOption =

  let inline toResult error valueOpt =
    match valueOpt with
    | ValueSome v -> Result.Ok v
    | ValueNone -> Result.Error error
