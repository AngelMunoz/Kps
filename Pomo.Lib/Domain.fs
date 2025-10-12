namespace Pomo.Lib.Domain

open System
open FSharp.Data.Adaptive
open FSharp.UMX

// All measure types defined at the top
[<Measure>]
type Tick

[<Measure>]
type EntityId

[<Measure>]
type EffectId

[<Measure>]
type AbilityId

[<Measure>]
type FormulaId

// Core types available at namespace level
[<Struct>]
type ResourceType =
  | HP
  | MP

[<Struct>]
type Position = { X: float32; Y: float32 }

[<Struct>]
type Movement = {
  Speed: float32 // units per second
  Destination: Position voption
  Path: Position list
}

[<Struct>]
type Stat =
  // Base attributes
  | Power
  | Magic
  | Sense
  | Charm
  // Derived stats (using game definition names)
  | AP // Attack Power
  | AC // Accuracy
  | DX // Dexterity
  | MP // Mana Pool
  | MA // Magic Attack
  | MD // Magic Defense
  | WT // Weight
  | DA // Detect Ability
  | LK // Luck
  | HP // Health Pool
  | DP // Defense Points
  | HV // Evasion

[<Struct>]
type AbilityRequirement =
  | StatRequirement of Stat * int
  | AbilityRequirement of int<AbilityId>
  | FormulaRequirement of int<FormulaId>

module Classification =
  [<Struct>]
  type Faction =
    | Player
    | Enemy
    | Ally
    | Neutral
    | Terrain

  [<Struct>]
  type Tag =
    | Biological
    | Artificial
    | Undead

  [<Struct>]
  type Family =
    | Power
    | Magic
    | Charm
    | Sense

  [<Struct>]
  type Stage =
    | First
    | Second
    | Third

  [<Struct>]
  type Profession = { Family: Family; Stage: Stage }

module Attributes =
  [<Struct>]
  type Element =
    | Fire
    | Water
    | Earth
    | Air
    | Lightning
    | Light
    | Dark
    | Neutral

  [<Struct>]
  type BaseAttributes = {
    Power: int
    Magic: int
    Sense: int
    Charm: int
  }

  [<Struct>]
  type DerivedStats = {
    // Power derived stats
    AP: int
    AC: int
    DX: int
    // Magic derived stats
    MP: int
    MA: int
    MD: int
    // Sense derived stats
    WT: int
    DA: int
    LK: int
    // Charm derived stats
    HP: int
    DP: int
    HV: int

    // Element % of attributes and resistances
    ElementAttributes: FSharp.Data.Adaptive.HashMap<Element, float>
    ElementResistances: FSharp.Data.Adaptive.HashMap<Element, float>
  }

  [<Struct>]
  type Status =
    | Alive
    | Dead
    | Disabled

  [<Struct>]
  type Resources = { HP: int; MP: int; Status: Status }

module Inventory =
  [<Struct>]
  type Slot =
    | Head
    | Chest
    | Legs
    | Hands
    | Weapon1
    | Weapon2
    | Accessory

  [<Measure>]
  type ItemId

  [<Struct>]
  type Rarity =
    | Common
    | Uncommon
    | Rare
    | Epic
    | Legendary

  [<Struct>]
  type ItemStatBonus = { Stat: Stat; Value: int }

  [<Struct>]
  type Equipment = {
    Id: int<ItemId>
    Name: string
    Slot: Slot
    Rarity: Rarity
    StatBonuses: ItemStatBonus array
    ElementalAttributes: HashMap<Attributes.Element, float>
    ElementalResistances: HashMap<Attributes.Element, float>
  }

module Effects =

  [<Struct>]
  type EffectKind =
    | Buff
    | Debuff
    | DamageOverTime
    | HealOverTime
    | Stun
    | Silence
    | Taunt

  [<Struct>]
  type StackingRule =
    | NoStack
    | RefreshDuration
    | AddStack of int // max stacks

  [<Struct>]
  type Duration =
    | Instant
    | Timed of int64<Tick>
    | Loop of int64<Tick> * int64<Tick> // Interval * Total Duration
    | Permanent // For passive skill effects, never expires

  [<Struct>]
  type StatModifier =
    | Additive of addStat: Stat * adStatValue: int
    | Subtractive of subStat: Stat * subStatValue: int
    | Multiplicative of mulStat: Stat * mulStatValue: float
    | Divisive of divStat: Stat * divStatValue: float

  [<Struct>]
  type EffectModifier =
    | StaticMod of StatModifier
    | DynamicMod of formulaId: int<FormulaId> * target: Stat
    | AbilityDamageMod of abilityDamageValue: float
    | ResourceConversion of ResourceType * ResourceType * float

  [<Struct>]
  type EffectDefinition = {
    Id: int<EffectId>
    Name: string
    Kind: EffectKind
    Stacking: StackingRule
    Duration: Duration
    Modifiers: EffectModifier[]
    FormulaId: int<FormulaId> voption
  }

  [<Struct>]
  type ActiveEffect = {
    EffectId: int<EffectId> // Corresponds to a definition
    SourceId: Guid<EntityId>
    RemainingTicks: int64<Tick>
    NextTickIn: int64<Tick>
    Stacks: int
    Definition: EffectDefinition
  }

module Abilities =

  [<Struct>]
  type DamageType =
    | Physical
    | Magical
    | Neutral

  [<Struct>]
  type DamageResult = {
    BaseDamage: int
    ElementalDamage: int
    Element: Attributes.Element
    DamageType: DamageType
  } with

    static member inline (+)(a: DamageResult, b: DamageResult) : DamageResult = {
      a with
          BaseDamage = a.BaseDamage + b.BaseDamage
          ElementalDamage = a.ElementalDamage + b.ElementalDamage
    }

  [<Struct>]
  type CalculationContext = {
    InvokerStats: Attributes.DerivedStats
    InvokerElementalAttributes: HashMap<Attributes.Element, float>
    TargetElementalResistances: HashMap<Attributes.Element, float>
  }

  type FormulaFunction = CalculationContext -> DamageResult

  type FormulaDefinition = {
    Id: int<FormulaId>
    Name: string
    Calculate: FormulaFunction
  }

  [<Struct>]
  type ResourceCost = { Type: ResourceType; Amount: int }

  [<Struct>]
  type TargetType =
    | Self
    | SingleAlly
    | SingleEnemy
    | MultiTarget of int

  [<Struct>]
  type PassiveAbilityDefinition = {
    Id: int<AbilityId>
    Name: string
    Effects: int<EffectId>[]
    Requirements: AbilityRequirement[]
  }

  [<Struct>]
  type ActiveAbilityDefinition = {
    Id: int<AbilityId>
    Name: string
    Cooldown: int64<Tick>
    Cost: ResourceCost voption
    Targeting: TargetType
    FormulaId: int<FormulaId> voption
    Effects: int<EffectId>[]
    Requirements: AbilityRequirement[]
  }

  [<Struct>]
  type AbilityKind =
    | Passive of passive: PassiveAbilityDefinition
    | Active of active: ActiveAbilityDefinition

module AggregatedEffects =
  [<Struct>]
  type TickResult = { Damage: int; Healing: int }

module CharacterKits =
  [<Struct>]
  type CharacterKit = {
    Profession: Classification.Profession
    Name: string
    BaseStats: Attributes.BaseAttributes
    StarterAbilities: int<AbilityId>[]
  }

module Rules =
  [<Struct>]
  type ResolvedDamage = {
    Amount: int
    IsCritical: bool
    IsEvaded: bool
  }

  [<Struct>]
  type UseAbilityAction = {
    actor: Guid<EntityId>
    targets: Guid<EntityId>[]
    abilityId: int<AbilityId>
  }

  [<Struct>]
  type MoveAction = {
    actor: Guid<EntityId>
    destination: Position
  }

  [<Struct>]
  type Command =
    | UseAbility of abilityAction: UseAbilityAction
    | Move of moveAction: MoveAction


module Components =
  open Effects
  open Inventory

  type EntityComponents = {
    Factions: Classification.Faction HashSet
    Identity: Classification.Profession
    BaseStats: Attributes.BaseAttributes
    Resources: Attributes.Resources
    Position: Position
    Movement: Movement
    Effects: alist<ActiveEffect>
    Abilities: alist<int<AbilityId>>
    AbilityCooldowns: amap<int<AbilityId>, int64<Tick>>
    Equipment: HashMap<Slot, Equipment>
  }

module Services =
  open Abilities
  open Effects

  type IAbilityStore =
    abstract member tryFind: int<AbilityId> -> AbilityKind voption
    abstract member find: int<AbilityId> -> AbilityKind

  type IEffectStore =
    abstract member tryFind: int<EffectId> -> EffectDefinition voption
    abstract member find: int<EffectId> -> EffectDefinition

  type IFormulaStore =
    abstract member tryFind: int<FormulaId> -> FormulaDefinition voption
    abstract member find: int<FormulaId> -> FormulaDefinition

  type EngineServices = {
    abilityStore: IAbilityStore
    effectStore: IEffectStore
    formulaStore: IFormulaStore
    rng: unit -> float
  }

module State =
  open Components

  [<Struct>]
  type StateChange = {
    entities: HashMap<Guid<EntityId>, EntityComponents>
    gameTime: int64<Tick> voption
  }
