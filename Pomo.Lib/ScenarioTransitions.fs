namespace Pomo.Lib.ScenarioTransitions

open System
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Scenario

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
        found <-
          ValueSome {
            Position = transition.FromPosition
            Range = 32.0f
            ToScenarioId = transition.ToScenarioId
            ToPosition = transition.ToPosition
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
