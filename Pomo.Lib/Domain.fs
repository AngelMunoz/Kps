namespace Pomo.Lib.Domain

open FSharp.UMX

[<Measure>]
type Tick

[<Measure>]
type EntityId

[<Measure>]
type EffectId

[<Measure>]
type AbilityId

module Classification =
  type Faction =
    | Player
    | Enemy
    | Neutral

  type Tag =
    | Biological
    | Artificial
    | Undead

  type Family =
    | Strength
    | Magic
    | Charm
    | Sensory

  type Stage =
    | First
    | Second
    | Third

  type Profession = { Family: Family; Stage: Stage }

module Attributes =
  type Element =
    | Fire
    | Earth
    | Water
    | Air
    | Light
    | Dark
    | Neutral

  type BaseAttributes = {
    Strength: int
    Agility: int
    Intellect: int
    Vitality: int
    Willpower: int
    Luck: int
  }

  type DerivedStats = {
    MaxHP: int
    MaxMP: int
    AttackPower: int
    SpellPower: int
    Armor: int
    Evasion: float
    CritChance: float
    Resistances: Map<Element, float>
  }

  type Status =
    | Alive
    | Dead
    | Disabled

  type Resources = {
    HP: int
    MP: int
    Stamina: int
    Status: Status
  }

module Inventory =
  type Slot =
    | Head
    | Chest
    | Legs
    | Hands
    | Weapon1
    | Weapon2
    | Accessory

module Effects =
  type EffectKind =
    | Buff
    | Debuff
    | DamageOverTime of int
    | HealOverTime of int
    | Stun
    | Silence
    | Taunt
    | Shield of int

  type StackingRule =
    | NoStack
    | RefreshDuration
    | AddStack of int // max stacks

  type Duration =
    | Instant
    | Timed of int64<Tick>
    | Loop of int64<Tick> * int64<Tick> // Interval * Total Duration

  type Stat =
    | Strength
    | Agility
    | Intellect
    | Vitality
    | Willpower
    | Luck
    | MaxHP
    | MaxMP
    | AttackPower
    | SpellPower
    | Armor

  type StatModifier =
    | Additive of Stat * int
    | Multiplicative of Stat * float

  type EffectDefinition = {
    Id: int<EffectId>
    Name: string
    Kind: EffectKind
    Stacking: StackingRule
    Duration: Duration
    Modifiers: StatModifier list
  }

  type ActiveEffect = {
    EffectId: int<EffectId> // Corresponds to a definition
    SourceId: int<EntityId>
    RemainingTicks: int64<Tick>
    NextTickIn: int64<Tick>
    Stacks: int
  }

module Abilities =
  open Effects

  type ResourceType =
    | HP
    | MP
    | Stamina

  type ResourceCost = { Type: ResourceType; Amount: int }

  type AbilityDefinition = {
    Id: int<AbilityId>
    Name: string
    Cooldown: int64<Tick>
    Cost: ResourceCost option
    Effects: int<EffectId> list
  }

module AggregatedEffects =
  type TickResult = { Damage: int; Healing: int }

  let empty = { Damage = 0; Healing = 0 }

module GameEvent =
  type DamageAppliedEvent = { target: int<EntityId>; amount: int }

  type HealedEvent = { target: int<EntityId>; amount: int }

  type ResourceChangedEvent = {
    target: int<EntityId>
    resource: string
    newValue: int
  }

  type EffectAppliedEvent = {
    target: int<EntityId>
    effectId: int<EffectId>
    source: int<EntityId>
  }

  type EffectExpiredEvent = {
    target: int<EntityId>
    effectId: int<EffectId>
  }

  type EntityDiedEvent = { entityId: int<EntityId> }

  type GameEvent =
    | DamageApplied of DamageAppliedEvent
    | Healed of HealedEvent
    | ResourceChanged of ResourceChangedEvent
    | EffectApplied of EffectAppliedEvent
    | EffectExpired of EffectExpiredEvent
    | EntityDied of EntityDiedEvent

module Rules =
  type MeleeAttackAction = {
    actor: int<EntityId>
    target: int<EntityId>
    abilityId: int<AbilityId>
  }

  type CastSpellAction = {
    actor: int<EntityId>
    target: int<EntityId>
    abilityId: int<AbilityId>
  }

  type Command =
    | MeleeAttack of MeleeAttackAction
    | CastSpell of CastSpellAction


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
    abstract member Find: int<AbilityId> -> AbilityDefinition option

  type IEffectStore =
    abstract member Find: int<EffectId> -> EffectDefinition option

  type EngineServices = {
    abilityStore: IAbilityStore
    effectStore: IEffectStore
    rng: unit -> float
  }

module State =
  open FSharp.Data.Adaptive
  open Components
  open GameEvent

  type StateChange = {
    entities: HashMap<int<EntityId>, All>
    events: GameEvent IndexList
    gameTime: int64<Tick> voption
  }
