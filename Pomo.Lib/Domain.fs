namespace Pomo.Lib.Domain

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
    | Neutral

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
  type ShieldData = {
    CurrentHP: int
    MaxHP: int
    LastHitTime: int64<Tick>
    RegenDelay: int64<Tick>
    RegenRate: int
  }

  [<Struct>]
  type Resources = {
    HP: int
    MP: int
    Status: Status
    Shields: FSharp.Data.Adaptive.HashMap<int<EffectId>, ShieldData>
  }

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

module Effects =
  open FSharp.Data.Adaptive

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
  type EffectHook =
    | OnAbilityInvoke
    | OnDamageReceived
    | OnResourceChange
    | OnAbilityComplete
    | OnTick

  [<Struct>]
  type StatModifier =
    | Additive of addStat: Stat * adStatValue: int
    | Subtractive of subStat: Stat * subStatValue: int
    | Multiplicative of mulStat: Stat * mulStatValue: float
    | Divisive of divStat: Stat * divStatValue: float

  [<Struct>]
  type EffectModifier =
    | StaticMod of StatModifier
    | DynamicMod of formulaId: int<FormulaId>
    | AbilityDamageMod of abilityDamageValue: float
    | ResourceConversion of ResourceType * ResourceType * float
    | ShieldGeneration of shieldFormula: int<FormulaId>

  [<Struct>]
  type EffectDefinition = {
    Id: int<EffectId>
    Name: string
    Kind: EffectKind
    Stacking: StackingRule
    Duration: Duration
    Modifiers: IndexList<EffectModifier>
    Hooks: IndexList<EffectHook>
    FormulaId: int<FormulaId> voption
  }

  [<Struct>]
  type ActiveEffect = {
    EffectId: int<EffectId> // Corresponds to a definition
    SourceId: int<EntityId>
    RemainingTicks: int64<Tick>
    NextTickIn: int64<Tick>
    Stacks: int
    Definition: EffectDefinition
  }

module Abilities =
  open FSharp.Data.Adaptive

  [<Struct>]
  type DamageType =
    | Physical
    | Magical

  [<Struct>]
  type DamageResult = {
    BaseDamage: int
    ElementalDamage: int
    Element: Attributes.Element
    DamageType: DamageType
  }

  [<Struct>]
  type CalculationContext = {
    InvokerStats: Attributes.DerivedStats
    InvokerElementalAttributes: HashMap<Attributes.Element, float>
    TargetElementalResistances: HashMap<Attributes.Element, float>
  }

  type FormulaFunction = CalculationContext -> DamageResult

  [<Struct>]
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
    | MultiTarget of int // number of targets



  [<Struct>]
  type PassiveAbilityDefinition = {
    Id: int<AbilityId>
    Name: string
    Effects: IndexList<int<EffectId>>
    Requirements: IndexList<AbilityRequirement>
  }

  [<Struct>]
  type ActiveAbilityDefinition = {
    Id: int<AbilityId>
    Name: string
    Cooldown: int64<Tick>
    Cost: ResourceCost voption
    Targeting: TargetType
    FormulaId: int<FormulaId> voption
    Effects: IndexList<int<EffectId>>
    Requirements: IndexList<AbilityRequirement>
  }

  [<Struct>]
  type AbilityKind =
    | Passive of passive: PassiveAbilityDefinition
    | Active of active: ActiveAbilityDefinition

module AggregatedEffects =
  [<Struct>]
  type TickResult = { Damage: int; Healing: int }

module GameEvent =
  open FSharp.Data.Adaptive

  [<Struct>]
  type DamageAppliedEvent = { target: int<EntityId>; amount: int }

  [<Struct>]
  type HealedEvent = { target: int<EntityId>; amount: int }

  [<Struct>]
  type ResourceChangedEvent = {
    target: int<EntityId>
    resource: string
    newValue: int
  }

  [<Struct>]
  type EffectAppliedEvent = {
    target: int<EntityId>
    effectId: int<EffectId>
    source: int<EntityId>
  }

  [<Struct>]
  type EffectExpiredEvent = {
    target: int<EntityId>
    effectId: int<EffectId>
  }

  [<Struct>]
  type EntityDiedEvent = { entityId: int<EntityId> }

  [<Struct>]
  type EffectRealizationEvent = {
    actor: int<EntityId>
    targets: IndexList<int<EntityId>>
    abilityId: int<AbilityId>
    RealizedEffect: Effects.EffectKind
  }

  [<Struct>]
  type GameEvent =
    | DamageApplied of dmgAE: DamageAppliedEvent
    | Healed of healE: HealedEvent
    | ResourceChanged of resCE: ResourceChangedEvent
    | EffectApplied of effAE: EffectAppliedEvent
    | EffectExpired of effEE: EffectExpiredEvent
    | EntityDied of entDieDE: EntityDiedEvent
    | EffectRealization of effRealE: EffectRealizationEvent

module Rules =
  [<Struct>]
  type AbilityContext = {
    InvokerStats: Attributes.DerivedStats
    TargetStats: Attributes.DerivedStats
    AbilityResult: Abilities.DamageResult voption
    InvokerEffects: Effects.ActiveEffect FSharp.Data.Adaptive.alist
    TargetEffects: Effects.ActiveEffect FSharp.Data.Adaptive.alist
    GameTime: int64<Tick>
  }

  [<Struct>]
  type UseAbilityAction = {
    actor: int<EntityId>
    targets: FSharp.Data.Adaptive.IndexList<int<EntityId>>
    abilityId: int<AbilityId>
  }

  [<Struct>]
  type Command = UseAbility of action: UseAbilityAction


module Components =
  open FSharp.Data.Adaptive
  open Effects

  type All = {
    Identity: Classification.Profession
    BaseStats: Attributes.BaseAttributes
    Resources: Attributes.Resources
    Effects: alist<ActiveEffect>
    Abilities: alist<int<AbilityId>> // Abilities this entity possesses
    AbilityCooldowns: amap<int<AbilityId>, int64<Tick>> // Tracks when a cooldown is complete
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
  open FSharp.Data.Adaptive
  open Components
  open GameEvent

  [<Struct>]
  type StateChange = {
    entities: HashMap<int<EntityId>, All>
    events: IndexList<GameEvent>
    gameTime: int64<Tick> voption
  }
