namespace Pomo.Core

open Microsoft.Xna.Framework.Input
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain

module KeybindingSystem =
  open InputManager

  [<Struct>]
  type QuickSlot =
    | Q
    | W
    | E
    | R
    | A
    | S
    | D
    | F

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
    | UseItem of item: int<Inventory.ItemId>
    | Empty

  type KeybindingConfig = {
    Bindings: HashMap<struct (ActionSet * QuickSlot), SlotAction>
    ActiveSet: ActionSet
  }

  let createDefault() = {
    Bindings =
      HashMap.ofList [
        struct (Set1, Q), ActivateAbility 103<AbilityId>
        struct (Set1, W), Empty
        struct (Set1, E), Empty
        struct (Set1, R), Empty
        struct (Set2, Q), ActivateAbility 104<AbilityId>
        struct (Set2, W), ActivateAbility 103<AbilityId>
      ]
    ActiveSet = Set1
  }

  let setActiveSet (set: ActionSet) (config: KeybindingConfig) = {
    config with
        ActiveSet = set
  }

  let getSlotAction (slot: QuickSlot) (config: KeybindingConfig) =
    config.Bindings
    |> HashMap.tryFindV struct (config.ActiveSet, slot)
    |> ValueOption.defaultValue Empty

  let slotToKey =
    function
    | Q -> Keys.Q
    | W -> Keys.W
    | E -> Keys.E
    | R -> Keys.R
    | A -> Keys.A
    | S -> Keys.S
    | D -> Keys.D
    | F -> Keys.F

  let setToKey =
    function
    | Set1 -> Keys.D1
    | Set2 -> Keys.D2
    | Set3 -> Keys.D3
    | Set4 -> Keys.D4
    | Set5 -> Keys.D5

  let allSlots = [| Q; W; E; R; A; S; D; F |]
  let allSets = [| Set1; Set2; Set3; Set4; Set5 |]

  type KeybindingResult =
    | NoAction
    | EnterAbilityTargeting of int<AbilityId> * InputManager.TargetingMode
    | ExecuteSelfAbility of int<AbilityId>
    | UseItemAction of int<Inventory.ItemId>

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
        | _ -> ValueNone
      | _ -> ValueNone
