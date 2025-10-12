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
    (profession: Profession)
    (baseStats: BaseAttributes)
    factions
    =

    let newId = Guid.NewGuid() |> UMX.tag<EntityId>

    let newEntity = {
      Factions = HashSet.ofSeq factions
      Identity = profession
      BaseStats = baseStats
      Resources = {
        HP = baseStats.Charm * 10
        MP = baseStats.Magic * 5
        Status = Alive
      }
      Position = { X = 0f; Y = 0f }
      Movement = {
        Speed = 100f
        Destination = ValueNone
        Path = []
      }
      Effects = AList.empty
      Abilities = AList.empty
      AbilityCooldowns = AMap.empty
      Equipment = HashMap.empty
    }

    let change = {
      entities = HashMap.ofList [ newId, newEntity ]
      gameTime = ValueNone
    }

    struct (newId, change)

  /// Removes an entity from the game.
  /// Returns StateChange with entity marked as Dead.
  let removeEntity entityId (state: GameState) : StateChange =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueSome components ->
      let removedComponents = {
        components with
            Resources = {
              components.Resources with
                  Status = Dead
            }
      }

      {
        entities = HashMap.ofList [ entityId, removedComponents ]
        gameTime = ValueNone
      }
    | ValueNone ->
        {
          entities = HashMap.empty
          gameTime = ValueNone
        }

  /// Retrieves entity components (non-reactive query).
  /// Uses synchronous access to entities map.
  let getEntity entityId (state: GameState) =
    state.entities |> AMap.force |> HashMap.tryFindV entityId

  // ============================================================================
  // EQUIPMENT OPERATIONS
  // ============================================================================

  /// Equips an item to the specified slot.
  /// Returns StateChange or error (entity not found, invalid slot).
  let equipItem
    entityId
    (slot: Slot)
    (equipment: Equipment)
    (state: GameState)
    =

    let entitiesMap = state.entities |> AMap.force

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
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
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
        entities = HashMap.ofList [ entityId, updatedComponents ]
        gameTime = ValueNone
      }


  /// Swaps equipment between two slots (e.g., Weapon1 ↔ Weapon2).
  /// Single atomic StateChange for both slot modifications.
  let swapEquipment entityId (slot1: Slot) (slot2: Slot) (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

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
        entities = HashMap.ofList [ entityId, updatedComponents ]
        gameTime = ValueNone
      }

  // ============================================================================
  // ABILITY OPERATIONS
  // ============================================================================

  /// Primary interface for using abilities in combat.
  /// Uses existing Command pattern: Creates UseAbility command and calls Resolution.step.
  /// Returns adaptive StateChange for reactive resolution.
  /// MonoGame must evaluate with AVal.force and apply.
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
    Resolution.resolve state command

  /// Adds a new ability to an entity's ability list.
  /// Direct operation: modifies Abilities alist.
  let learnAbility entityId (abilityId: int<AbilityId>) (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      // Check if ability already known
      let knownAbilities = components.Abilities |> AList.force

      if knownAbilities |> IndexList.exists(fun _ v -> v = abilityId) then
        Error(OperationError.AbilityError AlreadyKnown)
      else
        // Add ability to the list
        let updatedAbilities = components.Abilities |> AList.force
        let newAbilities = updatedAbilities |> IndexList.add abilityId

        let updatedComponents = {
          components with
              Abilities = AList.ofIndexList newAbilities
        }

        Ok {
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
        }

  /// Removes an ability from an entity.
  /// Direct operation: removes from Abilities alist.
  let forgetAbility entityId (abilityId: int<AbilityId>) (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      let knownAbilities = components.Abilities |> AList.force

      if not(knownAbilities |> IndexList.exists(fun _ v -> v = abilityId)) then
        Error(OperationError.AbilityError NotKnown)
      else
        let updatedAbilities =
          knownAbilities |> IndexList.filter((<>) abilityId)

        let updatedComponents = {
          components with
              Abilities = AList.ofIndexList updatedAbilities
        }

        Ok {
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
        }

  // ============================================================================
  // PROFESSION ADVANCEMENT
  // ============================================================================

  /// Advances entity's profession to next stage (First → Second → Third).
  /// Validates stage progression rules and updates BaseStats and Identity.Profession.
  let advanceStage entityId (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      match components.Identity.Stage with
      | Third -> Error(OperationError.AdvancementError AlreadyMaxStage)
      | First ->
        let newProfession = {
          components.Identity with
              Stage = Second
        }
        // Boost base stats on advancement
        let newBaseStats = {
          components.BaseStats with
              Power = components.BaseStats.Power + 2
              Magic = components.BaseStats.Magic + 2
              Sense = components.BaseStats.Sense + 2
              Charm = components.BaseStats.Charm + 2
        }

        let updatedComponents = {
          components with
              Identity = newProfession
              BaseStats = newBaseStats
        }

        Ok {
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
        }
      | Second ->
        let newProfession = {
          components.Identity with
              Stage = Third
        }

        let newBaseStats = {
          components.BaseStats with
              Power = components.BaseStats.Power + 3
              Magic = components.BaseStats.Magic + 3
              Sense = components.BaseStats.Sense + 3
              Charm = components.BaseStats.Charm + 3
        }

        let updatedComponents = {
          components with
              Identity = newProfession
              BaseStats = newBaseStats
        }

        Ok {
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
        }

  /// Query operation: checks if entity meets requirements for stage advancement.
  /// No state mutation, no StateChange needed.
  let canAdvanceStage entityId (state: GameState) : bool =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> false
    | ValueSome components ->
      match components.Identity.Stage with
      | Third -> false
      | First
      | Second -> true

  // ============================================================================
  // RESOURCE MANAGEMENT
  // ============================================================================

  /// Restores HP (capped at max HP from DerivedStats).
  /// Direct operation: modifies Resources.
  let healEntity entityId (amount: int) (state: GameState) =

    if amount < 0 then
      Error(OperationError.ResourceError InvalidAmount)
    else
      let entitiesMap = state.entities |> AMap.force

      match entitiesMap |> HashMap.tryFindV entityId with
      | ValueNone -> Error EntityNotFound
      | ValueSome components ->
        // Get derived stats to find max HP - force derivedStats map first
        let derivedStatsMap = GameState.getDerivedStats state |> AMap.force

        let maxHP = derivedStatsMap[entityId].HP

        let newHP = min (components.Resources.HP + amount) maxHP
        let updatedResources = { components.Resources with HP = newHP }

        let updatedComponents = {
          components with
              Resources = updatedResources
        }

        Ok {
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
        }

  /// Restores MP (capped at max MP from DerivedStats).
  /// Direct operation: modifies Resources.
  let restoreMP entityId (amount: int) (state: GameState) =

    if amount < 0 then
      Error(ResourceError InvalidAmount)
    else
      let entitiesMap = state.entities |> AMap.force

      match entitiesMap |> HashMap.tryFindV entityId with
      | ValueNone -> Error EntityNotFound
      | ValueSome components ->
        // Get derived stats to find max MP
        let derivedStatsMap = GameState.getDerivedStats state |> AMap.force
        let maxMP = derivedStatsMap[entityId].MP
        let newMP = min (components.Resources.MP + amount) maxMP
        let updatedResources = { components.Resources with MP = newMP }

        let updatedComponents = {
          components with
              Resources = updatedResources
        }

        Ok {
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
        }

  /// Applies direct damage (bypasses combat formulas).
  /// Direct operation: reduces HP, checks for death.
  let damageEntity entityId (amount: int) (state: GameState) =

    if amount < 0 then
      Error(OperationError.ResourceError InvalidAmount)
    else
      let entitiesMap = state.entities |> AMap.force

      match entitiesMap |> HashMap.tryFindV entityId with
      | ValueNone -> Error EntityNotFound
      | ValueSome components ->
        let newHP = max (components.Resources.HP - amount) 0
        let newStatus = if newHP = 0 then Dead else components.Resources.Status

        let updatedResources = {
          components.Resources with
              HP = newHP
              Status = newStatus
        }

        let updatedComponents = {
          components with
              Resources = updatedResources
        }

        Ok {
          entities = HashMap.ofList [ entityId, updatedComponents ]
          gameTime = ValueNone
        }

  /// Manually sets entity status (Alive, Dead, Disabled).
  /// Direct operation: modifies Resources.Status.
  let setResourceStatus entityId (status: Status) (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      let updatedResources = {
        components.Resources with
            Status = status
      }

      let updatedComponents = {
        components with
            Resources = updatedResources
      }

      Ok {
        entities = HashMap.ofList [ entityId, updatedComponents ]
        gameTime = ValueNone
      }

  // ============================================================================
  // EFFECT MANAGEMENT
  // ============================================================================

  /// Manually applies an effect to a target (duration, source entity).
  /// Direct operation: adds ActiveEffect to Effects alist.
  let applyEffect
    targetId
    sourceId
    (effectId: int<EffectId>)
    (duration: int64<Tick>)
    (state: GameState)
    =

    // Verify effect exists in store
    match state.services.effectStore.tryFind effectId with
    | ValueNone -> Error(OperationError.EffectError EffectNotFound)
    | ValueSome effectDef ->
      let entitiesMap = state.entities |> AMap.force

      match entitiesMap |> HashMap.tryFindV targetId with
      | ValueNone -> Error EntityNotFound
      | ValueSome components ->
        // ActiveEffect uses: SourceId, RemainingTicks, NextTickIn, Stacks, Definition
        let newEffect = {
          EffectId = effectId
          SourceId = sourceId
          RemainingTicks = duration
          NextTickIn = 0L<Tick> // Immediate first tick
          Stacks = 1
          Definition = effectDef
        }

        let currentEffects = components.Effects |> AList.force
        let updatedEffects = currentEffects |> IndexList.add newEffect

        let updatedComponents = {
          components with
              Effects = AList.ofIndexList updatedEffects
        }

        Ok {
          entities = HashMap.ofList [ targetId, updatedComponents ]
          gameTime = ValueNone
        }

  /// Removes all instances of an effect from an entity.
  /// Direct operation: filters Effects alist.
  let removeEffect entityId (effectId: int<EffectId>) (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      let currentEffects = components.Effects |> AList.force

      let filteredEffects =
        currentEffects |> IndexList.filter(fun e -> e.EffectId <> effectId)

      let updatedComponents = {
        components with
            Effects = AList.ofIndexList filteredEffects
      }

      Ok {
        entities = HashMap.ofList [ entityId, updatedComponents ]
        gameTime = ValueNone
      }

  /// Removes all effects from an entity.
  /// Direct operation: clears Effects alist.
  let clearAllEffects entityId (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      let updatedComponents = {
        components with
            Effects = AList.empty
      }

      Ok {
        entities = HashMap.ofList [ entityId, updatedComponents ]
        gameTime = ValueNone
      }

  // ============================================================================
  // TIME & SIMULATION
  // ============================================================================

  /// Advances game time and processes tick-based effects.
  /// Uses existing tick mechanism: Calls GameState.tick from Gameplay.fs.
  /// Returns adaptive StateChange (DoT/HoT damage, effect expiration).
  /// MonoGame evaluates and applies via GameState.applyTick.
  let advanceTime
    (deltaTicks: int64<Tick>)
    (state: GameState)
    : aval<StateChange> =

    GameState.tick state deltaTicks

  /// Clears all ability cooldowns (for testing/debug).
  /// Direct operation: clears AbilityCooldowns amap.
  let resetCooldowns entityId (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> Error EntityNotFound
    | ValueSome components ->
      let updatedComponents = {
        components with
            AbilityCooldowns = AMap.empty
      }

      Ok {
        entities = HashMap.ofList [ entityId, updatedComponents ]
        gameTime = ValueNone
      }

  // ============================================================================
  // QUERY OPERATIONS (NON-REACTIVE)
  // ============================================================================

  /// Returns all alive entities.
  /// Uses ASet.force on existing adaptive projection.
  let getAliveEntities(state: GameState) =

    let aliveSet = GameState.aAlive state.entities
    ASet.force aliveSet |> HashSet.toArray

  /// Returns abilities not on cooldown for an entity.
  /// Uses direct access to AbilityCooldowns and Abilities.
  let getReadyAbilities entityId (state: GameState) =

    let entitiesMap = state.entities |> AMap.force

    match entitiesMap |> HashMap.tryFindV entityId with
    | ValueNone -> IndexList.empty
    | ValueSome components ->
      let currentTime = state.gameTime |> AVal.force
      let cooldowns = components.AbilityCooldowns |> AMap.force
      let abilities = components.Abilities |> AList.force

      abilities
      |> IndexList.filter(fun abilityId ->
        match cooldowns |> HashMap.tryFindV abilityId with
        | ValueNone -> true // No cooldown = ready
        | ValueSome expiryTime -> currentTime >= expiryTime)

  /// Forces evaluation of adaptive stats and returns snapshot.
  let inline getDerivedStatsSnapshot entityId (state: GameState) =
    GameState.getDerivedStats state |> AMap.force |> HashMap.tryFindV entityId

  /// Force and apply an adaptive StateChange with time update. (applyTick)
  let inline forceAndApply(state: GameState) =
    AVal.force >> GameState.apply state
