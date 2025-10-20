namespace Pomo.Core

open System
open System.Collections.Generic
open Microsoft.Xna.Framework.Audio
open Microsoft.Xna.Framework.Media
open Microsoft.Xna.Framework.Content
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Audio
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Scenario


module AudioSystem =

  let loadedSounds = Dictionary<int<AudioClipId>, SoundEffect>()
  let activeSounds = Dictionary<Guid<AudioEventId>, SoundEffectInstance>()
  let loadedMusic = Dictionary<int<AudioClipId>, Song>()
  let mutable currentScenarioId: Guid<ScenarioId> voption = ValueNone

  let load(content: ContentManager) =
    loadedSounds.[2<AudioClipId>] <-
      content.Load<SoundEffect>("Audio/SkillActivation")

    loadedSounds.[3<AudioClipId>] <- content.Load<SoundEffect>("Audio/FireBlip")

    loadedSounds.[4<AudioClipId>] <-
      content.Load<SoundEffect>("Audio/DamageReceived")

    loadedSounds.[5<AudioClipId>] <-
      content.Load<SoundEffect>("Audio/MissedHit")

    loadedMusic.[1<AudioClipId>] <- content.Load<Song>("Audio/bg_temp")

  let updateScenarioMusic
    (audioStore: Services.IAudioStore)
    (scenarioId: Guid<ScenarioId>)
    =
    if currentScenarioId <> ValueSome scenarioId then
      currentScenarioId <- ValueSome scenarioId

      match audioStore.findMusicForScenario scenarioId with
      | ValueSome clipId ->
        match loadedMusic.TryGetValue clipId with
        | true, song ->
          MediaPlayer.IsRepeating <- true
          MediaPlayer.Volume <- 0.7f
          MediaPlayer.Play song
        | _ -> ()
      | ValueNone -> ()

  let calculateSpatialVolume
    (sourcePos: Position)
    (listenerPos: Position)
    (spatialInfo: SpatialInfo)
    =
    let dx = sourcePos.X - listenerPos.X
    let dy = sourcePos.Y - listenerPos.Y
    let distance = sqrt(dx * dx + dy * dy)

    if distance >= spatialInfo.MaxDistance then
      0f
    else
      let attenuation = 1f - distance / spatialInfo.MaxDistance
      attenuation ** spatialInfo.Rolloff

  let calculatePan
    (sourcePos: Position)
    (listenerPos: Position)
    (maxDistance: float32)
    =
    let dx = sourcePos.X - listenerPos.X
    let pan = dx / maxDistance
    max -1f (min 1f pan)

  let findPlayerListeners(scenario: ScenarioState) =
    scenario.entities
    |> AMap.filter'(fun entity -> entity.Factions |> HashSet.contains Player)
    |> AMap.map'(fun entity -> entity.Position)
    |> AMap.force
    |> HashMap.toValueArray

  let processAudioChanges
    (audioStore: Services.IAudioStore)
    (scenario: ScenarioState)
    (changes: AudioChange[])
    =
    updateScenarioMusic audioStore scenario.scenario.Id
    let listeners = findPlayerListeners scenario

    if listeners.Length = 0 then
      ()
    else
      let primaryListener = listeners[0]

      changes
      |> Array.iter(fun change ->
        match change with
        | PlayAudio audioEvent ->
          match loadedSounds.TryGetValue audioEvent.ClipId with
          | true, sound ->
            let instance = sound.CreateInstance()

            match audioEvent.SpatialInfo with
            | ValueSome spatialInfo ->
              let volume =
                calculateSpatialVolume
                  spatialInfo.Position
                  primaryListener
                  spatialInfo

              let pan =
                calculatePan
                  spatialInfo.Position
                  primaryListener
                  spatialInfo.MaxDistance

              instance.Volume <- volume
              instance.Pan <- pan
            | ValueNone ->
              let audio = audioStore.find audioEvent.ClipId
              instance.Volume <- audio.Volume

            activeSounds.Add(audioEvent.Id, instance)
            instance.Play()
          | false, _ -> ()
        | StopAudio eventId ->
          match activeSounds.TryGetValue(eventId) with
          | true, instance ->
            instance.Stop()
            instance.Dispose()
            activeSounds.Remove(eventId) |> ignore
          | false, _ -> ()
        | UpdateAudioPosition(eventId, position) ->
          match activeSounds.TryGetValue(eventId) with
          | true, instance ->
            let volume =
              calculateSpatialVolume position primaryListener {
                Position = position
                MaxDistance = 500f
                Rolloff = 1f
              }

            let pan = calculatePan position primaryListener 500f
            instance.Volume <- volume
            instance.Pan <- pan
          | false, _ -> ())

  let update() =
    activeSounds
    |> Seq.filter(fun kvp -> kvp.Value.State = SoundState.Stopped)
    |> Seq.map(fun kvp -> kvp.Key)
    |> Seq.iter(fun eventId ->
      let instance = activeSounds.[eventId]
      instance.Dispose()
      activeSounds.Remove(eventId) |> ignore)
