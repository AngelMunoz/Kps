namespace Pomo.Core

open System
open FSharp.UMX
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Scenario
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.State
open Pomo.Lib.InventoryManagement
open Pomo.Lib.Domain.Inventory
open Pomo.Lib.Content
open Pomo.Lib.Operations
open Pomo.Lib.Pathfinding
open Pomo.Lib.Gameplay
open Pomo.Lib.Rules
open FSharp.Data.Adaptive
open Pomo.Core.KeybindingSystem

module ScenarioLoader =

  let loadScenario(state: GameState, scenario: Scenario) =
    transact(fun _ ->

      let scenarioState = ScenarioState.create scenario
      state.scenarios[scenario.Id] <- scenarioState
      state.activeScenarioId.Value <- scenario.Id

      AudioSystem.updateScenarioMusic state.services.audioStore scenario.Id)

    Grid.createGrid scenario 32.0f 16.0f

module CharacterBuilder =

  let createCharacter
    (state: GameState)
    (profession: Profession)
    (faction: Faction)
    (position: Position)
    : Guid<EntityId> =
    let kit = CharacterKitStore.definitions[profession]

    let change =
      kit
      |> GameState.createEntity
        (fun stats -> {
          stats with
              Factions = HashSet.ofList [ faction ]
              Position = position
        })
        state.services

    let entityId = change.additions |> HashMap.toKeySeq |> Seq.head
    GameState.apply state change
    entityId

  let createAICharacter
    (state: GameState)
    (archetypeId: int<AiArchetypeId>)
    (position: Position)
    : Guid<EntityId> =
    let archetype = state.services.aiArchetypeStore.find archetypeId
    let kit = archetype.characterKit

    let entityId = Guid.NewGuid() |> UMX.tag

    let components =
      kit
      |> GameState.createEntity
        (fun stats -> {
          stats with
              Position = position
              Factions = HashSet.ofList [ Enemy; AIControlled ]
        })
        state.services

    let spawnData: Rules.SpawnEntityData = {
      components = components.additions |> HashMap.toValueArray |> Array.head
      archetypeId = ValueSome archetypeId
    }

    let cmd = Rules.AddEntitiesWithAI(HashMap.single entityId spawnData)
    let change = CommandHandler.evaluate state cmd |> AVal.force
    GameState.apply state change
    entityId

module TestScenarioBuilder =

  type TestScenarioData = {
    PlayerId: Guid<EntityId>
    Enemies: Guid<EntityId>[]
    NavigationGrid: PathfindingGrid
    KeyBindings: (struct (ActionSet * GameAction) * SlotAction)[]
  }

  let createDefaultScenario(state: GameState) : TestScenarioData =
    let struct (town, wilderness, dungeon) =
      ScenarioDefinitions.createConnectedScenarios()

    let grid = ScenarioLoader.loadScenario(state, wilderness)

    let playerId =
      CharacterBuilder.createCharacter
        state
        { Family = Magic; Stage = Second }
        Player
        { X = 100f; Y = 400f }

    let enemies = [|
      CharacterBuilder.createAICharacter state 1<AiArchetypeId> {
        X = 200f
        Y = 600f
      }
      CharacterBuilder.createAICharacter state 2<AiArchetypeId> {
        X = 300f
        Y = 400f
      }
      CharacterBuilder.createAICharacter state 3<AiArchetypeId> {
        X = 500f
        Y = 700f
      }
    |]

    let potionGuid = Guid.NewGuid() |> UMX.tag

    transact(fun _ ->
      let scenario = Scenario.ActiveScenario state |> AVal.force
      let playerComponents = scenario.entities.[playerId]

      let helmDef = state.services.itemStore.find 1<ItemId>
      let potionDef = state.services.itemStore.find 2<ItemId>
      let rockDef = state.services.itemStore.find 3<ItemId>
      let amuletDef = state.services.itemStore.find 4<ItemId>
      let helmInstance = Inventory.createItemInstance ValueNone helmDef

      let potionInstance1 =
        Inventory.createItemInstance (ValueSome potionGuid) {
          potionDef with
              Kind =
                Usable {
                  InitialUsageCount = 10
                  AbilityId = 200<AbilityId>
                }
        }

      let rockInstance = Inventory.createItemInstance ValueNone rockDef
      let amuletInstance = Inventory.createItemInstance ValueNone amuletDef

      let updatedInventory =
        playerComponents.Inventory
        |> HashMap.add helmInstance.InstanceId helmInstance
        |> HashMap.add potionInstance1.InstanceId potionInstance1
        |> HashMap.add rockInstance.InstanceId rockInstance
        |> HashMap.add amuletInstance.InstanceId amuletInstance

      let playerWithItems = {
        playerComponents with
            Inventory = updatedInventory
      }

      let equipAction: Rules.EquipItemAction = {
        actor = playerId
        itemInstanceId = helmInstance.InstanceId
        slot = Head
      }

      let equipResult =
        Equip.equipItem state.services.itemStore equipAction playerWithItems

      let finalPlayerComponents =
        match equipResult with
        | Ok equippedComponents ->
          let equipAmuletAction: Rules.EquipItemAction = {
            actor = playerId
            itemInstanceId = amuletInstance.InstanceId
            slot = Accessory
          }

          let equipAmuletResult =
            Equip.equipItem
              state.services.itemStore
              equipAmuletAction
              equippedComponents

          match equipAmuletResult with
          | Ok finalComponents -> finalComponents
          | Error e ->
            System.Diagnostics.Debug.WriteLine(
              sprintf "Failed to equip amulet: %A" e
            )

            equippedComponents
        | Error e ->
          System.Diagnostics.Debug.WriteLine(
            sprintf "Failed to equip item: %A" e
          )

          playerWithItems

      scenario.entities.[playerId] <- finalPlayerComponents)

    let keyBindings = [|
      struct (Set1, GameAction.UseQuickSlot1), ActivateAbility 103<AbilityId>
      struct (Set1, GameAction.UseQuickSlot2), UseItem potionGuid
      struct (Set1, GameAction.UseQuickSlot3), Empty
      struct (Set1, GameAction.UseQuickSlot4), Empty
      struct (Set2, GameAction.UseQuickSlot1), ActivateAbility 104<AbilityId>
      struct (Set2, GameAction.UseQuickSlot2), ActivateAbility 103<AbilityId>
    |]

    {
      PlayerId = playerId
      Enemies = enemies
      NavigationGrid = grid
      KeyBindings = keyBindings
    }
