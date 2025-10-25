namespace Pomo.Lib.InventoryManagement

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Inventory
open System
open FSharp.UMX
open FSharp.Data.Adaptive

module private Utils =
  let inline findItemInInventory
    itemInstanceId
    (comps: Components.EntityComponents)
    =
    HashMap.tryFindV itemInstanceId comps.Inventory

  let inline findItemDefinition
    (inventoryItem: InventoryItem)
    (itemStore: Services.IItemStore)
    =
    itemStore.tryFind inventoryItem.ItemId

module Inventory =
  /// Creates a new inventory item instance from an item definition.
  let createItemInstance guid (itemDef: ItemDefinition) : InventoryItem =
    let instanceId = defaultValueArg guid (%Guid.NewGuid())

    let usageCount =
      match itemDef.Kind with
      | Usable usableDef -> ValueSome usableDef.InitialUsageCount
      | Wearable _
      | NonUsable -> ValueNone

    {
      InstanceId = instanceId
      ItemId = itemDef.Id
      Name = itemDef.Name
      Weight = itemDef.Weight
      CurrentUsageCount = usageCount
    }

  let inline saveInventoryItem
    (itemInstanceId: Guid<InventoryItemInstanceId>)
    (itemOpt: InventoryItem voption)
    (components: Components.EntityComponents)
    : Components.EntityComponents =
    {
      components with
          Inventory =
            HashMap.alterV
              itemInstanceId
              (fun _ -> itemOpt)
              components.Inventory
    }


  let getInventoryList
    (entity: Guid<EntityId>)
    (entities: amap<Guid<EntityId>, Components.EntityComponents>)
    =
    adaptive {
      let! componentsOpt = entities |> AMap.tryFind entity

      match componentsOpt with
      | None -> return AList.empty
      | Some components ->
        return components.Inventory |> HashMap.toValueArray |> AList.ofArray
    }

  let inline calculateCarriedWeight(inventory: InventoryItem alist) =
    inventory |> AList.sumBy(fun item -> item.Weight)

  type UseItemError =
    | ItemNotFoundInInventory
    | ItemDefinitionNotFound
    | ItemNotUsable
    | ItemHasNoUsesLeft

  type UseItemOutput = {
    UpdatedComponents: Components.EntityComponents
    AbilityId: int<AbilityId>
  }

  let useItem
    (itemStore: Services.IItemStore)
    (action: Rules.UseItemAction)
    (components: EntityComponents)
    : Result<UseItemOutput, UseItemError> =
    let consumeUsage (inventoryItem: InventoryItem) (itemDef: ItemDefinition) =
      match itemDef.Kind with
      | Usable usableDef ->
        match inventoryItem.CurrentUsageCount with
        | ValueSome count ->
          if count > 0 then
            let newItemOpt =
              if count > 1 then
                ValueSome {
                  inventoryItem with
                      CurrentUsageCount = ValueSome(count - 1)
                }
              else
                ValueNone // Last use, so remove item.

            let updatedComponents =
              saveInventoryItem inventoryItem.InstanceId newItemOpt components

            Ok {
              UpdatedComponents = updatedComponents
              AbilityId = usableDef.AbilityId
            }
          else
            Error ItemHasNoUsesLeft // count is 0 or less
        | ValueNone -> Error ItemHasNoUsesLeft
      | Wearable _ -> Error ItemNotUsable
      | NonUsable -> Error ItemNotUsable

    Utils.findItemInInventory action.itemInstanceId components
    |> ValueOption.toResult ItemNotFoundInInventory
    |> Result.bind(fun inventoryItem ->
      Utils.findItemDefinition inventoryItem itemStore
      |> ValueOption.toResult ItemDefinitionNotFound
      |> Result.bind(fun itemDef -> consumeUsage inventoryItem itemDef))


module Equip =


  type EquipError =
    | ItemNotFoundInInventory
    | ItemDefinitionNotFound
    | ItemNotWearable
    | WrongSlot

  let inline equipItem
    (itemStore: Services.IItemStore)
    (action: Rules.EquipItemAction)
    (components: EntityComponents)
    : Result<EntityComponents, EquipError> =

    let checkAndEquipItem(itemDef: ItemDefinition) =
      match itemDef.Kind with
      | Wearable properties ->
        if properties.Slot = action.slot then
          Ok {
            components with
                EquippedItems =
                  components.EquippedItems
                  |> HashMap.add action.slot action.itemInstanceId
          }
        else
          Error WrongSlot
      | Usable _ -> Error ItemNotWearable
      | NonUsable -> Error ItemNotWearable

    Utils.findItemInInventory action.itemInstanceId components
    |> ValueOption.toResult ItemNotFoundInInventory
    |> Result.bind(fun inventoryItem ->
      Utils.findItemDefinition inventoryItem itemStore
      |> ValueOption.toResult ItemDefinitionNotFound
      |> Result.bind checkAndEquipItem)


  let inline unequipItem
    (action: Rules.UnequipItemAction)
    (components: EntityComponents)
    : EntityComponents =
    {
      components with
          EquippedItems = HashMap.remove action.slot components.EquippedItems
    }
