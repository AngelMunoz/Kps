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
    | Earth
    | Water
    | Air
    | Light
    | Dark
    | Neutral

  [<Struct>]
  type BaseAttributes = {
    Strength: int
    Magic: int
    Sense: int
    Charm: int
  }

  [<Struct>]
  type DerivedStats = {
    // Strength derived stats
    AttackPower: int
    Accuracy: float
    Dexterity: int
    // Magic derived stats
    MagicPotential: int
    MagicAttack: int
    MagicDefense: int
    // Sense derived stats
    DetectAbility: int
    WillPower: int
    Luck: int
    // Charm derived stats
    HealthPoints: int
    DefensePotential: int
    Hevasion: float

    Resistances: FSharp.Data.Adaptive.HashMap<Element, float>
  }

  [<Struct>]
  type Status =
    | Alive
    | Dead
    | Disabled

  [<Struct>]
  type Resources = {
    HP: int
    MP: int
    Stamina: int
    Status: Status
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
  [<Struct>]
  type EffectKind =
    | Buff
    | Debuff
    | DamageOverTime of int
    | HealOverTime of int
    | Stun
    | Silence
    | Taunt
    | Shield of int

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

    member this.Duration =
      match this with
      | Instant -> ValueNone
      | Timed d -> ValueSome d
      | Loop(_, d) -> ValueSome d

    member this.Interval =
      match this with
      | Loop(i, _) -> ValueSome i
      | _ -> ValueNone

  [<Struct>]
  type Stat =
    | Strength
    | Magic
    | Sense
    | Charm
    | HealthPoints
    | MagicPotential
    | AttackPower
    | Accuracy
    | Dexterity
    | MagicAttack
    | MagicDefense
    | DetectAbility
    | WillPower
    | Luck
    | DefensePotential
    | Hevasion

  [<Struct>]
  type StatModifier =
    | Additive of addStat: Stat * adStatValue: int
    | Multiplicative of mulStat: Stat * mulStatValue: float

  [<Struct>]
  type EffectDefinition = {
    Id: int<EffectId>
    Name: string
    Kind: EffectKind
    Stacking: StackingRule
    Duration: Duration
    Modifiers: FSharp.Data.Adaptive.IndexList<StatModifier>
  }

  [<Struct>]
  type ActiveEffect = {
    EffectId: int<EffectId> // Corresponds to a definition
    SourceId: int<EntityId>
    RemainingTicks: int64<Tick>
    NextTickIn: int64<Tick>
    Stacks: int
  }

module Abilities =
  open Effects

  [<Struct>]
  type ResourceType =
    | HP
    | MP
    | Stamina

  [<Struct>]
  type ResourceCost = { Type: ResourceType; Amount: int }

  [<Struct>]
  type AbilityDefinition = {
    Id: int<AbilityId>
    Name: string
    Cooldown: int64<Tick>
    Cost: ResourceCost voption
    Effects: FSharp.Data.Adaptive.IndexList<int<EffectId>>
  }

module AggregatedEffects =
  [<Struct>]
  type TickResult = { Damage: int; Healing: int }

  let empty = { Damage = 0; Healing = 0 }

module GameEvent =
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
  type GameEvent =
    | DamageApplied of dmgAE: DamageAppliedEvent
    | Healed of healE: HealedEvent
    | ResourceChanged of resCE: ResourceChangedEvent
    | EffectApplied of effAE: EffectAppliedEvent
    | EffectExpired of effEE: EffectExpiredEvent
    | EntityDied of entDieDE: EntityDiedEvent

module Rules =
  [<Struct>]
  type MeleeAttackAction = {
    actor: int<EntityId>
    target: int<EntityId>
    abilityId: int<AbilityId>
  }

  [<Struct>]
  type CastSpellAction = {
    actor: int<EntityId>
    target: int<EntityId>
    abilityId: int<AbilityId>
  }

  [<Struct>]
  type Command =
    | MeleeAttack of mAction: MeleeAttackAction
    | CastSpell of cSpell: CastSpellAction


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
  open FSharp.Data.Adaptive
  open Abilities
  open Effects

  type IAbilityStore =
    abstract member tryFind: int<AbilityId> -> AbilityDefinition voption
    abstract member find: int<AbilityId> -> AbilityDefinition
    abstract member asList: FSharp.Data.Adaptive.IndexList<AbilityDefinition>
    abstract member asAList: alist<AbilityDefinition>
    abstract member asAMap: amap<int<AbilityId>, AbilityDefinition>

  type IEffectStore =
    abstract member tryFind: int<EffectId> -> EffectDefinition voption
    abstract member find: int<EffectId> -> EffectDefinition
    abstract member asList: FSharp.Data.Adaptive.IndexList<EffectDefinition>
    abstract member asAList: alist<EffectDefinition>
    abstract member asAMap: amap<int<EffectId>, EffectDefinition>

  type EngineServices = {
    abilityStore: IAbilityStore
    effectStore: IEffectStore
    rng: unit -> float
  }

module State =
  open FSharp.Data.Adaptive
  open Components
  open GameEvent

  [<Struct>]
  type StateChange = {
    entities: FSharp.Data.Adaptive.HashMap<int<EntityId>, All>
    events: FSharp.Data.Adaptive.IndexList<GameEvent>
    gameTime: int64<Tick> voption
  }
