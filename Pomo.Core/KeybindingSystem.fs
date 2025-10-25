namespace Pomo.Core

open System.Diagnostics
open Microsoft.Xna.Framework.Input
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Rules
open Pomo.Lib.Operations
open Pomo.Lib.Scenario
open Pomo.Lib.Gameplay

module KeybindingSystem =
  open Pomo.Lib.Domain.State

  // --- EXISTING TYPES ---
  [<Struct>]
  type ActionSet =
    | Set1
    | Set2
    | Set3
    | Set4
    | Set5

  [<Struct>]
  type SlotAction =
    | ActivateAbility of ability: int<AbilityId>
    | UseItem of item: Guid<InventoryItemInstanceId>
    | Empty

  type KeybindingConfig = {
    Bindings: HashMap<struct (ActionSet * GameAction), SlotAction>
    ActiveSet: ActionSet
  }

  type KeybindingResult =
    | NoAction
    | EnterAbilityTargeting of int<AbilityId> * TargetingMode
    | ExecuteSelfAbility of int<AbilityId>
    | UseItemAction of Guid<InventoryItemInstanceId>

  // --- FUNCTIONS ---
  let createDefault(bindings) = {
    Bindings = HashMap.ofSeq bindings
    ActiveSet = Set1
  }

  let setActiveSet (set: ActionSet) (config: KeybindingConfig) = {
    config with
        ActiveSet = set
  }

  let getSlotAction (action: GameAction) (config: KeybindingConfig) =
    config.Bindings
    |> HashMap.tryFindV struct (config.ActiveSet, action)
    |> ValueOption.defaultValue Empty

  let setToKey =
    function
    | Set1 -> Keys.D1
    | Set2 -> Keys.D2
    | Set3 -> Keys.D3
    | Set4 -> Keys.D4
    | Set5 -> Keys.D5

  let allSets = [| Set1; Set2; Set3; Set4; Set5 |]

  let getAbilityTargetingMode
    (abilityStore: Services.IAbilityStore)
    (abilityId: int<AbilityId>)
    =
    match abilityStore.tryFind abilityId with
    | ValueNone -> ValueNone
    | ValueSome abilityKind ->
      match abilityKind with
      | Abilities.Active def ->
        match def.Targeting with
        | Abilities.SingleEnemy -> ValueSome EntityTargeting
        | Abilities.SingleAlly -> ValueSome EntityTargeting
        | Abilities.GroundTarget radius -> ValueSome(GroundTargeting radius)
        | Abilities.Self -> ValueNone
        | Abilities.MultiTarget _ -> ValueNone
      | Abilities.Passive _ -> ValueNone

  let processSlotAction
    (gameAction: GameAction)
    (config: KeybindingConfig)
    (state: GameState)
    (playerId: Guid<EntityId>)
    =
    let action = getSlotAction gameAction config
    let mutable result = NoAction

    match action with
    | ActivateAbility abilityId ->
      match getAbilityTargetingMode state.services.abilityStore abilityId with
      | ValueSome targetingMode ->
        result <- EnterAbilityTargeting(abilityId, targetingMode)

        Debug.WriteLine
          $"[Keybinding] Slot {gameAction} entering targeting for ability {abilityId}"
      | ValueNone ->
        result <- ExecuteSelfAbility abilityId

        Debug.WriteLine
          $"[Keybinding] Slot {gameAction} executing self ability {abilityId}"
    | UseItem instanceId ->
      Debug.WriteLine $"[Keybinding] Slot {gameAction} using item {instanceId}"
      result <- UseItemAction instanceId
    | Empty -> ()

    // Automatically handle the execution of self-cast abilities
    match result with
    | ExecuteSelfAbility abilityId ->
      let scenario = Scenario.ActiveScenario state |> AVal.force

      let stateChange =
        GameState.activateAbility playerId abilityId [| playerId |] state
        |> AVal.force

      AudioSystem.processAudioChanges
        state.services.audioStore
        scenario
        stateChange.audioChanges

      GameState.apply state stateChange
      NoAction // Reset result after execution
    | _ -> result
