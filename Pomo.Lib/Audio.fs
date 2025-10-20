namespace Pomo.Lib.Audio

open System
open FSharp.UMX
open Pomo.Lib
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Audio

module Cues =

  let createBackgroundCue
    (audioStore: Services.IAudioStore)
    (scenario: Guid<ScenarioId>)
    (currentTick: TimeSpan)
    =
    let trigger = AmbienLoop scenario
    let clips = audioStore.findByTrigger trigger

    clips
    |> Array.map(fun audio ->
      PlayAudio {
        Id = %Guid.NewGuid()
        ClipId = audio
        Trigger = trigger
        SpatialInfo = ValueNone
        CreationTick = currentTick
        EntityId = ValueNone
      })

  let createAbilityCastCue
    (audioStore: Services.IAudioStore)
    (abilityId: int<AbilityId>)
    (actorId: Guid<EntityId>)
    (actorPosition: Position)
    (currentTick: TimeSpan)
    =
    let trigger = AbilityCast abilityId
    let clips = audioStore.findByTrigger trigger

    clips
    |> Array.map(fun clipId ->
      PlayAudio {
        Id = %Guid.NewGuid()
        ClipId = clipId
        Trigger = trigger
        SpatialInfo = ValueSome {
          Position = actorPosition
          MaxDistance = 500f
          Rolloff = 1f
        }
        CreationTick = currentTick
        EntityId = ValueSome actorId
      })

  let createAbilityImpactCue
    (audioStore: Services.IAudioStore)
    (abilityId: int<AbilityId>)
    (targetId: Guid<EntityId>)
    (targetPosition: Position)
    (currentTick: TimeSpan)
    =
    let trigger = AbilityImpact abilityId
    let clips = audioStore.findByTrigger trigger

    clips
    |> Array.map(fun clipId ->
      PlayAudio {
        Id = %Guid.NewGuid()
        ClipId = clipId
        Trigger = trigger
        SpatialInfo = ValueSome {
          Position = targetPosition
          MaxDistance = 500f
          Rolloff = 1f
        }
        CreationTick = currentTick
        EntityId = ValueSome targetId
      })
