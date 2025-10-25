namespace Pomo.Core

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.Inventory
open Pomo.Lib.Operations

module UISystem =
  open Pomo.Lib.Gameplay.Scenario

  [<Struct>]
  type UIPanel =
    | CharacterSheet
    | EquipmentView
    | AbilityList

  [<Struct>]
  type UIState = {
    ActivePanels: UIPanel HashSet
    SelectedEntity: Guid<EntityId> voption
  }

  let createUIState() = {
    ActivePanels = HashSet.empty
    SelectedEntity = ValueNone
  }

  let togglePanel panel uiState =
    let panels =
      if uiState.ActivePanels |> HashSet.contains panel then
        uiState.ActivePanels |> HashSet.remove panel
      else
        uiState.ActivePanels |> HashSet.add panel

    { uiState with ActivePanels = panels }

  let setSelectedEntity entityId uiState = {
    uiState with
        SelectedEntity = ValueSome entityId
  }

  let clearSelectedEntity uiState = {
    uiState with
        SelectedEntity = ValueNone
  }

  let private drawPanel
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (font: SpriteFont)
    (x: int)
    (y: int)
    (width: int)
    (height: int)
    (title: string)
    =

    let panelColor = Color(40, 40, 40, 220)
    let borderColor = Color(100, 100, 100, 255)
    let titleColor = Color.White

    sb.Draw(pixel, Rectangle(x, y, width, height), panelColor)
    sb.Draw(pixel, Rectangle(x, y, width, 2), borderColor)
    sb.Draw(pixel, Rectangle(x, y + height - 2, width, 2), borderColor)
    sb.Draw(pixel, Rectangle(x, y, 2, height), borderColor)
    sb.Draw(pixel, Rectangle(x + width - 2, y, 2, height), borderColor)

    let titleSize = font.MeasureString(title)
    let titleX = float32 x + (float32 width - titleSize.X) * 0.5f
    let titleY = float32 y + 8f
    sb.DrawString(font, title, Vector2(titleX, titleY), titleColor)

  let private drawStatLine
    (sb: SpriteBatch)
    (font: SpriteFont)
    (x: float32)
    (y: float32)
    (label: string)
    (value: string)
    =

    let labelColor = Color(200, 200, 200)
    let valueColor = Color.White

    sb.DrawString(font, label, Vector2(x, y), labelColor)
    let labelSize = font.MeasureString(label)
    sb.DrawString(font, value, Vector2(x + labelSize.X + 10f, y), valueColor)

  let private drawCharacterSheet
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (font: SpriteFont)
    (entityId: Guid<EntityId>)
    (entities: HashMap<Guid<EntityId>, EntityComponents>)
    (derivedStats: HashMap<Guid<EntityId>, DerivedStats>)
    (x: int)
    (y: int)
    =

    let width = 300
    let height = 600

    drawPanel sb pixel font x y width height "Character Sheet"

    match entities |> HashMap.tryFindV entityId with
    | ValueSome entity ->
      let stats = derivedStats |> HashMap.tryFindV entityId
      let startY = float32 y + 40f
      let lineHeight = 20f
      let leftX = float32 x + 10f

      drawStatLine
        sb
        font
        leftX
        (startY + 0f * lineHeight)
        "Profession:"
        $"{entity.Identity.Family}/{entity.Identity.Stage}"

      drawStatLine
        sb
        font
        leftX
        (startY + 1f * lineHeight)
        "HP:"
        $"{entity.Resources.HP}"

      drawStatLine
        sb
        font
        leftX
        (startY + 2f * lineHeight)
        "MP:"
        $"{entity.Resources.MP}"

      drawStatLine
        sb
        font
        leftX
        (startY + 3f * lineHeight)
        "Status:"
        $"{entity.Resources.Status}"

      drawStatLine sb font leftX (startY + 5f * lineHeight) "Base Stats:" ""

      drawStatLine
        sb
        font
        leftX
        (startY + 6f * lineHeight)
        "  Power:"
        $"{entity.BaseStats.Power}"

      drawStatLine
        sb
        font
        leftX
        (startY + 7f * lineHeight)
        "  Magic:"
        $"{entity.BaseStats.Magic}"

      drawStatLine
        sb
        font
        leftX
        (startY + 8f * lineHeight)
        "  Sense:"
        $"{entity.BaseStats.Sense}"

      drawStatLine
        sb
        font
        leftX
        (startY + 9f * lineHeight)
        "  Charm:"
        $"{entity.BaseStats.Charm}"

      match stats with
      | ValueSome derivedStats ->
        drawStatLine
          sb
          font
          leftX
          (startY + 11f * lineHeight)
          "Derived Stats:"
          ""

        drawStatLine sb font leftX (startY + 12f * lineHeight) "Power:" ""

        drawStatLine
          sb
          font
          leftX
          (startY + 13f * lineHeight)
          "  AP:"
          $"{derivedStats.AP}"

        drawStatLine
          sb
          font
          leftX
          (startY + 14f * lineHeight)
          "  AC:"
          $"{derivedStats.AC}"

        drawStatLine
          sb
          font
          leftX
          (startY + 15f * lineHeight)
          "  DX:"
          $"{derivedStats.DX}"

        drawStatLine sb font leftX (startY + 16f * lineHeight) "Magic:" ""

        drawStatLine
          sb
          font
          leftX
          (startY + 17f * lineHeight)
          "  MP:"
          $"{derivedStats.MP}"

        drawStatLine
          sb
          font
          leftX
          (startY + 18f * lineHeight)
          "  MA:"
          $"{derivedStats.MA}"

        drawStatLine
          sb
          font
          leftX
          (startY + 19f * lineHeight)
          "  MD:"
          $"{derivedStats.MD}"

        drawStatLine sb font leftX (startY + 20f * lineHeight) "Sense:" ""

        drawStatLine
          sb
          font
          leftX
          (startY + 21f * lineHeight)
          "  WT:"
          $"{derivedStats.WT}"

        drawStatLine
          sb
          font
          leftX
          (startY + 22f * lineHeight)
          "  DA:"
          $"{derivedStats.DA}"

        drawStatLine
          sb
          font
          leftX
          (startY + 23f * lineHeight)
          "  LK:"
          $"{derivedStats.LK}"

        drawStatLine sb font leftX (startY + 24f * lineHeight) "Charm:" ""

        drawStatLine
          sb
          font
          leftX
          (startY + 25f * lineHeight)
          "  HP:"
          $"{derivedStats.HP}"

        drawStatLine
          sb
          font
          leftX
          (startY + 26f * lineHeight)
          "  DP:"
          $"{derivedStats.DP}"

        drawStatLine
          sb
          font
          leftX
          (startY + 27f * lineHeight)
          "  HV:"
          $"{derivedStats.HV}"

        let hasElementalData =
          not(derivedStats.ElementAttributes |> HashMap.isEmpty)
          || not(derivedStats.ElementResistances |> HashMap.isEmpty)

        if hasElementalData then
          drawStatLine sb font leftX (startY + 29f * lineHeight) "Elemental:" ""

          let mutable lineOffset = 24f

          if not(derivedStats.ElementAttributes |> HashMap.isEmpty) then
            drawStatLine
              sb
              font
              leftX
              (startY + lineOffset * lineHeight)
              "  Attributes:"
              ""

            lineOffset <- lineOffset + 1f

            for element, value in derivedStats.ElementAttributes do
              let pct = value * 100.0

              drawStatLine
                sb
                font
                (leftX + 20f)
                (startY + lineOffset * lineHeight)
                $"    {element}:"
                $"{pct:F0}%%"

              lineOffset <- lineOffset + 1f

          if not(derivedStats.ElementResistances |> HashMap.isEmpty) then
            drawStatLine
              sb
              font
              leftX
              (startY + lineOffset * lineHeight)
              "  Resistances:"
              ""

            lineOffset <- lineOffset + 1f

            for element, value in derivedStats.ElementResistances do
              let pct = value * 100.0

              drawStatLine
                sb
                font
                (leftX + 20f)
                (startY + lineOffset * lineHeight)
                $"    {element}:"
                $"{pct:F0}%%"

              lineOffset <- lineOffset + 1f
      | ValueNone -> ()

    | ValueNone ->
      drawStatLine
        sb
        font
        (float32 x + 10f)
        (float32 y + 40f)
        "Entity not found"
        ""

  let private drawEquipmentView
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (font: SpriteFont)
    (entityId: Guid<EntityId>)
    (entityItems: HashMap<Guid<EntityId>, HashMap<Slot, InventoryItem>>)
    (x: int)
    (y: int)
    =

    let width = 280
    let height = 350

    drawPanel sb pixel font x y width height "Equipment"

    match entityItems |> HashMap.tryFindV entityId with
    | ValueNone ->
      drawStatLine
        sb
        font
        (float32 x + 10f)
        (float32 y + 40f)
        "No equipment found"
        ""
    | ValueSome wearableItems ->
      let drawEmptySlot slotName slotY =
        let startY = float32 y + 40f
        let lineHeight = 20f
        let leftX = float32 x + 10f
        let slotY = startY + slotY * lineHeight
        drawStatLine sb font leftX slotY $"{slotName}:" "Empty"

      let drawSlot slotY slotName itemDef =
        let startY = float32 y + 40f
        let lineHeight = 20f
        let leftX = float32 x + 10f
        let slotY = startY + slotY * lineHeight
        drawStatLine sb font leftX slotY $"{slotName}:" itemDef.Name


      wearableItems
      |> HashMap.tryFindV Head
      |> ValueOption.map(drawSlot 0f Head)
      |> ValueOption.defaultWith(fun () -> drawEmptySlot Head 0f)

      wearableItems
      |> HashMap.tryFindV Chest
      |> ValueOption.map(drawSlot 1f Chest)
      |> ValueOption.defaultWith(fun () -> drawEmptySlot Chest 1f)

      wearableItems
      |> HashMap.tryFindV Hands
      |> ValueOption.map(drawSlot 2f Hands)
      |> ValueOption.defaultWith(fun () -> drawEmptySlot Hands 2f)

      wearableItems
      |> HashMap.tryFindV Weapon1
      |> ValueOption.map(drawSlot 3f Weapon1)
      |> ValueOption.defaultWith(fun () -> drawEmptySlot Weapon1 3f)

      wearableItems
      |> HashMap.tryFindV Weapon2
      |> ValueOption.map(drawSlot 4f Weapon2)
      |> ValueOption.defaultWith(fun () -> drawEmptySlot Weapon2 4f)

      wearableItems
      |> HashMap.tryFindV Accessory
      |> ValueOption.map(drawSlot 5f Accessory)
      |> ValueOption.defaultWith(fun () -> drawEmptySlot Accessory 5f)

      wearableItems
      |> HashMap.tryFindV Legs
      |> ValueOption.map(drawSlot 6f Legs)
      |> ValueOption.defaultWith(fun () -> drawEmptySlot Legs 6f)

  let private drawAbilityList
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (font: SpriteFont)
    (entityId: Guid<EntityId>)
    (entities: HashMap<Guid<EntityId>, EntityComponents>)
    (gameTime: TimeSpan)
    (x: int)
    (y: int)
    =

    let width = 320
    let height = 300

    drawPanel sb pixel font x y width height "Abilities"

    match entities |> HashMap.tryFindV entityId with
    | ValueSome entity ->
      let startY = float32 y + 40f
      let lineHeight = 18f
      let leftX = float32 x + 10f

      let abilities = entity.Abilities |> HashSet.toArray

      let readyAbilities =
        entity.AbilityCooldowns
        |> HashMap.filter(fun _ readyTick -> readyTick <= gameTime)
        |> HashMap.keys

      for i in 0 .. min (abilities.Length - 1) 12 do
        let abilityId = abilities.[i]
        let abilityY = startY + float32 i * lineHeight

        let isReady = readyAbilities |> HashSet.contains abilityId
        let statusText = if isReady then "(Ready)" else "(Cooldown)"
        let color = if isReady then Color.LimeGreen else Color.Gray

        sb.DrawString(
          font,
          $"Ability {%abilityId} {statusText}",
          Vector2(leftX, abilityY),
          color
        )

    | ValueNone ->
      drawStatLine
        sb
        font
        (float32 x + 10f)
        (float32 y + 40f)
        "Entity not found"
        ""

  let draw
    (sb: SpriteBatch)
    (pixel: Texture2D)
    (font: SpriteFont)
    (uiState: UIState)
    (drawCtx: DrawingContext)
    =

    match uiState.SelectedEntity with
    | ValueSome entityId when not(uiState.ActivePanels |> HashSet.isEmpty) ->
      sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend)

      let panels = uiState.ActivePanels |> HashSet.toArray
      let panelWidth = 320
      let spacing = 20

      for i in 0 .. panels.Length - 1 do
        let panel = panels.[i]
        let x = 50 + i * (panelWidth + spacing)
        let y = 50

        match panel with
        | CharacterSheet ->
          drawCharacterSheet
            sb
            pixel
            font
            entityId
            drawCtx.Entities
            drawCtx.DerivedStats
            x
            y
        | EquipmentView ->
          drawEquipmentView sb pixel font entityId drawCtx.WearableItems x y
        | AbilityList ->
          drawAbilityList
            sb
            pixel
            font
            entityId
            drawCtx.Entities
            drawCtx.GameTime
            x
            y

      sb.End()

    | _ -> ()
