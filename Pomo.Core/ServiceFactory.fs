namespace Pomo.Core

open Pomo.Lib.Domain.Services
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Content
open FSharp.Data.Adaptive

module ServiceFactory =

  let createServices(scenarios: cmap<_, _>) : EngineServices = {
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
    projectileStore =
      { new IProjectileStore with
          member _.tryFind projectileId =
            ProjectileStore.definitions
            |> Map.tryFind projectileId
            |> ValueOption.ofOption

          member _.find projectileId =
            ProjectileStore.definitions |> Map.find projectileId
      }
    aoeStore =
      { new IAoeStore with
          member _.tryFind aoeId =
            AoeStore.definitions |> Map.tryFind aoeId |> ValueOption.ofOption

          member _.find aoeId = AoeStore.definitions |> Map.find aoeId
      }
    impactStore =
      { new IImpactStore with
          member _.tryFind impactId =
            ImpactStore.definitions
            |> Map.tryFind impactId
            |> ValueOption.ofOption

          member _.find impactId =
            ImpactStore.definitions |> Map.find impactId
      }
    audioStore =
      { new IAudioStore with
          member _.tryFind clipId =
            AudioStore.definitions |> Map.tryFind clipId |> ValueOption.ofOption

          member _.find clipId =
            AudioStore.definitions |> Map.find clipId

          member _.findByTrigger trigger =
            AudioStore.triggerMap
            |> Map.tryFind trigger
            |> Option.defaultValue Array.empty

          member _.findMusicForScenario scenarioId =
            let scenario =
              scenarios.Value
              |> HashMap.tryFindV scenarioId
              |> ValueOption.map _.scenario

            scenario
            |> ValueOption.bind(fun s ->
              AudioStore.scenarioMusicMap
              |> Map.tryFind s.Name
              |> ValueOption.ofOption)
      }
    rng = fun () -> System.Random.Shared.NextDouble()
  }
