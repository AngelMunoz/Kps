module Pomo.Lib.Tests.InventoryManagementTests

open System
open Xunit
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Inventory
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Rules
open Pomo.Lib.Domain.Services
open Pomo.Lib.InventoryManagement
open Pomo.Lib.Tests

// Test Data
module TestItems =
  let usablePotionId = 1<ItemId>
  let wearableHelmetId = 2<ItemId>
  let junkItemId = 3<ItemId>
  let wearableChestId = 4<ItemId>

  let usablePotionDef = {
    Id = usablePotionId
    Name = "Health Potion"
    Description = "Restores health."
    Weight = 0.5f
    Rarity = Common
    Kind =
      Usable {
        InitialUsageCount = 3
        AbilityId = 1<AbilityId>
      }
  }

  let wearableHelmetDef = {
    Id = wearableHelmetId
    Name = "Iron Helmet"
    Description = "A sturdy helmet."
    Weight = 5.0f
    Rarity = Common
    Kind =
      Wearable {
        Slot = Head
        StatBonuses = [||]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
  }

  let junkItemDef = {
    Id = junkItemId
    Name = "Useless Rock"
    Description = "It's just a rock."
    Weight = 1.0f
    Rarity = Common
    Kind = NonUsable
  }

  let wearableChestDef = {
    Id = wearableChestId
    Name = "Iron Chestplate"
    Description = "A sturdy chestplate."
    Weight = 10.0f
    Rarity = Common
    Kind =
      Wearable {
        Slot = Head // Intentionally wrong for a test
        StatBonuses = [||]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
  }

  let itemStore =
    let definitions =
      HashMap.ofList [
        usablePotionId, usablePotionDef
        wearableHelmetId, wearableHelmetDef
        junkItemId, junkItemDef
        wearableChestId, wearableChestDef
      ]

    { new IItemStore with
        member _.tryFind itemId = definitions |> HashMap.tryFindV itemId
        member _.find itemId = definitions |> HashMap.find itemId
    }

[<Fact>]
let ``createItemInstance correctly populates instance from definition``() =
  let itemInstance = Inventory.createItemInstance TestItems.usablePotionDef

  Assert.Equal(TestItems.usablePotionDef.Id, itemInstance.ItemId)
  Assert.Equal(TestItems.usablePotionDef.Name, itemInstance.Name)
  Assert.Equal(TestItems.usablePotionDef.Weight, itemInstance.Weight)
  Assert.Equal(ValueSome 3, itemInstance.CurrentUsageCount)

[<Fact>]
let ``saveInventoryItem can add an item``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.usablePotionDef

  let updatedComponents =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  Assert.True(
    HashMap.containsKey itemInstance.InstanceId updatedComponents.Inventory
  )

[<Fact>]
let ``saveInventoryItem can remove an item``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.usablePotionDef

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let updatedComponents =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      ValueNone
      componentsWithItem

  Assert.False(
    HashMap.containsKey itemInstance.InstanceId updatedComponents.Inventory
  )

[<Fact>]
let ``useItem successfully consumes a charge``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.usablePotionDef

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = itemInstance.InstanceId
  }

  let result = Inventory.useItem TestItems.itemStore action componentsWithItem

  match result with
  | Ok output ->
    let updatedItem =
      output.UpdatedComponents.Inventory[itemInstance.InstanceId]

    Assert.Equal(ValueSome 2, updatedItem.CurrentUsageCount)
  | Error e -> Assert.Fail(sprintf "Test failed with error: %A" e)

[<Fact>]
let ``useItem consumes the last charge and removes the item``() =
  let components = TestHelpers.createTestEntity()

  let itemInstance = {
    (Inventory.createItemInstance TestItems.usablePotionDef) with
        CurrentUsageCount = ValueSome 1
  }

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = itemInstance.InstanceId
  }

  let result = Inventory.useItem TestItems.itemStore action componentsWithItem

  match result with
  | Ok output ->
    Assert.False(
      HashMap.containsKey
        itemInstance.InstanceId
        output.UpdatedComponents.Inventory
    )
  | Error e -> Assert.Fail(sprintf "Test failed with error: %A" e)

[<Fact>]
let ``useItem fails for a non-usable item``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.junkItemDef

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = itemInstance.InstanceId
  }

  let result = Inventory.useItem TestItems.itemStore action componentsWithItem

  Assert.Equal(Error Inventory.ItemNotUsable, result)

[<Fact>]
let ``useItem fails for item not in inventory``() =
  let components = TestHelpers.createTestEntity()

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = %Guid.NewGuid()
  }

  let result = Inventory.useItem TestItems.itemStore action components

  Assert.Equal(Error Inventory.ItemNotFoundInInventory, result)

[<Fact>]
let ``equipItem successfully equips an item``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.wearableHelmetDef

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = itemInstance.InstanceId
    slot = Head
  }

  let result = Equip.equipItem TestItems.itemStore action componentsWithItem

  match result with
  | Ok output ->
    Assert.Equal(
      itemInstance.InstanceId,
      output.EquippedItems |> HashMap.find Head
    )
  | Error e -> Assert.Fail(sprintf "Test failed with error: %A" e)

[<Fact>]
let ``equipItem fails for wrong slot``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.wearableHelmetDef

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = itemInstance.InstanceId
    slot = Chest
  }

  let result = Equip.equipItem TestItems.itemStore action componentsWithItem

  Assert.Equal(Error Equip.WrongSlot, result)

[<Fact>]
let ``equipItem fails for non-wearable item``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.usablePotionDef

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = itemInstance.InstanceId
    slot = Head
  }

  let result = Equip.equipItem TestItems.itemStore action componentsWithItem

  Assert.Equal(Error Equip.ItemNotWearable, result)

[<Fact>]
let ``equipItem fails for item not in inventory``() =
  let components = TestHelpers.createTestEntity()

  let action = {
    actor = %Guid.NewGuid()
    itemInstanceId = %Guid.NewGuid()
    slot = Head
  }

  let result = Equip.equipItem TestItems.itemStore action components

  Assert.Equal(Error Equip.ItemNotFoundInInventory, result)

[<Fact>]
let ``equipItem replaces an already equipped item``() =
  let components = TestHelpers.createTestEntity()
  let helmetInstance = Inventory.createItemInstance TestItems.wearableHelmetDef

  let chestInstance = {
    Inventory.createItemInstance TestItems.wearableChestDef with
        Name = "New Helmet"
  }

  let componentsWithItems =
    components
    |> Inventory.saveInventoryItem
      helmetInstance.InstanceId
      (ValueSome helmetInstance)
    |> Inventory.saveInventoryItem
      chestInstance.InstanceId
      (ValueSome chestInstance)

  let equipHelmetAction = {
    actor = %Guid.NewGuid()
    itemInstanceId = helmetInstance.InstanceId
    slot = Head
  }

  let equippedComponents =
    Equip.equipItem TestItems.itemStore equipHelmetAction componentsWithItems
    |> Result.defaultWith(fun e ->
      Assert.Fail $"Initial equip failed with error: {e}"
      componentsWithItems)

  let equipChestAction = {
    actor = %Guid.NewGuid()
    itemInstanceId = chestInstance.InstanceId
    slot = Head
  }

  let reEquippedComponents =
    Equip.equipItem TestItems.itemStore equipChestAction equippedComponents
    |> Result.defaultWith(fun e ->
      Assert.Fail $"Re-equip failed with error: {e}"
      equippedComponents)

  Assert.Equal(
    chestInstance.InstanceId,
    reEquippedComponents.EquippedItems |> HashMap.find Head
  )

[<Fact>]
let ``unequipItem successfully removes an item from a slot``() =
  let components = TestHelpers.createTestEntity()
  let itemInstance = Inventory.createItemInstance TestItems.wearableHelmetDef

  let componentsWithItem =
    Inventory.saveInventoryItem
      itemInstance.InstanceId
      (ValueSome itemInstance)
      components

  let equipAction = {
    actor = %Guid.NewGuid()
    itemInstanceId = itemInstance.InstanceId
    slot = Head
  }

  let equippedComponents =
    Equip.equipItem TestItems.itemStore equipAction componentsWithItem
    |> Result.defaultWith(fun e ->
      Assert.Fail $"Setup failed with error: {e}"
      componentsWithItem)

  let unequipAction = { actor = %Guid.NewGuid(); slot = Head }

  let unequippedComponents = Equip.unequipItem unequipAction equippedComponents

  Assert.False(HashMap.containsKey Head unequippedComponents.EquippedItems)

[<Fact>]
let ``calculateCarriedWeight calculates weight correctly``() =
  let components = TestHelpers.createTestEntity()
  let potionInstance = Inventory.createItemInstance TestItems.usablePotionDef
  let helmetInstance = Inventory.createItemInstance TestItems.wearableHelmetDef

  let componentsWithItems =
    components
    |> Inventory.saveInventoryItem
      potionInstance.InstanceId
      (ValueSome potionInstance)
    |> Inventory.saveInventoryItem
      helmetInstance.InstanceId
      (ValueSome helmetInstance)

  let inventoryList =
    componentsWithItems.Inventory |> HashMap.toValueArray |> AList.ofArray

  let weight = Inventory.calculateCarriedWeight inventoryList |> AVal.force

  Assert.Equal(5.5f, weight)
