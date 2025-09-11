namespace Pomo.Lib.Domain

[<Measure>]
type ticks

module Primitives =
  type EntityId = EntityId of int
  type Ticks = int64<ticks>

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

  type Resistances = Map<Element, float>

  type DerivedStats = {
    MaxHP: int
    MaxMP: int
    AttackPower: int
    SpellPower: int
    Armor: int
    Evasion: float
    CritChance: float
    Resistances: Resistances
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
  open Primitives

  type EffectId = EffectId of int

  type EffectKind =
    | Buff
    | Debuff
    | DamageOverTime
    | HealOverTime
    | Stun
    | Silence
    | Taunt
    | Shield

  type StackingRule =
    | NoStack
    | RefreshDuration
    | AddStack of int // max stacks

  type Duration =
    | Instant
    | Timed of Ticks

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
    Id: EffectId
    Name: string
    Kind: EffectKind
    Stacking: StackingRule
    Duration: Duration
    Modifiers: StatModifier list
  }

  type ActiveEffect = {
    EffectId: EffectId // Corresponds to a definition
    SourceId: EntityId
    RemainingTicks: Ticks
    Stacks: int
  }

module Abilities =
  open Primitives
  open Effects

  type ResourceType =
    | HP
    | MP
    | Stamina

  type ResourceCost = { Type: ResourceType; Amount: int }

  type AbilityId = AbilityId of int

  type AbilityDefinition = {
    Id: AbilityId
    Name: string
    Cooldown: Ticks
    Cost: ResourceCost option
    Effects: Effects.EffectId list
  }

module GameEvent =
  type DamageAppliedEvent = {
    target: Primitives.EntityId
    amount: int
  }

  type HealedEvent = {
    target: Primitives.EntityId
    amount: int
  }

  type ResourceChangedEvent = {
    target: Primitives.EntityId
    resource: string
    newValue: int
  }

  type EffectAppliedEvent = {
    target: Primitives.EntityId
    effectId: Effects.EffectId
    source: Primitives.EntityId
  }

  type EffectExpiredEvent = {
    target: Primitives.EntityId
    effectId: Effects.EffectId
  }

  type EntityDiedEvent = { entityId: Primitives.EntityId }

  type GameEvent =
    | DamageApplied of DamageAppliedEvent
    | Healed of HealedEvent
    | ResourceChanged of ResourceChangedEvent
    | EffectApplied of EffectAppliedEvent
    | EffectExpired of EffectExpiredEvent
    | EntityDied of EntityDiedEvent
