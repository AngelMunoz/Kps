namespace Pomo.Lib.Content

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Effects

module EffectStore =
  let definitions: Map<int<EffectId>, EffectDefinition> =
    Map.ofList [
      1<EffectId>,
      {
        Id = 1<EffectId>
        Name = "Minor Strength Buff"
        Kind = EffectKind.Buff
        Duration = Timed(30000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Additive(Power, 5))
        |]
        FormulaId = ValueNone
      }
      2<EffectId>,
      {
        Id = 2<EffectId>
        Name = "Minor Armor Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(20000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Additive(DP, -5))
        |]
        FormulaId = ValueNone
      }
      // Phase 3 Effect Kinds for testing
      100<EffectId>,
      {
        Id = 100<EffectId>
        Name = "Stun"
        Kind = EffectKind.Stun
        Duration = Timed(5000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      101<EffectId>,
      {
        Id = 101<EffectId>
        Name = "Silence"
        Kind = EffectKind.Silence
        Duration = Timed(8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      103<EffectId>,
      {
        Id = 103<EffectId>
        Name = "Taunt"
        Kind = EffectKind.Taunt
        Duration = Timed(3000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      104<EffectId>,
      {
        Id = 104<EffectId>
        Name = "No-Stack Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(10000L<Tick>)
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Subtractive(DX, 2))
        |]
        FormulaId = ValueNone
      }
      105<EffectId>,
      {
        Id = 105<EffectId>
        Name = "Poison"
        Kind = EffectKind.DamageOverTime
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Subtractive(HP, 5))
        |]
        FormulaId = ValueNone
      }
      106<EffectId>,
      {
        Id = 106<EffectId>
        Name = "Regeneration"
        Kind = EffectKind.HealOverTime
        Duration = Loop(2000L<Tick>, 8000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.StaticMod(StatModifier.Additive(HP, 5)) |]
        FormulaId = ValueNone
      }
      107<EffectId>,
      {
        Id = 107<EffectId>
        Name = "MP Boost"
        Kind = EffectKind.Buff
        Duration = Permanent
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Multiplicative(MP, 1.2))
        |]
        FormulaId = ValueNone
      }
      108<EffectId>,
      {
        Id = 108<EffectId>
        Name = "AP Boost"
        Kind = EffectKind.Buff
        Duration = Permanent
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Multiplicative(AP, 1.15))
        |]
        FormulaId = ValueNone
      }
      // Enhanced Effects for Testing (Phase 4.5 - ResourceConversion integrated in applyResourceCost)
      200<EffectId>,
      {
        Id = 200<EffectId>
        Name = "Sacrificial Power HP Cost"
        Kind = EffectKind.Buff
        Duration = Timed(30000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.ResourceConversion(
            ResourceType.HP,
            ResourceType.HP,
            -0.10
          )
        |]
        FormulaId = ValueNone
      }
      202<EffectId>,
      {
        Id = 202<EffectId>
        Name = "Sacrificial Power Damage Boost"
        Kind = EffectKind.Buff
        Duration = Timed(30000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.AbilityDamageMod(0.50) |]
        FormulaId = ValueNone
      }
      201<EffectId>,
      {
        Id = 201<EffectId>
        Name = "MP to HP Conversion"
        Kind = EffectKind.Buff
        Duration = Instant
        Stacking = StackingRule.NoStack
        Modifiers = [|
          EffectModifier.ResourceConversion(
            ResourceType.MP,
            ResourceType.HP,
            0.5
          )
        |]
        FormulaId = ValueNone
      }
      // DynamicMod Test Effect
      300<EffectId>,
      {
        Id = 300<EffectId>
        Name = "Dynamic AP Boost"
        Kind = EffectKind.Buff
        Duration = Timed(15000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.DynamicMod(101<FormulaId>, AP) |]
        FormulaId = ValueSome 101<FormulaId>
      }
      // DynamicMod Test Effect targeting MA
      301<EffectId>,
      {
        Id = 301<EffectId>
        Name = "Dynamic MA Boost"
        Kind = EffectKind.Buff
        Duration = Timed(15000L<Tick>)
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.DynamicMod(101<FormulaId>, MA) |]
        FormulaId = ValueSome 101<FormulaId>
      }
    ]

module FormulaStore =
  let definitions: Map<int<FormulaId>, FormulaDefinition> =
    Map.ofList [
      1<FormulaId>,
      {
        Id = 1<FormulaId>
        Name = "Physical + Neutral Damage"
        Calculate =
          fun ctx -> {
            BaseDamage = ctx.InvokerStats.AP * 2
            ElementalDamage = 0
            Element = Attributes.Neutral
            DamageType = DamageType.Physical
          }
      }
      2<FormulaId>,
      {
        Id = 2<FormulaId>
        Name = "Fire + Magical Damage"
        Calculate =
          fun ctx ->
            let fireAttr =
              ctx.InvokerElementalAttributes.TryFindV Attributes.Fire
              |> ValueOption.defaultValue 0.0

            let fireDamage = fireAttr * (ctx.InvokerStats.MA * 2 |> float)

            {
              BaseDamage = ctx.InvokerStats.MA * 2
              ElementalDamage = fireDamage |> int
              Element = Attributes.Fire
              DamageType = DamageType.Magical
            }
      }
      3<FormulaId>,
      {
        Id = 3<FormulaId>
        Name = "Magic + Neutral Damage"
        Calculate =
          fun ctx -> {
            BaseDamage = ctx.InvokerStats.MA * 2
            ElementalDamage = 0
            Element = Attributes.Neutral
            DamageType = DamageType.Magical
          }
      }
      4<FormulaId>,
      {
        Id = 4<FormulaId>
        Name = "Fire + Physical Damage"
        Calculate =
          fun ctx ->
            let fireAttr =
              ctx.InvokerElementalAttributes.TryFindV Attributes.Fire
              |> ValueOption.defaultValue 0.0

            let elementalDamage = fireAttr * (ctx.InvokerStats.AP * 2 |> float)

            {
              BaseDamage = ctx.InvokerStats.AP * 2
              ElementalDamage = elementalDamage |> int
              Element = Attributes.Fire
              DamageType = DamageType.Physical
            }
      }
      // Enhanced Effects Formulas
      100<FormulaId>,
      {
        Id = 100<FormulaId>
        Name = "HP Cost Calculation"
        Calculate =
          fun ctx -> {
            BaseDamage = ctx.InvokerStats.HP / 10
            ElementalDamage = 0
            Element = Attributes.Neutral
            DamageType = DamageType.Physical
          }
      }
      // DynamicMod Test Formula
      101<FormulaId>,
      {
        Id = 101<FormulaId>
        Name = "Magic-based AP Boost"
        Calculate =
          fun ctx -> {
            BaseDamage = ctx.InvokerStats.MA / 2 // AP boost = half of Magic Attack
            ElementalDamage = 0
            Element = Attributes.Neutral
            DamageType = DamageType.Neutral
          }
      }
    ]

module AbilityStore =

  let activeDefinitions: Map<int<AbilityId>, ActiveAbilityDefinition> =
    Map.ofList [
      1<AbilityId>,
      {
        Id = 1<AbilityId>
        Name = "Melee Attack"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 2000L<Tick> // 2 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
      }
      2<AbilityId>,
      {
        Id = 2<AbilityId>
        Name = "Fireball"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<Tick> // 5 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 2<FormulaId>
        Effects = [| 2<EffectId> |]
        Requirements = Array.empty
      }
      3<AbilityId>,
      {
        Id = 3<AbilityId>
        Name = "No-Stack Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueNone
        Effects = [| 104<EffectId> |]
        Requirements = Array.empty
      }
      4<AbilityId>,
      {
        Id = 4<AbilityId>
        Name = "Buff Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.Self
        FormulaId = ValueNone
        Effects = [| 1<EffectId> |] // RefreshDuration effect
        Requirements = Array.empty
      }
      5<AbilityId>,
      {
        Id = 5<AbilityId>
        Name = "Shield Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 15 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleAlly
        FormulaId = ValueNone
        Effects = [| 102<EffectId> |] // AddStack effect
        Requirements = Array.empty
      }
      6<AbilityId>,
      {
        Id = 6<AbilityId>
        Name = "Poison Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 3<FormulaId>
        Effects = [| 105<EffectId> |] // DoT effect
        Requirements = Array.empty
      }
      7<AbilityId>,
      {
        Id = 7<AbilityId>
        Name = "Regen Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleAlly
        FormulaId = ValueNone
        Effects = [| 106<EffectId> |] // HoT effect
        Requirements = Array.empty
      }
      8<AbilityId>,
      {
        Id = 8<AbilityId>
        Name = "Basic Melee Attack No Cost"
        Cost = ValueNone
        Cooldown = 1000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
      }
      9<AbilityId>,
      {
        Id = 9<AbilityId>
        Name = "Silence Spell"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 25 }
        Cooldown = 10000L<Tick> // 10 seconds
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueNone
        Effects = [| 101<EffectId> |] // Silence effect
        Requirements = Array.empty
      }
      // Enhanced Effects Test Abilities
      100<AbilityId>,
      {
        Id = 100<AbilityId>
        Name = "Sacrificial Strike"
        Cost = ValueNone
        Cooldown = 3000L<Tick>
        Targeting = TargetType.SingleEnemy
        FormulaId = ValueSome 1<FormulaId>
        Effects = [| 200<EffectId>; 202<EffectId> |] // Applies HP cost + damage boost to self
        Requirements = Array.empty
      }
      101<AbilityId>,
      {
        Id = 101<AbilityId>
        Name = "MP Conversion"
        Cost = ValueSome { Type = ResourceType.MP; Amount = 20 }
        Cooldown = 5000L<Tick>
        Targeting = TargetType.Self
        FormulaId = ValueNone
        Effects = [| 201<EffectId> |] // Converts MP to HP
        Requirements = Array.empty
      }
    ]

  let passiveDefinitions: Map<int<AbilityId>, PassiveAbilityDefinition> =
    Map.ofList [
      1001<AbilityId>,
      {
        Id = 1001<AbilityId>
        Name = "Total Concentration"
        Requirements = Array.empty
        Effects = [| 108<EffectId> |]
      }
      1002<AbilityId>,
      {
        Id = 1002<AbilityId>
        Name = "Mana Flow"
        Requirements = Array.empty
        Effects = [| 107<EffectId> |]
      }
    ]

  // Unified ability store that returns AbilityKind
  let definitions: Map<int<AbilityId>, AbilityKind> =
    let actives = activeDefinitions |> Map.map(fun _ def -> Active def)

    let passives = passiveDefinitions |> Map.map(fun _ def -> Passive def)

    Map.fold (fun acc k v -> Map.add k v acc) actives passives

module CharacterKitStore =
  open Pomo.Lib.Domain.Classification
  open Pomo.Lib.Domain.Attributes
  open Pomo.Lib.Domain.CharacterKits

  let definitions: Map<Profession, CharacterKit> =
    Map.ofList [
      { Family = Power; Stage = First },
      {
        Profession = { Family = Power; Stage = First }
        Name = "Warrior Initiate"
        BaseStats = {
          Power = 15
          Magic = 5
          Sense = 8
          Charm = 12
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [| 8<AbilityId> |]
      }
      { Family = Power; Stage = Second },
      {
        Profession = { Family = Power; Stage = Second }
        Name = "Veteran Fighter"
        BaseStats = {
          Power = 22
          Magic = 7
          Sense = 12
          Charm = 17
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [| 8<AbilityId>; 1<AbilityId> |]
      }
      { Family = Power; Stage = Third },
      {
        Profession = { Family = Power; Stage = Third }
        Name = "Battle Master"
        BaseStats = {
          Power = 30
          Magic = 10
          Sense = 15
          Charm = 22
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            8<AbilityId>
            1<AbilityId>
            100<AbilityId>
          |]
      }
      { Family = Magic; Stage = First },
      {
        Profession = { Family = Magic; Stage = First }
        Name = "Apprentice Mage"
        BaseStats = {
          Power = 5
          Magic = 15
          Sense = 10
          Charm = 8
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [| 2<AbilityId>; 4<AbilityId> |]
      }
      { Family = Magic; Stage = Second },
      {
        Profession = { Family = Magic; Stage = Second }
        Name = "Adept Sorcerer"
        BaseStats = {
          Power = 8
          Magic = 22
          Sense = 14
          Charm = 12
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            2<AbilityId>
            4<AbilityId>
            6<AbilityId>
          |]
      }
      { Family = Magic; Stage = Third },
      {
        Profession = { Family = Magic; Stage = Third }
        Name = "Archmage"
        BaseStats = {
          Power = 10
          Magic = 30
          Sense = 18
          Charm = 15
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            2<AbilityId>
            4<AbilityId>
            6<AbilityId>
            9<AbilityId>
          |]
      }
      { Family = Sense; Stage = First },
      {
        Profession = { Family = Sense; Stage = First }
        Name = "Scout Novice"
        BaseStats = {
          Power = 10
          Magic = 8
          Sense = 15
          Charm = 10
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [| 8<AbilityId>; 3<AbilityId> |]
      }
      { Family = Sense; Stage = Second },
      {
        Profession = { Family = Sense; Stage = Second }
        Name = "Skilled Ranger"
        BaseStats = {
          Power = 14
          Magic = 12
          Sense = 22
          Charm = 14
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            8<AbilityId>
            3<AbilityId>
            6<AbilityId>
          |]
      }
      { Family = Sense; Stage = Third },
      {
        Profession = { Family = Sense; Stage = Third }
        Name = "Master Scout"
        BaseStats = {
          Power = 18
          Magic = 15
          Sense = 30
          Charm = 18
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            8<AbilityId>
            3<AbilityId>
            6<AbilityId>
            9<AbilityId>
          |]
      }
      { Family = Charm; Stage = First },
      {
        Profession = { Family = Charm; Stage = First }
        Name = "Defender Trainee"
        BaseStats = {
          Power = 8
          Magic = 7
          Sense = 8
          Charm = 15
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            8<AbilityId>
            5<AbilityId>
            7<AbilityId>
          |]
      }
      { Family = Charm; Stage = Second },
      {
        Profession = { Family = Charm; Stage = Second }
        Name = "Guardian Knight"
        BaseStats = {
          Power = 12
          Magic = 10
          Sense = 12
          Charm = 22
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            8<AbilityId>
            5<AbilityId>
            7<AbilityId>
            4<AbilityId>
          |]
      }
      { Family = Charm; Stage = Third },
      {
        Profession = { Family = Charm; Stage = Third }
        Name = "Protector Paragon"
        BaseStats = {
          Power = 15
          Magic = 13
          Sense = 15
          Charm = 30
        }
        StarterAbilities =
          FSharp.Data.Adaptive.HashSet.ofArray [|
            8<AbilityId>
            5<AbilityId>
            7<AbilityId>
            4<AbilityId>
            101<AbilityId>
          |]
      }
    ]

module EquipmentStore =
  open Pomo.Lib.Domain.Inventory
  open Pomo.Lib.Domain.Attributes
  open FSharp.Data.Adaptive

  let definitions: Map<int<ItemId>, Equipment> =
    Map.ofList [
      1<ItemId>,
      {
        Id = 1<ItemId>
        Name = "Iron Helm"
        Slot = Head
        Rarity = Common
        StatBonuses = [| { Stat = DP; Value = 5 }; { Stat = HP; Value = 20 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
      2<ItemId>,
      {
        Id = 2<ItemId>
        Name = "Leather Cap"
        Slot = Head
        Rarity = Common
        StatBonuses = [| { Stat = HV; Value = 3 }; { Stat = DX; Value = 2 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
      3<ItemId>,
      {
        Id = 3<ItemId>
        Name = "Wizard's Hat"
        Slot = Head
        Rarity = Uncommon
        StatBonuses = [| { Stat = MA; Value = 8 }; { Stat = MP; Value = 30 } |]
        ElementalAttributes = HashMap.ofList [ Fire, 5.0 ]
        ElementalResistances = HashMap.empty
      }
      4<ItemId>,
      {
        Id = 4<ItemId>
        Name = "Steel Plate Armor"
        Slot = Chest
        Rarity = Uncommon
        StatBonuses = [| { Stat = DP; Value = 15 }; { Stat = HP; Value = 50 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.ofList [ Fire, 0.1; Lightning, 0.15 ]
      }
      5<ItemId>,
      {
        Id = 5<ItemId>
        Name = "Mystic Robes"
        Slot = Chest
        Rarity = Rare
        StatBonuses = [|
          { Stat = MA; Value = 12 }
          { Stat = MD; Value = 10 }
          { Stat = MP; Value = 50 }
        |]
        ElementalAttributes = HashMap.ofList [ Light, 8.0 ]
        ElementalResistances = HashMap.ofList [ Dark, 0.2 ]
      }
      6<ItemId>,
      {
        Id = 6<ItemId>
        Name = "Ranger's Tunic"
        Slot = Chest
        Rarity = Uncommon
        StatBonuses = [|
          { Stat = DX; Value = 8 }
          { Stat = HV; Value = 6 }
          { Stat = DA; Value = 5 }
        |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.ofList [ Earth, 0.15 ]
      }
      7<ItemId>,
      {
        Id = 7<ItemId>
        Name = "Chainmail Leggings"
        Slot = Legs
        Rarity = Common
        StatBonuses = [| { Stat = DP; Value = 8 }; { Stat = HP; Value = 30 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
      8<ItemId>,
      {
        Id = 8<ItemId>
        Name = "Enchanted Greaves"
        Slot = Legs
        Rarity = Epic
        StatBonuses = [|
          { Stat = DP; Value = 20 }
          { Stat = MD; Value = 15 }
          { Stat = HP; Value = 80 }
        |]
        ElementalAttributes = HashMap.ofList [ Light, 10.0 ]
        ElementalResistances = HashMap.ofList [ Fire, 0.25; Dark, 0.25 ]
      }
      9<ItemId>,
      {
        Id = 9<ItemId>
        Name = "Leather Gloves"
        Slot = Hands
        Rarity = Common
        StatBonuses = [| { Stat = DX; Value = 3 }; { Stat = AC; Value = 2 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
      10<ItemId>,
      {
        Id = 10<ItemId>
        Name = "Gauntlets of Strength"
        Slot = Hands
        Rarity = Rare
        StatBonuses = [| { Stat = AP; Value = 15 }; { Stat = AC; Value = 10 } |]
        ElementalAttributes = HashMap.ofList [ Fire, 12.0 ]
        ElementalResistances = HashMap.empty
      }
      11<ItemId>,
      {
        Id = 11<ItemId>
        Name = "Iron Sword"
        Slot = Weapon1
        Rarity = Common
        StatBonuses = [| { Stat = AP; Value = 10 }; { Stat = AC; Value = 5 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
      12<ItemId>,
      {
        Id = 12<ItemId>
        Name = "Flamebrand"
        Slot = Weapon1
        Rarity = Epic
        StatBonuses = [| { Stat = AP; Value = 25 }; { Stat = AC; Value = 15 } |]
        ElementalAttributes = HashMap.ofList [ Fire, 20.0 ]
        ElementalResistances = HashMap.empty
      }
      13<ItemId>,
      {
        Id = 13<ItemId>
        Name = "Staff of Arcane Power"
        Slot = Weapon1
        Rarity = Legendary
        StatBonuses = [|
          { Stat = MA; Value = 35 }
          { Stat = MP; Value = 100 }
          { Stat = LK; Value = 10 }
        |]
        ElementalAttributes = HashMap.ofList [ Light, 25.0; Fire, 15.0 ]
        ElementalResistances = HashMap.ofList [ Dark, 0.3 ]
      }
      14<ItemId>,
      {
        Id = 14<ItemId>
        Name = "Wooden Shield"
        Slot = Weapon2
        Rarity = Common
        StatBonuses = [| { Stat = DP; Value = 8 }; { Stat = HV; Value = 3 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
      15<ItemId>,
      {
        Id = 15<ItemId>
        Name = "Tower Shield"
        Slot = Weapon2
        Rarity = Rare
        StatBonuses = [| { Stat = DP; Value = 20 }; { Stat = HP; Value = 60 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.ofList [ Fire, 0.2; Water, 0.15 ]
      }
      16<ItemId>,
      {
        Id = 16<ItemId>
        Name = "Lucky Charm"
        Slot = Accessory
        Rarity = Uncommon
        StatBonuses = [| { Stat = LK; Value = 8 }; { Stat = HV; Value = 5 } |]
        ElementalAttributes = HashMap.empty
        ElementalResistances = HashMap.empty
      }
      17<ItemId>,
      {
        Id = 17<ItemId>
        Name = "Amulet of Vitality"
        Slot = Accessory
        Rarity = Rare
        StatBonuses = [|
          { Stat = HP; Value = 100 }
          { Stat = DP; Value = 10 }
          { Stat = MD; Value = 10 }
        |]
        ElementalAttributes = HashMap.empty
        ElementalResistances =
          HashMap.ofList [ Fire, 0.15; Water, 0.15; Earth, 0.15; Air, 0.15 ]
      }
      18<ItemId>,
      {
        Id = 18<ItemId>
        Name = "Ring of Elements"
        Slot = Accessory
        Rarity = Legendary
        StatBonuses = [| { Stat = MA; Value = 20 }; { Stat = DA; Value = 15 } |]
        ElementalAttributes =
          HashMap.ofList [
            Fire, 10.0
            Water, 10.0
            Earth, 10.0
            Air, 10.0
            Lightning, 10.0
          ]
        ElementalResistances =
          HashMap.ofList [
            Fire, 0.25
            Water, 0.25
            Earth, 0.25
            Air, 0.25
            Lightning, 0.25
          ]
      }
    ]
