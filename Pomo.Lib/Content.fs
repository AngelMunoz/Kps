namespace Pomo.Lib.Content

open System
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.Visuals
open Pomo.Lib.Domain.Scenario
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.State
open Pomo.Lib.Scenario
open Pomo.Lib.Domain.Services


module ProjectileStore =
  let definitions: Map<int<ProjectileId>, ProjectileDefinition> =
    Map.ofList [
      1<ProjectileId>,
      {
        Id = 1<ProjectileId>
        Name = "Fireball"
        Shape = Shape.Circle 8.0f
        Speed = 32f * 10f
        Color = VisualColor.Red
        Size = 16.0f
        Behavior = Visuals.ProjectileBehavior.Seeker
        CollisionMode = Visuals.CollisionMode.IgnoreTerrain
        ImpactRadius = ValueNone
      }
      2<ProjectileId>,
      {
        Id = 2<ProjectileId>
        Name = "Frostbolt"
        Shape = Shape.Square 12.0f
        Speed = 32f * 8f
        Color = VisualColor.Blue
        Size = 12.0f
        Behavior = Visuals.ProjectileBehavior.Seeker
        CollisionMode = Visuals.CollisionMode.IgnoreTerrain
        ImpactRadius = ValueNone
      }
      3<ProjectileId>,
      {
        Id = 3<ProjectileId>
        Name = "Arrow"
        Shape = Shape.Square 8.0f
        Speed = 32f * 12f
        Color = VisualColor.Yellow
        Size = 12.0f
        Behavior = Visuals.ProjectileBehavior.Linear
        CollisionMode = Visuals.CollisionMode.BlockedByTerrain
        ImpactRadius = ValueSome 32.0f
      }
      4<ProjectileId>,
      {
        Id = 4<ProjectileId>
        Name = "Meteor"
        Shape = Shape.Circle 16.0f
        Speed = 32f * 6f
        Color = VisualColor.Orange
        Size = 32.0f
        Behavior = Visuals.ProjectileBehavior.Linear
        CollisionMode = Visuals.CollisionMode.IgnoreTerrain
        ImpactRadius = ValueSome 64.0f
      }
    ]

module AoeStore =
  let definitions: Map<int<AoeId>, AoeDefinition> =
    Map.ofList [
      1<AoeId>,
      {
        Id = 1<AoeId>
        Name = "Fire Nova"
        Shape = Shape.Circle(32f * 3f)
        Radius = 32f * 3f
        Color = VisualColor.Orange
      }
    ]

module ImpactStore =
  let definitions: Map<int<ImpactId>, ImpactDefinition> =
    Map.ofList [
      1<ImpactId>,
      {
        Id = 1<ImpactId>
        Name = "Slash"
        Shape = Shape.Square 10.0f
        Duration = TimeSpan.FromSeconds(0.3)
        Color = VisualColor.White
        Size = 20.0f
      }
      2<ImpactId>,
      {
        Id = 2<ImpactId>
        Name = "Explosion"
        Shape = Shape.Circle 20.0f
        Duration = TimeSpan.FromSeconds(0.5)
        Color = VisualColor.Yellow
        Size = 40.0f
      }
    ]

module EffectStore =
  let definitions: Map<int<EffectId>, EffectDefinition> =
    Map.ofList [
      1<EffectId>,
      {
        Id = 1<EffectId>
        Name = "Minor Strength Buff"
        Kind = EffectKind.Buff
        Duration = Timed(TimeSpan.FromSeconds(30.0))
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.StaticMod(StatModifier.Additive(AP, 5)) |]
        FormulaId = ValueNone
      }
      2<EffectId>,
      {
        Id = 2<EffectId>
        Name = "Minor Armor Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(TimeSpan.FromSeconds(20.0))
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
        Duration = Timed(TimeSpan.FromSeconds(5.0))
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      101<EffectId>,
      {
        Id = 101<EffectId>
        Name = "Silence"
        Kind = EffectKind.Silence
        Duration = Timed(TimeSpan.FromSeconds(8.0))
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      102<EffectId>,
      {
        Id = 102<EffectId>
        Name = "Stacking Shield"
        Kind = EffectKind.Buff
        Duration = Timed(TimeSpan.FromSeconds(30.0))
        Stacking = StackingRule.AddStack 5
        Modifiers = [| EffectModifier.StaticMod(StatModifier.Additive(DP, 2)) |]
        FormulaId = ValueNone
      }
      103<EffectId>,
      {
        Id = 103<EffectId>
        Name = "Taunt"
        Kind = EffectKind.Taunt
        Duration = Timed(TimeSpan.FromSeconds(3.0))
        Stacking = StackingRule.RefreshDuration
        Modifiers = Array.empty
        FormulaId = ValueNone
      }
      104<EffectId>,
      {
        Id = 104<EffectId>
        Name = "No-Stack Debuff"
        Kind = EffectKind.Debuff
        Duration = Timed(TimeSpan.FromSeconds(10.0))
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
        Duration = Loop(TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(8.0))
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
        Kind = EffectKind.ResourceOverTime
        Duration = Loop(TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(8.0))
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
        Duration = Timed(TimeSpan.FromSeconds(30.0))
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
        Duration = Timed(TimeSpan.FromSeconds(30.0))
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
        Duration = Timed(TimeSpan.FromSeconds(15.0))
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
        Duration = Timed(TimeSpan.FromSeconds(15.0))
        Stacking = StackingRule.RefreshDuration
        Modifiers = [| EffectModifier.DynamicMod(101<FormulaId>, MA) |]
        FormulaId = ValueSome 101<FormulaId>
      }
      304<EffectId>,
      {
        Id = 304<EffectId>
        Name = "Mana Regen"
        Kind = EffectKind.ResourceOverTime
        Duration = PermanentLoop(TimeSpan.FromSeconds(2.0))
        Stacking = StackingRule.RefreshDuration
        Modifiers = [|
          EffectModifier.StaticMod(StatModifier.Additive(MP, 10))
        |]
        FormulaId = ValueNone
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
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = TimeSpan.FromMilliseconds(2000.0) // 2 seconds
        Targeting = TargetType.SingleEnemy
        Range = 32.0f // 2 tiles
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueSome 1<ImpactId>
      }
      2<AbilityId>,
      {
        Id = 2<AbilityId>
        Name = "Fireball"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 20 }
        Cooldown = TimeSpan.FromSeconds(5.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 3f
        FormulaId = ValueSome 2<FormulaId>
        Effects = [| 2<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueSome 1<ProjectileId>
        AoeId = ValueNone
        ImpactId = ValueSome 2<ImpactId>
      }
      3<AbilityId>,
      {
        Id = 3<AbilityId>
        Name = "No-Stack Spell"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = TimeSpan.FromSeconds(1.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 5f
        FormulaId = ValueNone
        Effects = [| 104<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      4<AbilityId>,
      {
        Id = 4<AbilityId>
        Name = "Buff Spell"
        Intent = AbilityIntent.Support
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = TimeSpan.FromSeconds(1.0)
        Targeting = TargetType.Self
        Range = 0.0f
        FormulaId = ValueNone
        Effects = [| 1<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      5<AbilityId>,
      {
        Id = 5<AbilityId>
        Name = "Shield Spell"
        Intent = AbilityIntent.Support
        Cost = ValueSome { Type = ResourceType.MP; Amount = 15 }
        Cooldown = TimeSpan.FromSeconds(1.0)
        Targeting = TargetType.SingleAlly
        Range = 32f * 6f
        FormulaId = ValueNone
        Effects = [| 102<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      6<AbilityId>,
      {
        Id = 6<AbilityId>
        Name = "Poison Spell"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = TimeSpan.FromSeconds(1.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 4f
        FormulaId = ValueSome 3<FormulaId>
        Effects = [| 105<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      10<AbilityId>,
      {
        Id = 10<AbilityId>
        Name = "Quick Strike"
        Intent = AbilityIntent.Offensive
        Cost = ValueNone
        Cooldown = TimeSpan.FromSeconds(1.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 1f
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      11<AbilityId>,
      {
        Id = 11<AbilityId>
        Name = "Power Strike"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = TimeSpan.FromSeconds(2.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 1f
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      7<AbilityId>,
      {
        Id = 7<AbilityId>
        Name = "Regen Spell"
        Intent = AbilityIntent.Support
        Cost = ValueSome { Type = ResourceType.MP; Amount = 10 }
        Cooldown = TimeSpan.FromSeconds(1.0)
        Targeting = TargetType.SingleAlly
        Range = 32f * 5f
        FormulaId = ValueNone
        Effects = [| 106<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      8<AbilityId>,
      {
        Id = 8<AbilityId>
        Name = "Basic Melee Attack No Cost"
        Intent = AbilityIntent.Offensive
        Cost = ValueNone
        Cooldown = TimeSpan.FromSeconds(1.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 1f
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueSome 1<ImpactId>
      }
      9<AbilityId>,
      {
        Id = 9<AbilityId>
        Name = "Silence Spell"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 25 }
        Cooldown = TimeSpan.FromSeconds(10.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 5f
        FormulaId = ValueNone
        Effects = [| 101<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      // Enhanced Effects Test Abilities
      100<AbilityId>,
      {
        Id = 100<AbilityId>
        Name = "Sacrificial Strike"
        Intent = AbilityIntent.Offensive
        Cost = ValueNone
        Cooldown = TimeSpan.FromSeconds(3.0)
        Targeting = TargetType.SingleEnemy
        Range = 32f * 2f
        FormulaId = ValueSome 1<FormulaId>
        Effects = [| 200<EffectId>; 202<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueSome 1<ImpactId>
      }
      101<AbilityId>,
      {
        Id = 101<AbilityId>
        Name = "MP Conversion"
        Intent = AbilityIntent.Support
        Cost = ValueSome { Type = ResourceType.MP; Amount = 20 }
        Cooldown = TimeSpan.FromSeconds(5.0)
        Targeting = TargetType.Self
        Range = 0.0f
        FormulaId = ValueNone
        Effects = [| 201<EffectId> |]
        Requirements = Array.empty
        ProjectileId = ValueNone
        AoeId = ValueNone
        ImpactId = ValueNone
      }
      102<AbilityId>,
      {
        Id = 102<AbilityId>
        Name = "Arrow Shot"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 15 }
        Cooldown = TimeSpan.FromSeconds(3.0)
        Targeting = GroundTarget(32f * 1f)
        Range = 32f * 6f
        FormulaId = ValueSome 1<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
        ProjectileId = ValueSome 3<ProjectileId>
        AoeId = ValueNone
        ImpactId = ValueSome 1<ImpactId>
      }
      103<AbilityId>,
      {
        Id = 103<AbilityId>
        Name = "Meteor Shower"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 25 }
        Cooldown = TimeSpan.FromSeconds(5.0)
        Targeting = GroundTarget(32f * 2f)
        Range = 32f * 7f
        FormulaId = ValueSome 2<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
        ProjectileId = ValueSome 4<ProjectileId>
        AoeId = ValueNone
        ImpactId = ValueSome 2<ImpactId>
      }
      104<AbilityId>,
      {
        Id = 104<AbilityId>
        Name = "Magic Arrow"
        Intent = AbilityIntent.Offensive
        Cost = ValueSome { Type = ResourceType.MP; Amount = 15 }
        Cooldown = TimeSpan.FromSeconds(3.0)
        Targeting = GroundTarget(32f * 1f)
        Range = 32f * 4f
        FormulaId = ValueSome 3<FormulaId>
        Effects = Array.empty
        Requirements = Array.empty
        ProjectileId = ValueSome 3<ProjectileId>
        AoeId = ValueNone
        ImpactId = ValueSome 1<ImpactId>
      }
    ]

  let passiveDefinitions: Map<int<AbilityId>, PassiveAbilityDefinition> =
    Map.ofList [
      1001<AbilityId>,
      {
        Id = 1001<AbilityId>
        Name = "Total Concentration"
        Intent = AbilityIntent.Neutral
        Requirements = Array.empty
        Effects = [| 108<EffectId> |]
      }
      1002<AbilityId>,
      {
        Id = 1002<AbilityId>
        Name = "Mana Flow"
        Intent = AbilityIntent.Neutral
        Requirements = Array.empty
        Effects = [| 107<EffectId> |]
      }
      1003<AbilityId>,
      {
        Id = 1003<AbilityId>
        Name = "Mana Regeneration"
        Intent = AbilityIntent.Support
        Requirements = Array.empty
        Effects = [| 304<EffectId> |]
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
        StarterAbilities = HashSet.ofArray [| 8<AbilityId>; 1003<AbilityId> |]
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
          HashSet.ofArray [| 8<AbilityId>; 1<AbilityId>; 1003<AbilityId> |]
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
          HashSet.ofArray [|
            8<AbilityId>
            1<AbilityId>
            100<AbilityId>
            1003<AbilityId>
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
          HashSet.ofArray [| 2<AbilityId>; 4<AbilityId>; 1003<AbilityId> |]
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
          HashSet.ofArray [|
            2<AbilityId>
            4<AbilityId>
            6<AbilityId>
            102<AbilityId>
            103<AbilityId>
            104<AbilityId>
            1003<AbilityId>
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
          HashSet.ofArray [|
            2<AbilityId>
            4<AbilityId>
            6<AbilityId>
            9<AbilityId>
            1003<AbilityId>
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
          HashSet.ofArray [| 8<AbilityId>; 3<AbilityId>; 1003<AbilityId> |]
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
          HashSet.ofArray [|
            8<AbilityId>
            3<AbilityId>
            6<AbilityId>
            1003<AbilityId>
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
          HashSet.ofArray [|
            8<AbilityId>
            3<AbilityId>
            6<AbilityId>
            9<AbilityId>
            1003<AbilityId>
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
          HashSet.ofArray [|
            8<AbilityId>
            5<AbilityId>
            7<AbilityId>
            1003<AbilityId>
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
          HashSet.ofArray [|
            8<AbilityId>
            5<AbilityId>
            7<AbilityId>
            4<AbilityId>
            1003<AbilityId>
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
          HashSet.ofArray [|
            8<AbilityId>
            5<AbilityId>
            7<AbilityId>
            4<AbilityId>
            101<AbilityId>
            1003<AbilityId>
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

module AudioStore =

  let definitions: Map<int<AudioClipId>, Audio.AudioClip> =
    Map.ofList [
      1<AudioClipId>,
      {
        Id = 1<AudioClipId>
        Name = "Shy But Deadly 8 Bit"
        ContentPath = "Audio/bg_temp.mp3"
        Category = Audio.AudioCategory.Music
        Volume = 0.3f
        Pitch = 1.0f
        Loop = true
      }
      2<AudioClipId>,
      {
        Id = 2<AudioClipId>
        Name = "Skill Activation"
        ContentPath = "Audio/SkillActivation.wav"
        Category = Audio.AudioCategory.Ability
        Volume = 0.8f
        Pitch = 1.0f
        Loop = false
      }
      3<AudioClipId>,
      {
        Id = 3<AudioClipId>
        Name = "Fire Blip"
        ContentPath = "Audio/FireBlip.wav"
        Category = Audio.AudioCategory.Ability
        Volume = 0.7f
        Pitch = 1.0f
        Loop = false
      }
      4<AudioClipId>,
      {
        Id = 4<AudioClipId>
        Name = "Damage Received"
        ContentPath = "Audio/DamageReceived.wav"
        Category = Audio.AudioCategory.Impact
        Volume = 0.6f
        Pitch = 1.0f
        Loop = false
      }
      5<AudioClipId>,
      {
        Id = 5<AudioClipId>
        Name = "Missed Hit"
        ContentPath = "Audio/MissedHit.wav"
        Category = Audio.AudioCategory.Impact
        Volume = 0.5f
        Pitch = 1.0f
        Loop = false
      }
    ]

  let triggerMap: Map<Audio.AudioTrigger, int<AudioClipId>[]> =
    Map.ofList [
      Audio.AudioTrigger.AbilityCast 2<AbilityId>,
      [| 2<AudioClipId>; 3<AudioClipId> |]
      Audio.AudioTrigger.DamageTaken false, [| 5<AudioClipId> |]
      Audio.AudioTrigger.DamageTaken true, [| 4<AudioClipId> |]
    ]

  let scenarioMusicMap: Map<string, int<AudioClipId>> =
    Map.ofList [ "Test Scenario", 1<AudioClipId> ]

module AIArchetypeStore =
  open Pomo.Lib.Domain.CharacterKits
  open Pomo.Lib.Domain.Classification
  open Pomo.Lib.Domain.AI

  let definitions: Map<int<AiArchetypeId>, AI.AIArchetype> =
    Map.ofList [
      1<AiArchetypeId>,
      {
        id = 1<AiArchetypeId>
        name = "AggressiveMelee"
        characterKit =
          CharacterKitStore.definitions[{ Family = Power; Stage = First }]
        behaviorType = Aggressive
        perceptionConfig = {
          visualRange = 32f * 4f
          audioSensitivity = 1.5f
          memoryDuration = TimeSpan.FromSeconds(20.0)
          canDetectProjectiles = true
        }
        decisionInterval = TimeSpan.FromSeconds(0.5)
        cuePriorities = [|
          {
            cueType = Projectile
            minStrength = Strong
            priority = 1
            response = Evade
          }
          {
            cueType = Visual
            minStrength = Strong
            priority = 2
            response = Engage
          }
          {
            cueType = Audio
            minStrength = Strong
            priority = 3
            response = Investigate
          }
          {
            cueType = Memory
            minStrength = Moderate
            priority = 4
            response = Investigate
          }
        |]
        patrolWaypoints =
          ValueSome [|
            { X = -300f; Y = -300f } // Top-Left
            { X = 400f; Y = -400f } // Top-Right
            { X = 100f; Y = 100f } // Bottom-Right
            { X = -100f; Y = 100f } // Bottom-Left
          |]
      }
      2<AiArchetypeId>,
      {
        id = 2<AiArchetypeId>
        name = "StaticTurret"
        characterKit =
          CharacterKitStore.definitions[{ Family = Magic; Stage = First }]
        behaviorType = Turret
        perceptionConfig = {
          visualRange = 32f * 6f
          audioSensitivity = 0f
          memoryDuration = TimeSpan.FromSeconds(5.0)
          canDetectProjectiles = false
        }
        decisionInterval = TimeSpan.FromSeconds(2.0)
        cuePriorities = [|
          {
            cueType = Visual
            minStrength = Strong
            priority = 1
            response = Engage
          }
          {
            cueType = Memory
            minStrength = Moderate
            priority = 2
            response = Engage
          }
        |]
        patrolWaypoints = ValueNone
      }
      3<AiArchetypeId>,
      {
        id = 3<AiArchetypeId>
        name = "PatrolGuard"
        characterKit =
          CharacterKitStore.definitions[{ Family = Magic; Stage = Second }]
        behaviorType = Patrol
        perceptionConfig = {
          visualRange = 32f * 5f
          audioSensitivity = 2.0f
          memoryDuration = TimeSpan.FromSeconds(30.0)
          canDetectProjectiles = true
        }
        decisionInterval = TimeSpan.FromSeconds(1.5)
        cuePriorities = [|
          {
            cueType = Projectile
            minStrength = Weak
            priority = 1
            response = Investigate
          }
          {
            cueType = Tactile
            minStrength = Weak
            priority = 2
            response = Engage
          }
          {
            cueType = Visual
            minStrength = Weak
            priority = 3
            response = Engage
          }
          {
            cueType = Audio
            minStrength = Weak
            priority = 4
            response = Investigate
          }
        |]
        patrolWaypoints =
          ValueSome [|
            { X = -400f; Y = -400f } // Top-Left
            { X = 500f; Y = -500f } // Top-Right
            { X = 100f; Y = 100f } // Bottom-Right
            { X = -100f; Y = 100f } // Bottom-Left
          |]
      }
    ]


module ScenarioDefinitions =
  let createTownScenario(id: Guid<ScenarioId>) : Scenario = {
    Id = id
    Name = "Peaceful Town"
    BoundsWidth = 800f
    BoundsHeight = 600f
    BattleEnabled = false
    CombatType = PvE
    EngagementMode = Peaceful
    TerrainObjects =
      IndexList.ofList [
        // Buildings as blocked areas
        {
          Id = %Guid.NewGuid()
          Position = { X = 200f; Y = 150f }
          CollisionGeometry =
            Polygon(
              [|
                { X = 150f; Y = 100f }
                { X = 250f; Y = 100f }
                { X = 250f; Y = 200f }
                { X = 150f; Y = 200f }
              |]
            )
          TerrainType = Blocked
          DepthLayer = 0.6f
          SpriteId = ValueSome "town_house"
        }
        // Fountain as decorative water
        {
          Id = %Guid.NewGuid()
          Position = { X = 400f; Y = 300f }
          CollisionGeometry =
            CollisionGeometry.Circle({ X = 400f; Y = 300f }, 25f)
          TerrainType = TerrainType.Water
          DepthLayer = 0.4f
          SpriteId = ValueSome "fountain"
        }
      ]
    VisualLayers = [|
      {
        SpriteId = "town_background"
        Position = { X = 400f; Y = 300f }
        DepthLayer = 0.1f
        Parallax = 0.8f
      }
    |]
    Transitions = [|
      {
        FromPosition = { X = 750f; Y = 300f } // East exit
        ToScenarioId = %Guid.NewGuid() // Will be set when wilderness is created
        ToPosition = { X = 50f; Y = 300f } // West entrance of wilderness
      }
    |]
  }

  let createWildernessScenario
    (id: Guid<ScenarioId>)
    (townId: Guid<ScenarioId>)
    : Scenario =
    {
      Id = id
      Name = "Dark Wilderness"
      BoundsWidth = 1000f
      BoundsHeight = 800f
      BattleEnabled = true
      CombatType = PvE
      EngagementMode = AlwaysOn
      TerrainObjects =
        IndexList.ofList [
          // Dense forest areas as blocked terrain
          {
            Id = %Guid.NewGuid()
            Position = { X = 300f; Y = 200f }
            CollisionGeometry =
              Polygon(
                [|
                  { X = 250f; Y = 150f }
                  { X = 350f; Y = 150f }
                  { X = 380f; Y = 220f }
                  { X = 270f; Y = 250f }
                |]
              )
            TerrainType = Blocked
            DepthLayer = 0.7f
            SpriteId = ValueSome "dense_trees"
          }
          // Swamp areas
          {
            Id = %Guid.NewGuid()
            Position = { X = 600f; Y = 500f }
            CollisionGeometry =
              CollisionGeometry.Circle({ X = 600f; Y = 500f }, 60f)
            TerrainType = TerrainType.Water
            DepthLayer = 0.3f
            SpriteId = ValueSome "swamp"
          }
          // Hazard area (poisonous plants)
          {
            Id = %Guid.NewGuid()
            Position = { X = 800f; Y = 300f }
            CollisionGeometry =
              CollisionGeometry.Circle({ X = 800f; Y = 300f }, 40f)
            TerrainType = Hazard
            DepthLayer = 0.5f
            SpriteId = ValueSome "poison_plants"
          }
        ]
      VisualLayers = [|
        {
          SpriteId = "wilderness_background"
          Position = { X = 500f; Y = 400f }
          DepthLayer = 0.1f
          Parallax = 0.9f
        }
      |]
      Transitions = [|
        {
          FromPosition = { X = 50f; Y = 300f } // West entrance
          ToScenarioId = townId
          ToPosition = { X = 750f; Y = 300f } // East exit of town
        }
        {
          FromPosition = { X = 950f; Y = 400f } // East exit to dungeon
          ToScenarioId = %Guid.NewGuid() // Will be set when dungeon is created
          ToPosition = { X = 100f; Y = 400f } // West entrance of dungeon
        }
      |]
    }

  let createDungeonScenario
    (id: Guid<ScenarioId>)
    (wildernessId: Guid<ScenarioId>)
    : Scenario =
    {
      Id = id
      Name = "Ancient Dungeon"
      BoundsWidth = 600f
      BoundsHeight = 600f
      BattleEnabled = true
      CombatType = PvH
      EngagementMode = Structured
      TerrainObjects =
        IndexList.ofList [
          // Dungeon walls
          {
            Id = %Guid.NewGuid()
            Position = { X = 200f; Y = 200f }
            CollisionGeometry =
              Polygon(
                [|
                  { X = 150f; Y = 150f }
                  { X = 250f; Y = 150f }
                  { X = 250f; Y = 250f }
                  { X = 150f; Y = 250f }
                |]
              )
            TerrainType = Blocked
            DepthLayer = 0.8f
            SpriteId = ValueSome "stone_wall"
          }
          // Lava pit hazard
          {
            Id = %Guid.NewGuid()
            Position = { X = 400f; Y = 300f }
            CollisionGeometry =
              CollisionGeometry.Circle({ X = 400f; Y = 300f }, 35f)
            TerrainType = Hazard
            DepthLayer = 0.4f
            SpriteId = ValueSome "lava_pit"
          }
          // Water trap
          {
            Id = %Guid.NewGuid()
            Position = { X = 300f; Y = 450f }
            CollisionGeometry =
              Polygon(
                [|
                  { X = 280f; Y = 430f }
                  { X = 320f; Y = 430f }
                  { X = 320f; Y = 470f }
                  { X = 280f; Y = 470f }
                |]
              )
            TerrainType = TerrainType.Water
            DepthLayer = 0.2f
            SpriteId = ValueSome "water_trap"
          }
        ]
      VisualLayers = [|
        {
          SpriteId = "dungeon_background"
          Position = { X = 300f; Y = 300f }
          DepthLayer = 0.1f
          Parallax = 1.0f
        }
      |]
      Transitions = [|
        {
          FromPosition = { X = 100f; Y = 400f } // West entrance
          ToScenarioId = wildernessId
          ToPosition = { X = 950f; Y = 400f } // East exit of wilderness
        }
      |]
    }

  let createConnectedScenarios() =
    let townId = %Guid.NewGuid()
    let wildernessId = %Guid.NewGuid()
    let dungeonId = %Guid.NewGuid()

    let town = createTownScenario townId
    let wilderness = createWildernessScenario wildernessId townId
    let dungeon = createDungeonScenario dungeonId wildernessId

    // Update town's transition to point to wilderness
    let updatedTown = {
      town with
          Transitions = [|
            {
              town.Transitions.[0] with
                  ToScenarioId = wildernessId
            }
          |]
    }

    // Update wilderness's transition to point to dungeon
    let updatedWilderness = {
      wilderness with
          Transitions = [|
            wilderness.Transitions.[0]
            {
              wilderness.Transitions.[1] with
                  ToScenarioId = dungeonId
            }
          |]
    }

    struct (updatedTown, updatedWilderness, dungeon)

module GameState =
  let create'
    (services: Services.EngineServices)
    (
      activeScenarioId: Guid<ScenarioId>,
      scenarios: cmap<Guid<ScenarioId>, ScenarioState>
    ) =
    {
      scenarios = scenarios
      activeScenarioId = cval activeScenarioId
      players = cmap()
      parties = cmap()
      services = services
    }

  let create() =
    let initialScenarioId = %Guid.NewGuid()

    let initialScenarioState =
      {
        Id = initialScenarioId
        Name = "Test Scenario"
        BoundsWidth = 2000f
        BoundsHeight = 2000f
      }
      |> ScenarioState.create id

    let scenarios = cmap [ initialScenarioId, initialScenarioState ]

    create'
      {
        effectStore =
          { new IEffectStore with
              member _.tryFind effectId =
                EffectStore.definitions
                |> Map.tryFind effectId
                |> ValueOption.ofOption

              member _.find effectId =
                EffectStore.definitions |> Map.find effectId
          }
        abilityStore =
          { new IAbilityStore with
              member _.tryFind abilityId =
                AbilityStore.definitions
                |> Map.tryFind abilityId
                |> ValueOption.ofOption

              member _.find abilityId =
                AbilityStore.definitions |> Map.find abilityId
          }
        formulaStore =
          { new IFormulaStore with
              member _.tryFind formulaId =
                FormulaStore.definitions
                |> Map.tryFind formulaId
                |> ValueOption.ofOption

              member _.find formulaId =
                FormulaStore.definitions |> Map.find formulaId
          }
        projectileStore =
          { new IProjectileStore with
              member _.tryFind projectileId =
                ProjectileStore.definitions
                |> Map.tryFind projectileId
                |> ValueOption.ofOption

              member _.find projectileId =
                ProjectileStore.definitions |> Map.find projectileId
          }
        aoeStore =
          { new IAoeStore with
              member _.tryFind aoeId =
                AoeStore.definitions
                |> Map.tryFind aoeId
                |> ValueOption.ofOption

              member _.find aoeId = AoeStore.definitions |> Map.find aoeId
          }
        impactStore =
          { new IImpactStore with
              member _.tryFind impactId =
                ImpactStore.definitions
                |> Map.tryFind impactId
                |> ValueOption.ofOption

              member _.find impactId =
                ImpactStore.definitions |> Map.find impactId
          }
        audioStore =
          { new IAudioStore with
              member _.tryFind clipId =
                AudioStore.definitions
                |> Map.tryFind clipId
                |> ValueOption.ofOption

              member _.find clipId =
                AudioStore.definitions |> Map.find clipId

              member _.findByTrigger trigger =
                AudioStore.triggerMap
                |> Map.tryFind trigger
                |> Option.defaultValue Array.empty

              member _.findMusicForScenario scenarioId =
                let scenario =
                  scenarios.Value
                  |> HashMap.tryFindV scenarioId
                  |> ValueOption.map _.scenario

                scenario
                |> ValueOption.bind(fun s ->
                  AudioStore.scenarioMusicMap
                  |> Map.tryFind s.Name
                  |> ValueOption.ofOption)
          }
        aiArchetypeStore =
          { new IAIArchetypeStore with
              member _.tryFind archetypeId =
                AIArchetypeStore.definitions
                |> Map.tryFind archetypeId
                |> ValueOption.ofOption

              member _.find archetypeId =
                AIArchetypeStore.definitions |> Map.find archetypeId
          }
        rng = fun () -> System.Random().NextDouble()
      }
      (initialScenarioId, scenarios)
