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
            EffectStore.definitions |> HashMap.tryFindV effectId

          member _.find effectId =
            EffectStore.definitions |> HashMap.find effectId
      }
    abilityStore =
      { new IAbilityStore with
          member _.tryFind abilityId =
            AbilityStore.definitions |> HashMap.tryFindV abilityId

          member _.find abilityId =
            AbilityStore.definitions |> HashMap.find abilityId
      }
    formulaStore =
      { new IFormulaStore with
          member _.tryFind formulaId =
            FormulaStore.definitions |> HashMap.tryFindV formulaId

          member _.find formulaId =
            FormulaStore.definitions |> HashMap.find formulaId
      }
    projectileStore =
      { new IProjectileStore with
          member _.tryFind projectileId =
            ProjectileStore.definitions |> HashMap.tryFindV projectileId

          member _.find projectileId =
            ProjectileStore.definitions |> HashMap.find projectileId
      }
    aoeStore =
      { new IAoeStore with
          member _.tryFind aoeId =
            AoeStore.definitions |> HashMap.tryFindV aoeId

          member _.find aoeId =
            AoeStore.definitions |> HashMap.find aoeId
      }
    impactStore =
      { new IImpactStore with
          member _.tryFind impactId =
            ImpactStore.definitions |> HashMap.tryFindV impactId

          member _.find impactId =
            ImpactStore.definitions |> HashMap.find impactId
      }
    audioStore =
      { new IAudioStore with
          member _.tryFind clipId =
            AudioStore.definitions |> HashMap.tryFindV clipId

          member _.find clipId =
            AudioStore.definitions |> HashMap.find clipId

          member _.findByTrigger trigger =
            AudioStore.triggerMap
            |> HashMap.tryFindV trigger
            |> ValueOption.defaultValue Array.empty

          member _.findMusicForScenario scenarioId =
            let scenario =
              scenarios.Value
              |> HashMap.tryFindV scenarioId
              |> ValueOption.map _.scenario

            scenario
            |> ValueOption.bind(fun s ->
              AudioStore.scenarioMusicMap |> HashMap.tryFindV s.Name)
      }
    aiArchetypeStore =
      { new IAIArchetypeStore with
          member _.tryFind archetypeId =
            AIArchetypeStore.definitions |> HashMap.tryFindV archetypeId

          member _.find archetypeId =
            AIArchetypeStore.definitions |> HashMap.find archetypeId
      }
    itemStore =
      { new IItemStore with
          member _.tryFind itemId =
            ItemStore.definitions |> HashMap.tryFindV itemId

          member _.find itemId =
            ItemStore.definitions |> HashMap.find itemId
      }
    rng = fun () -> System.Random().NextDouble()
  }
