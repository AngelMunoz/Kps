namespace Pomo.Lib.Domain

open System
open FSharp.Data.Adaptive
open FSharp.UMX

// All measure types defined at the top
[<Measure>]
type EntityId

[<Measure>]
type EffectId

[<Measure>]
type AbilityId

[<Measure>]
type FormulaId

[<Measure>]
type ObjectId

[<Measure>]
type ScenarioId

[<Measure>]
type BattleInstanceId

[<Measure>]
type ProjectileId

[<Measure>]
type AoeId

[<Measure>]
type ImpactId

[<Measure>]
type FloatingTextId

module Visuals =
  [<Struct>]
  type Shape =
    | Circle of float32
    | Square of float32

  [<Struct>]
  type VisualColor =
    | Red
    | Green
    | Blue
    | Yellow
    | White
    | Purple
    | Orange

  type ProjectileDefinition = {
    Id: int<ProjectileId>
    Name: string
    Shape: Shape
    Speed: float32
    Color: VisualColor
    Size: float32
  }

  type AoeDefinition = {
    Id: int<AoeId>
    Name: string
    Shape: Shape
    Radius: float32
    Color: VisualColor
  }

  type ImpactDefinition = {
    Id: int<ImpactId>
    Name: string
    Shape: Shape
    Duration: TimeSpan
    Color: VisualColor
    Size: float32
  }

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
type ScenarioBounds = {
  Width: float32
  Height: float32
  CenterX: float32
  CenterY: float32
}

[<Struct>]
type TerrainType =
  | Walkable
  | Blocked
  | Water
  | Hazard

[<Struct>]
type CollisionGeometry =
  | Circle of center: Position * radius: float32
  | Polygon of vertices: Position[]
  | NoCollision

[<Struct>]
type TerrainObject = {
  Id: Guid<ObjectId>
  Position: Position
  CollisionGeometry: CollisionGeometry
  TerrainType: TerrainType
  DepthLayer: float32
  SpriteId: string voption
}

[<Struct>]
type VisualLayer = {
  SpriteId: string
  Position: Position
  DepthLayer: float32
  Parallax: float32
}

[<Struct>]
type TransitionTrigger = {
  Position: Position
  Range: float32
  ToScenarioId: Guid<ScenarioId>
  ToPosition: Position
}

[<Struct>]
type TransitionState =
  | Inactive
  | Detected of targetScenario: Guid<ScenarioId> * targetPosition: Position
  | InProgress of
    targetScenario: Guid<ScenarioId> *
    targetPosition: Position *
    progress: float32
  | Completed of targetScenario: Guid<ScenarioId> * targetPosition: Position

[<Struct>]
type TeleportChange = {
  EntityId: Guid<EntityId>
  ToScenarioId: Guid<ScenarioId>
  ToPosition: Position
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

[<Struct>]
type ScenarioCombatType =
  | PvE
  | PvP
  | PvH

[<Measure>]
type PartyId

type Party = {
  Id: Guid<PartyId>
  Members: HashSet<Guid<EntityId>>
  Name: string
}

[<Measure>]
type PlayerId

[<Struct>]
type PlayerContext = {
  playerId: int<PlayerId>
  currentScenarioId: Guid<ScenarioId>
  controlledEntityId: Guid<EntityId>
}

[<Struct>]
type BattleContext = {
  IsActive: bool
  Participants: HashSet<Guid<EntityId>>
  StartTick: TimeSpan
  CanDisengage: bool
}

[<Struct>]
type AbilityIntent =
  | Offensive
  | Support
  | Neutral

[<Struct>]
type EngagementMode =
  | Peaceful
  | AlwaysOn
  | Structured

[<Struct>]
type BattleInstance = {
  Id: Guid<BattleInstanceId>
  Participants: HashSet<Guid<EntityId>>
  StartTick: TimeSpan
}

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
    ElementAttributes: HashMap<Element, float>
    ElementResistances: HashMap<Element, float>
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
    StatBonuses: ItemStatBonus[]
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
    | Timed of TimeSpan
    | Loop of TimeSpan * TimeSpan // Interval * Total Duration
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
    EffectId: int<EffectId>
    SourceId: Guid<EntityId>
    RemainingTicks: TimeSpan
    NextTickIn: TimeSpan
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
    Intent: AbilityIntent
    Effects: int<EffectId>[]
    Requirements: AbilityRequirement[]
  }

  [<Struct>]
  type ActiveAbilityDefinition = {
    Id: int<AbilityId>
    Name: string
    Intent: AbilityIntent
    Cooldown: TimeSpan
    Cost: ResourceCost voption
    Targeting: TargetType
    FormulaId: int<FormulaId> voption
    Effects: int<EffectId>[]
    Requirements: AbilityRequirement[]
    ProjectileId: int<ProjectileId> voption
    AoeId: int<AoeId> voption
    ImpactId: int<ImpactId> voption
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
    StarterAbilities: int<AbilityId> HashSet
  }

module Components =
  open Effects
  open Inventory

  type EntityComponents = {
    Identity: Classification.Profession
    BaseStats: Attributes.BaseAttributes
    Resources: Attributes.Resources
    Position: Position
    Movement: Movement
    AbilityCooldowns: HashMap<int<AbilityId>, TimeSpan>
    Effects: HashMap<int<EffectId>, ActiveEffect>
    Factions: Classification.Faction HashSet
    Abilities: int<AbilityId> HashSet
    Equipment: HashMap<Slot, Equipment>
    PartyId: Guid<PartyId> voption
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
  type DuelCommand =
    | Request of requester: Guid<EntityId> * target: Guid<EntityId>
    | PartyRequest of requester: Guid<PartyId> * target: Guid<PartyId>
    | Accept of accepter: Guid<EntityId> * requester: Guid<EntityId>
    | Cancel of canceller: Guid<EntityId> * otherPlayer: Guid<EntityId>

  [<Struct>]
  type Command =
    | UseAbility of abilityAction: UseAbilityAction
    | Move of moveAction: MoveAction
    | Duel of duelAction: DuelCommand
    | RemoveEntities of entityIds: Guid<EntityId> seq
    | AddEntities of HashMap<Guid<EntityId>, Components.EntityComponents>
    | Teleport of teleportChange: TeleportChange


module Services =
  open Abilities
  open Effects
  open Visuals

  type IAbilityStore =
    abstract member tryFind: int<AbilityId> -> AbilityKind voption
    abstract member find: int<AbilityId> -> AbilityKind

  type IEffectStore =
    abstract member tryFind: int<EffectId> -> EffectDefinition voption
    abstract member find: int<EffectId> -> EffectDefinition

  type IFormulaStore =
    abstract member tryFind: int<FormulaId> -> FormulaDefinition voption
    abstract member find: int<FormulaId> -> FormulaDefinition

  type IProjectileStore =
    abstract member tryFind:
      int<ProjectileId> -> ProjectileDefinition voption
    abstract member find: int<ProjectileId> -> ProjectileDefinition

  type IAoeStore =
    abstract member tryFind: int<AoeId> -> AoeDefinition voption
    abstract member find: int<AoeId> -> AoeDefinition

  type IImpactStore =
    abstract member tryFind: int<ImpactId> -> ImpactDefinition voption
    abstract member find: int<ImpactId> -> ImpactDefinition

  type EngineServices = {
    abilityStore: IAbilityStore
    effectStore: IEffectStore
    formulaStore: IFormulaStore
    projectileStore: IProjectileStore
    aoeStore: IAoeStore
    impactStore: IImpactStore
    rng: unit -> float
  }

module VisualEffects =
  open Visuals

  [<Struct>]
  type FloatingTextColor =
    | Damage
    | Heal
    | Critical
    | MPRecovery
    | Evade

  [<Struct>]
  type FloatingText = {
    Id: Guid<FloatingTextId>
    Text: string
    Position: Position
    Color: FloatingTextColor
    CreationTick: TimeSpan
  }

  [<Struct>]
  type ActiveProjectile = {
    Id: Guid<ProjectileId>
    DefinitionId: int<ProjectileId>
    StartPosition: Position
    EndPosition: Position
    Age: TimeSpan
  }

  [<Struct>]
  type ActiveAoe = {
    Id: Guid<AoeId>
    DefinitionId: int<AoeId>
    Position: Position
    CreationTick: TimeSpan
  }

  [<Struct>]
  type ActiveImpact = {
    Id: Guid<ImpactId>
    DefinitionId: int<ImpactId>
    Position: Position
    CreationTick: TimeSpan
  }

  [<Struct>]
  type VisualEffect =
    | FloatingText of text: FloatingText
    | Projectile of projectile: ActiveProjectile
    | Aoe of aoe: ActiveAoe
    | Impact of impact: ActiveImpact

module State =
  open Components
  open VisualEffects

  [<Struct>]
  type ScenarioChange =
    | AddBattleInstance of battleInstance: BattleInstance
    | UpdateBattleInstance of battleInstance: BattleInstance
    | RemoveBattleInstance of battleInstanceId: Guid<BattleInstanceId>
    | AddPendingDuel of requester: Guid<EntityId> * target: Guid<EntityId>
    | RemovePendingDuel of requester: Guid<EntityId>
    | AddPendingPartyDuel of requester: Guid<PartyId> * target: Guid<PartyId>
    | RemovePendingPartyDuel of requester: Guid<PartyId>

  [<Struct>]
  type VisualEffectChange =
    | AddFloatingText of addText: FloatingText
    | RemoveFloatingText of floatingTextId: Guid<FloatingTextId>
    | AddProjectile of addProjectile: ActiveProjectile
    | UpdateProjectile of updatedProjectile: ActiveProjectile
    | RemoveProjectile of projectileId: Guid<ProjectileId>
    | AddAoe of addAoe: ActiveAoe
    | RemoveAoe of aoeId: Guid<AoeId>
    | AddImpact of addImpact: ActiveImpact
    | RemoveImpact of impactId: Guid<ImpactId>

  [<Struct>]
  type StateChange = {
    updates: HashMap<Guid<EntityId>, EntityComponents>
    additions: HashMap<Guid<EntityId>, EntityComponents>
    removals: Guid<EntityId>[]
    gameTime: TimeSpan voption
    scenarioChanges: ScenarioChange[]
    teleports: TeleportChange[]
    visualEffects: VisualEffectChange[]
  }

