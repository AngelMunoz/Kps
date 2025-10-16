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
    (kit: CharacterKits.CharacterKit)
    =

    let newId = Guid.NewGuid() |> UMX.tag<EntityId>

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
        Speed = 100f
        Destination = ValueNone
        Path = []
      }
      Effects = HashMap.empty
      Abilities = kit.StarterAbilities
      AbilityCooldowns = HashMap.empty
      Equipment = HashMap.empty
      PartyId = ValueNone
    }

    {
      updates = HashMap.empty
      additions = HashMap.ofList [ newId, configure newEntity ]
      removals = Array.empty
      gameTime = ValueNone
      scenarioChanges = Array.empty
      teleports = Array.empty
      visualEffects = Array.empty
    }

  /// Removes an entity from the game state.
  let removeEntity entityId = {
    updates = HashMap.empty
    additions = HashMap.empty
    removals = [| entityId |]
    gameTime = ValueNone
    scenarioChanges = Array.empty
    teleports = Array.empty
    visualEffects = Array.empty
  }

  let getEntity entityId (state: GameState) =
    adaptive {
      let! scenario = GameState.getActiveScenario state
      return! scenario.entities |> AMap.tryFind entityId
    }
    |> AVal.force


  /// Equips an item to the specified slot.
  /// Returns StateChange or error (entity not found, invalid slot).
  let equipItem
    entityId
    (slot: Slot)
    (equipment: Equipment)
    (state: GameState)
    =

    let entitiesMap =
      adaptive {
        let! scenario = GameState.getActiveScenario state
        return! scenario.entities |> AMap.toAVal
      }
      |> AVal.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      // Check if equipment is compatible with slot
      let isCompatible =
        match slot, equipment.Slot with
        | Weapon1, Weapon1 -> true
        | Weapon2, Weapon2 -> true
        | Head, Head -> true
        | Chest, Chest -> true
        | Hands, Hands -> true
        | Legs, Legs -> true
        | Accessory, Accessory -> true
        | _ -> false

      if not isCompatible then
        Error(OperationError.EquipError IncompatibleEquipment)
      else
        let updatedEquipment =
          components.Equipment |> HashMap.add slot equipment

        let updatedComponents = {
          components with
              Equipment = updatedEquipment
        }

        Ok {
          updates = HashMap.ofList [ entityId, updatedComponents ]
          additions = HashMap.empty
          removals = Array.empty
          gameTime = ValueNone
          scenarioChanges = Array.empty
          teleports = Array.empty
          visualEffects = Array.empty
        }

  /// Removes equipment from the specified slot.
  /// Returns StateChange with equipment removed.
  let unequipItem
    entityId
    (slot: Slot)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    =
    let found = entities |> AMap.tryFind entityId |> AVal.force

    match found with
    | None -> Error EntityNotFound
    | Some components ->
      let updatedEquipment = components.Equipment |> HashMap.remove slot

      let updatedComponents = {
        components with
            Equipment = updatedEquipment
      }


      Ok {
        updates = HashMap.ofList [ entityId, updatedComponents ]
        additions = HashMap.empty
        removals = Array.empty
        gameTime = ValueNone
        scenarioChanges = Array.empty
        teleports = Array.empty
        visualEffects = Array.empty
      }


  /// Swaps equipment between two slots (e.g., Weapon1 ↔ Weapon2).
  /// Single atomic StateChange for both slot modifications.
  let swapEquipment entityId (slot1: Slot) (slot2: Slot) (state: GameState) =

    let entitiesMap =
      adaptive {
        let! scenario = GameState.getActiveScenario state
        return! scenario.entities |> AMap.toAVal
      }
      |> AVal.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      let equipment1 = components.Equipment |> HashMap.tryFindV slot1
      let equipment2 = components.Equipment |> HashMap.tryFindV slot2

      let updatedEquipment =
        components.Equipment
        |> (fun eq ->
          match equipment2 with
          | ValueSome e2 -> HashMap.add slot1 e2 eq
          | ValueNone -> HashMap.remove slot1 eq)
        |> (fun eq ->
          match equipment1 with
          | ValueSome e1 -> HashMap.add slot2 e1 eq
          | ValueNone -> HashMap.remove slot2 eq)

      let updatedComponents = {
        components with
            Equipment = updatedEquipment
      }

      Ok {
        updates = HashMap.ofList [ entityId, updatedComponents ]
        additions = HashMap.empty
        removals = Array.empty
        gameTime = ValueNone
        scenarioChanges = Array.empty
        teleports = Array.empty
        visualEffects = Array.empty
      }

  let activateAbility
    entityId
    (abilityId: int<AbilityId>)
    (targets: Guid<EntityId>[])
    (state: GameState)
    : aval<StateChange> =

    let action = {
      actor = entityId
      targets = targets
      abilityId = abilityId
    }

    let command = UseAbility action
    Resolution.evaluate state command

  /// Returns all alive entities.
  /// Uses ASet.force on existing adaptive projection.
  let inline getAliveEntities(state: GameState) =
    adaptive {
      let! scenario = GameState.getActiveScenario state
      let! aliveEntities = Projections.aAlive scenario.entities |> AMap.toAVal
      return aliveEntities
    }
    |> AVal.force

  /// Returns abilities not on cooldown for an entity.
  /// Uses direct access to AbilityCooldowns and Abilities.
  let inline getReadyAbilities entityId (state: GameState) =
    adaptive {
      let! scenario = GameState.getActiveScenario state
      let! found = scenario.entities |> AMap.tryFind entityId

      match found with
      | None -> return HashMap.empty
      | Some components ->
        return!
          Projections.aReadyForEntity components scenario.gameTime
          |> AMap.toAVal
    }
    |> AVal.force

  /// Forces evaluation of adaptive stats and returns snapshot.
  let inline getDerivedStatsSnapshot entityId (state: GameState) =
    adaptive {
      let! stats = GameState.getDerivedStats state
      return! stats |> AMap.tryFind entityId
    }
    |> AVal.force

  /// Force and apply an adaptive StateChange with time update. (applyTick)
  let inline forceAndApply(state: GameState) =
    AVal.force >> GameState.apply state
