namespace Pomo.Lib.ScenarioTransitions

open System
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Scenario
open Pomo.Lib.Collision


[<Struct>]
type TransitionTrigger = {
  Position: Position
  Range: float32
  ToScenarioId: Guid<ScenarioId>
  ToPosition: Position
  RequiresCondition: (unit -> bool) voption
}

[<Struct>]
type TransitionState =
  | Inactive
  | Detected of targetScenario: Guid<ScenarioId> * targetPosition: Position
  | InProgress of
    targetScenario: Guid<ScenarioId> *
    targetPosition: Position *
    progress: float32
  | Completed of targetScenario: Guid<ScenarioId> * targetPosition: Position

module TransitionDetection =
  let checkProximity
    (entityPos: Position)
    (transitions: ScenarioTransition[])
    : TransitionTrigger voption =
    let mutable found = ValueNone
    let mutable i = 0

    while found.IsNone && i < transitions.Length do
      let transition = transitions.[i]
      let dx = entityPos.X - transition.FromPosition.X
      let dy = entityPos.Y - transition.FromPosition.Y
      let distance = sqrt(dx * dx + dy * dy)

      if distance <= 32.0f then // Default trigger range
        let canTransition =
          match transition.RequiresCondition with
          | ValueSome condition -> condition()
          | ValueNone -> true

        if canTransition then
          found <-
            ValueSome {
              Position = transition.FromPosition
              Range = 32.0f
              ToScenarioId = transition.ToScenarioId
              ToPosition = transition.ToPosition
              RequiresCondition = transition.RequiresCondition
            }

      i <- i + 1

    found

  let detectTransitions
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (scenario: Scenario)
    =
    entities
    |> AMap.choose(fun entityId components ->
      match checkProximity components.Position scenario.Transitions with
      | ValueSome trigger -> Some(trigger)
      | ValueNone -> None)

module TransitionExecution =
  let preserveEntityState(entity: EntityComponents) : EntityComponents = {
    entity with
        Movement = {
          entity.Movement with
              Destination = ValueNone
              Path = []
        }
  }

  let migrateEntity
    (entityId: Guid<EntityId>)
    (entity: EntityComponents)
    (newPosition: Position)
    (fromScenarioId: Guid<ScenarioId>)
    (toScenarioId: Guid<ScenarioId>)
    =

    let preservedEntity = preserveEntityState entity

    let updatedEntity = {
      preservedEntity with
          Position = newPosition
    }

    struct (struct (toScenarioId, updatedEntity),
            struct (fromScenarioId, entityId))

  let executeTransition
    (scenarios: cmap<Guid<ScenarioId>, ScenarioState>)
    (activeScenarioId: cval<Guid<ScenarioId>>)
    (entityId: Guid<EntityId>)
    (trigger: TransitionTrigger)
    : unit =

    let currentScenarioId = activeScenarioId |> AVal.force
    let scenarios = scenarios |> AMap.force

    match scenarios |> HashMap.tryFindV currentScenarioId with
    | ValueSome currentScenario ->
      let entities = currentScenario.entities |> AMap.toAVal |> AVal.force

      match entities |> HashMap.tryFindV entityId with
      | ValueSome entity ->
        let struct (struct (targetScenarioId, updatedEntity),
                    struct (sourceScenarioId, entityToRemove)) =
          migrateEntity
            entityId
            entity
            trigger.ToPosition
            currentScenarioId
            trigger.ToScenarioId

        // Create state changes for removing from source and adding to target
        transact(fun () ->
          match scenarios |> HashMap.tryFindV sourceScenarioId with
          | ValueSome sourceScenario ->
            sourceScenario.entities.Remove entityToRemove |> ignore
          | ValueNone -> ())

        transact(fun () ->
          match scenarios |> HashMap.tryFindV targetScenarioId with
          | ValueSome targetScenario ->
            targetScenario.entities.[entityId] <- updatedEntity
          | ValueNone -> ())

        // Update active scenario
        transact(fun () -> activeScenarioId.Value <- targetScenarioId)

      | ValueNone -> ()

    | ValueNone -> ()

module VisualTransitionEffects =
  [<Struct>]
  type TransitionEffect = {
    EffectType: string
    Duration: float32
    Progress: float32
    IsActive: bool
  }

  let createFadeEffect(duration: float32) : TransitionEffect = {
    EffectType = "fade"
    Duration = duration
    Progress = 0.0f
    IsActive = true
  }

  let updateEffect
    (deltaTime: float32)
    (effect: TransitionEffect)
    : TransitionEffect =
    if effect.IsActive then
      let newProgress = min 1.0f (effect.Progress + deltaTime / effect.Duration)

      {
        effect with
            Progress = newProgress
            IsActive = newProgress < 1.0f
      }
    else
      effect

  let getFadeAlpha(effect: TransitionEffect) : float32 =
    match effect.EffectType with
    | "fade" when effect.IsActive ->
      if effect.Progress <= 0.5f then
        // Fade out (first half)
        1.0f - (effect.Progress * 2.0f)
      else
        // Fade in (second half)
        (effect.Progress - 0.5f) * 2.0f
    | _ -> 1.0f

module ScenarioDefinitions =
  let createTownScenario(id: Guid<ScenarioId>) : Scenario = {
    Id = id
    Name = "Peaceful Town"
    BoundsWidth = 800f
    BoundsHeight = 600f
    BattleEnabled = false
    CombatType = PvE
    TerrainObjects =
      IndexList.ofList [
        // Buildings as blocked areas
        {
          Id = %Guid.NewGuid()
          Position = { X = 200f; Y = 150f }
          CollisionGeometry =
            Polygon(
              [|
                { X = 150f; Y = 100f }
                { X = 250f; Y = 100f }
                { X = 250f; Y = 200f }
                { X = 150f; Y = 200f }
              |]
            )
          TerrainType = Blocked
          DepthLayer = 0.6f
          SpriteId = ValueSome "town_house"
        }
        // Fountain as decorative water
        {
          Id = %Guid.NewGuid()
          Position = { X = 400f; Y = 300f }
          CollisionGeometry = Circle({ X = 400f; Y = 300f }, 25f)
          TerrainType = Water
          DepthLayer = 0.4f
          SpriteId = ValueSome "fountain"
        }
      ]
    VisualLayers = [|
      {
        SpriteId = "town_background"
        Position = { X = 400f; Y = 300f }
        DepthLayer = 0.1f
        Parallax = 0.8f
      }
    |]
    Transitions = [|
      {
        FromPosition = { X = 750f; Y = 300f } // East exit
        ToScenarioId = %Guid.NewGuid() // Will be set when wilderness is created
        ToPosition = { X = 50f; Y = 300f } // West entrance of wilderness
        RequiresCondition = ValueNone
      }
    |]
  }

  let createWildernessScenario
    (id: Guid<ScenarioId>)
    (townId: Guid<ScenarioId>)
    : Scenario =
    {
      Id = id
      Name = "Dark Wilderness"
      BoundsWidth = 1000f
      BoundsHeight = 800f
      BattleEnabled = true
      CombatType = PvE
      TerrainObjects =
        IndexList.ofList [
          // Dense forest areas as blocked terrain
          {
            Id = %Guid.NewGuid()
            Position = { X = 300f; Y = 200f }
            CollisionGeometry =
              Polygon(
                [|
                  { X = 250f; Y = 150f }
                  { X = 350f; Y = 150f }
                  { X = 380f; Y = 220f }
                  { X = 270f; Y = 250f }
                |]
              )
            TerrainType = Blocked
            DepthLayer = 0.7f
            SpriteId = ValueSome "dense_trees"
          }
          // Swamp areas
          {
            Id = %Guid.NewGuid()
            Position = { X = 600f; Y = 500f }
            CollisionGeometry = Circle({ X = 600f; Y = 500f }, 60f)
            TerrainType = Water
            DepthLayer = 0.3f
            SpriteId = ValueSome "swamp"
          }
          // Hazard area (poisonous plants)
          {
            Id = %Guid.NewGuid()
            Position = { X = 800f; Y = 300f }
            CollisionGeometry = Circle({ X = 800f; Y = 300f }, 40f)
            TerrainType = Hazard
            DepthLayer = 0.5f
            SpriteId = ValueSome "poison_plants"
          }
        ]
      VisualLayers = [|
        {
          SpriteId = "wilderness_background"
          Position = { X = 500f; Y = 400f }
          DepthLayer = 0.1f
          Parallax = 0.9f
        }
      |]
      Transitions = [|
        {
          FromPosition = { X = 50f; Y = 300f } // West entrance
          ToScenarioId = townId
          ToPosition = { X = 750f; Y = 300f } // East exit of town
          RequiresCondition = ValueNone
        }
        {
          FromPosition = { X = 950f; Y = 400f } // East exit to dungeon
          ToScenarioId = %Guid.NewGuid() // Will be set when dungeon is created
          ToPosition = { X = 100f; Y = 400f } // West entrance of dungeon
          RequiresCondition = ValueNone
        }
      |]
    }

  let createDungeonScenario
    (id: Guid<ScenarioId>)
    (wildernessId: Guid<ScenarioId>)
    : Scenario =
    {
      Id = id
      Name = "Ancient Dungeon"
      BoundsWidth = 600f
      BoundsHeight = 600f
      BattleEnabled = true
      CombatType = PvPvE
      TerrainObjects =
        IndexList.ofList [
          // Dungeon walls
          {
            Id = %Guid.NewGuid()
            Position = { X = 200f; Y = 200f }
            CollisionGeometry =
              Polygon(
                [|
                  { X = 150f; Y = 150f }
                  { X = 250f; Y = 150f }
                  { X = 250f; Y = 250f }
                  { X = 150f; Y = 250f }
                |]
              )
            TerrainType = Blocked
            DepthLayer = 0.8f
            SpriteId = ValueSome "stone_wall"
          }
          // Lava pit hazard
          {
            Id = %Guid.NewGuid()
            Position = { X = 400f; Y = 300f }
            CollisionGeometry = Circle({ X = 400f; Y = 300f }, 35f)
            TerrainType = Hazard
            DepthLayer = 0.4f
            SpriteId = ValueSome "lava_pit"
          }
          // Water trap
          {
            Id = %Guid.NewGuid()
            Position = { X = 300f; Y = 450f }
            CollisionGeometry =
              Polygon(
                [|
                  { X = 280f; Y = 430f }
                  { X = 320f; Y = 430f }
                  { X = 320f; Y = 470f }
                  { X = 280f; Y = 470f }
                |]
              )
            TerrainType = Water
            DepthLayer = 0.2f
            SpriteId = ValueSome "water_trap"
          }
        ]
      VisualLayers = [|
        {
          SpriteId = "dungeon_background"
          Position = { X = 300f; Y = 300f }
          DepthLayer = 0.1f
          Parallax = 1.0f
        }
      |]
      Transitions = [|
        {
          FromPosition = { X = 100f; Y = 400f } // West entrance
          ToScenarioId = wildernessId
          ToPosition = { X = 950f; Y = 400f } // East exit of wilderness
          RequiresCondition = ValueNone
        }
      |]
    }

  let createConnectedScenarios() : (Scenario * Scenario * Scenario) =
    let townId = %Guid.NewGuid()
    let wildernessId = %Guid.NewGuid()
    let dungeonId = %Guid.NewGuid()

    let town = createTownScenario townId
    let wilderness = createWildernessScenario wildernessId townId
    let dungeon = createDungeonScenario dungeonId wildernessId

    // Update town's transition to point to wilderness
    let updatedTown = {
      town with
          Transitions = [|
            {
              town.Transitions.[0] with
                  ToScenarioId = wildernessId
            }
          |]
    }

    // Update wilderness's transition to point to dungeon
    let updatedWilderness = {
      wilderness with
          Transitions = [|
            wilderness.Transitions.[0]
            {
              wilderness.Transitions.[1] with
                  ToScenarioId = dungeonId
            }
          |]
    }

    (updatedTown, updatedWilderness, dungeon)
