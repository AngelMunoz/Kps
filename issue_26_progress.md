# GH Issue 26: Implement Inventory System with Usable, Wearable, and Non-Usable Items

## Original Plan

### I. Core Concepts & Definitions (Pomo.Lib.Domain.fs)

1. New Measure Type: InventoryItemInstanceId
2. Item Properties: ItemDefinition (Id, Name, Description, Weight, Rarity, Kind)
3. Item Categories (ItemKind DU):
   - Usable of UsableItemDefinition (UsableItemDefinition includes InitialUsageCount and AbilityId)
   - Wearable (marker, links to existing Equipment definitions)
   - NonUsable
4. Runtime Inventory Item: InventoryItem (InstanceId, ItemId, CurrentUsageCount voption)
5. Equipment Integration: Modify existing Equipment type to include Weight: float32.

### II. Entity Component Updates (Pomo.Lib.Domain.fs -> Components module)

1. EntityComponents: Add Inventory: InventoryItem list and EquippedItems: HashMap<Slot, Guid<InventoryItemInstanceId>>.

### III. Game Commands (Pomo.Lib.Domain.fs -> Rules module)

1. Command DU: Add UseItem, EquipItem, and UnequipItem commands.

### IV. Item Definitions (Pomo.Lib.Content.fs)

1. ItemStore (New Module): definitions: Map<int<ItemId>, ItemDefinition> with example items.
2. EquipmentStore: Update existing Equipment definitions with Weight.

### V. Game Logic Considerations (Future Implementation):

- Inventory management functions (add/remove, weight checks).
- Ability system integration for UseItem.
- Equipping/unequipping logic.

## Current Design Analysis

1. Duplicated Item Representations: We have two types that seem to define item characteristics: ItemDefinition and Equipment. An ItemDefinition has a Kind field, which can be Wearable, but the Equipment type also defines properties for wearable items. This is redundant and can lead to confusion.
2. Component State: The EntityComponents record has both Equipment: HashMap<Slot, Equipment> and EquippedItems: HashMap<Slot, Guid>. The former stores full equipment data, while the latter just stores references (IDs) to equipped items. In a DOP approach, storing only the IDs (EquippedItems) is preferable to keep the component lean. The full item data can be looked up from a central store when needed.

## Proposed Design Refinement

To address this, I propose we unify our item definitions. The ItemDefinition should be the single source of truth for any item.

Here is the proposed change to the types in Pomo.Lib/Domain.fs:

1. Consolidate Equipment into ItemKind: We can move the properties from the Equipment type directly into the Wearable case of the ItemKind discriminated union.

   ```fsharp
   // Proposed new types in the Inventory module
   type EquipmentProperties = {
     Slot: Slot
     StatBonuses: ItemStatBonus[]
     ElementalAttributes: HashMap<Attributes.Element, float>
     ElementalResistances: HashMap<Attributes.Element, float>
   }

   type ItemKind =
     | Usable of UsableItemDefinition
     | Wearable of EquipmentProperties
     | NonUsable
   ```

2. Remove Redundant Equipment Type: The old Equipment record would be completely removed, as its purpose is now served by ItemDefinition with a Wearable kind.
3. Simplify EntityComponents: We would remove the Equipment: HashMap<Slot, Equipment> field. The EquippedItems: HashMap<Slot, Guid> field correctly represents the state of what is equipped, pointing to an item instance in the entity's Inventory list.

## Advantages of this Refined Design

- Single Source of Truth: There is only one way to define an item: ItemDefinition.
- Clearer Intent: The ItemKind DU makes it explicit what an item can do.
- DOP-Friendly: EntityComponents remains lean, storing IDs and state, not bulky definitional data. We can look up item details from a central ItemStore using the ItemId.

## Already Implemented

- Consolidated item definitions and inventory types in `Pomo.Lib.Domain`:
  - `InventoryItemInstanceId` measure, `EquipmentProperties`, `ItemKind` (Usable | Wearable | NonUsable), `ItemDefinition`, `UsableItemDefinition`, and `InventoryItem` are present.
  - `ItemDefinition` includes `Weight` and wearable properties are represented via `EquipmentProperties` in the `Wearable` case.
- Components updated:
  - `Components.EntityComponents` stores `Inventory: InventoryItem IndexList` and `EquippedItems: HashMap<Slot, Guid<InventoryItemInstanceId>>` (IDs only).
- Rules updated:
  - `Rules.Command` includes `UseItem`, `EquipItem`, and `UnequipItem`.
- Content updated:
  - `Pomo.Lib.Content` contains an `ItemStore` with sample `ItemDefinition` entries.

## Remaining Tasks

The structural/type work is complete (see "Already Implemented"). Remaining work focuses on runtime logic, tests, and docs:

1. Inventory runtime helpers (not started)

   - Pure functions to create item instances, add/remove items from an entity's `Inventory`, and compute total carried weight.
   - Stack/usage semantics for usable items (manage `InitialUsageCount` and `CurrentUsageCount`).

2. Equip / Unequip logic (not started)

   - Handlers that validate slot compatibility, handle swaps or failures, update `EquippedItems`, and emit appropriate `StateChange` entries.

3. UseItem integration (not started)

   - Map usable items to abilities/effects (via `UsableItemDefinition.AbilityId`), consume usages or remove items, and trigger ability/effect resolution (respect cooldowns/resources).

4. Tests (not started)

   - Unit tests covering add/remove behavior, equip/unequip, using consumables, weight calculations, and persistence in `EntityComponents`.

5. Docs & issue update (in-progress)
   - Keep `issue_26_progress.md` and any README sections updated with implementation status and design notes.
