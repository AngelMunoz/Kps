namespace Pomo.Lib.Operations

open System
open FSharp.Data.Adaptive
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.Inventory
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.State
open Pomo.Lib.Gameplay
open Pomo.Lib.Rules
open Pomo.Lib.Domain.Scenario

module Errors =

  [<Struct>]
  type EquipError =
    | InvalidSlot
    | IncompatibleEquipment

  [<Struct>]
  type AbilityError =
    | AbilityNotFound
    | OnCooldown
    | InsufficientResources
    | InvalidTarget
    | Stunned
    | Silenced
    | AlreadyKnown
    | NotKnown
    | RequirementsNotMet

  [<Struct>]
  type AdvancementError =
    | AlreadyMaxStage
    | RequirementsNotMet

  [<Struct>]
  type ResourceError = | InvalidAmount

  [<Struct>]
  type EffectError = | EffectNotFound

  [<Struct>]
  type OperationError =
    | EquipError of equipError: EquipError
    | AbilityError of abilityError: AbilityError
    | AdvancementError of advancementError: AdvancementError
    | ResourceError of resourceError: ResourceError
    | EffectError of effectError: EffectError
    | EntityNotFound


open Errors

/// High-level API surface for GameState operations
module GameState =

  /// Creates a new entity with the given profession and base attributes.
  /// Returns the new EntityId and StateChange to apply via Resolution.apply.
  let createEntity
    (configure: EntityComponents -> EntityComponents)
    (services: Services.EngineServices)
    (kit: CharacterKits.CharacterKit)
    =

    let newId = Guid.NewGuid() |> UMX.tag<EntityId>

    let passiveEffects =
      kit.StarterAbilities
      |> HashSet.fold
        (fun acc abilityId ->
          match services.abilityStore.tryFind abilityId with
          | ValueSome(Abilities.Passive passiveDef) ->
            passiveDef.Effects
            |> Array.fold
              (fun effectAcc effectId ->
                match services.effectStore.tryFind effectId with
                | ValueSome effectDef ->
                  let activeEffect = {
                    EffectId = effectId
                    SourceId = newId
                    RemainingTicks =
                      effectDef.Duration.Ticks
                      |> ValueOption.defaultValue TimeSpan.Zero
                    NextTickIn =
                      effectDef.Duration.Interval
                      |> ValueOption.defaultValue TimeSpan.Zero
                    Stacks = 1
                    Definition = effectDef
                  }

                  HashMap.add effectId activeEffect effectAcc
                | ValueNone -> effectAcc)
              acc
          | _ -> acc)
        HashMap.empty

    let newEntity = {
      Factions = HashSet.empty
      Identity = kit.Profession
      BaseStats = kit.BaseStats
      Resources = {
        HP = kit.BaseStats.Charm * 10
        MP = kit.BaseStats.Magic * 5
        Status = Alive
      }
      Position = { X = 0f; Y = 0f }
      Movement = {
        Destination = ValueNone
        Path = []
      }
      Effects = passiveEffects
      Abilities = kit.StarterAbilities
      AbilityCooldowns = HashMap.empty
      PartyId = ValueNone
      Inventory = HashMap.empty
      EquippedItems = HashMap.empty
    }

    {
      StateChange.empty with
          additions = HashMap.ofList [ newId, configure newEntity ]
    }

  /// Removes an entity from the game state.
  let removeEntity entityId = {
    StateChange.empty with
        removals = [| entityId |]
  }

  let getEntity entityId (state: GameState) =
    adaptive {
      let! scenario = Scenario.ActiveScenario state
      return! scenario.entities |> AMap.tryFind entityId
    }
    |> AVal.force


  let activateAbility
    entityId
    (abilityId: int<AbilityId>)
    (targets: Guid<EntityId>[])
    (state: GameState)
    : aval<StateChange> =

    let action = {
      actor = entityId
      target = EntityTargets targets
      abilityId = abilityId
    }

    let command = UseAbility action
    CommandHandler.evaluate state command

  let activateAbilityAtPosition
    entityId
    (abilityId: int<AbilityId>)
    (position: Position)
    (state: GameState)
    : aval<StateChange> =

    let action = {
      actor = entityId
      target = PositionTarget position
      abilityId = abilityId
    }

    let command = UseAbility action
    CommandHandler.evaluate state command

  /// Returns abilities not on cooldown for an entity.
  /// Uses direct access to AbilityCooldowns and Abilities.
  let inline getReadyAbilities entityId (state: GameState) =
    adaptive {
      let! scenario = Scenario.ActiveScenario state
      let! found = scenario.entities |> AMap.tryFind entityId
      let! gameTime = scenario.gameTime

      match found with
      | None -> return HashSet.empty
      | Some components ->
        return
          components.AbilityCooldowns
          |> HashMap.filter(fun abilityId readyTick -> readyTick <= gameTime)
          |> HashMap.keys
    }
    |> AVal.force

  /// Forces evaluation of adaptive stats and returns snapshot.
  let inline getDerivedStatsSnapshot entityId (state: GameState) =
    adaptive {
      let! stats = DerivedStats.byGameState state
      return! stats |> AMap.tryFind entityId
    }
    |> AVal.force

  /// Force and apply an adaptive StateChange with time update. (applyTick)
  let inline forceAndApply(state: GameState) =
    AVal.force >> GameState.apply state

  let inline apply (state: GameState) (change: StateChange) =
    GameState.apply state change


module ScenarioState =

  let create scenario : ScenarioState = {
    scenario = scenario
    entities = cmap()
    gameTime = cval TimeSpan.Zero
    battleContext = ValueNone
    battleInstances = cmap()
    pendingDuels = cmap()
    pendingPartyDuels = cmap()
    parties = cmap()
    floatingTexts = cmap()
    projectiles = cmap()
    aoes = cmap()
    impacts = cmap()
    pendingResolutions = cmap()
    aiControllers = cmap()
    activeZones = cmap()
  }
