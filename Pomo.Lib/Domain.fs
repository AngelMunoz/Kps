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

[<Measure>]
type PendingResolutionId

[<Measure>]
type ActiveZoneId

[<Measure>]
type AudioClipId

[<Measure>]
type AudioEventId

[<Measure>]
type AiArchetypeId

[<Measure>]
type InventoryItemInstanceId

[<Struct>]
type Position = { X: float32; Y: float32 }

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

  [<Struct>]
  type ProjectileBehavior =
    | Seeker
    | Linear

  [<Struct>]
  type CollisionMode =
    | IgnoreTerrain
    | BlockedByTerrain

  type ProjectileDefinition = {
    Id: int<ProjectileId>
    Name: string
    Shape: Shape
    Speed: float32
    Color: VisualColor
    Size: float32
    Behavior: ProjectileBehavior
    CollisionMode: CollisionMode
    ImpactRadius: float32 voption
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

[<Struct>]
type ResolutionTarget =
  | EntityResolution of entityRes: Guid<EntityId>
  | PositionResolution of positionRes: Position

[<Struct>]
type PendingResolution = {
  Id: Guid<PendingResolutionId>
  ActorId: Guid<EntityId>
  Target: ResolutionTarget
  AbilityId: int<AbilityId>
  TriggerTick: TimeSpan
}

// Core types available at namespace level
[<Struct>]
type ResourceType =
  | HP
  | MP

[<Struct>]
type Movement = {
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
  | MovementSpeed // Movement Speed

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
    | AIControlled
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
    // Movement
    MovementSpeed: int

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

  type UsableItemDefinition = {
    InitialUsageCount: int
    AbilityId: int<AbilityId>
  }

  type EquipmentProperties = {
    Slot: Slot
    StatBonuses: ItemStatBonus[]
    ElementalAttributes: HashMap<Attributes.Element, float>
    ElementalResistances: HashMap<Attributes.Element, float>
  }

  type ItemKind =
    | Usable of UsableItemDefinition
    | Wearable of EquipmentProperties
    | NonUsable

  type ItemDefinition = {
    Id: int<ItemId>
    Name: string
    Description: string
    Weight: float32
    Rarity: Rarity
    Kind: ItemKind
  }

  type InventoryItem = {
    InstanceId: Guid<InventoryItemInstanceId>
    ItemId: int<ItemId>
    Name: string
    Weight: float32
    CurrentUsageCount: int voption
  }

module Effects =

  [<Struct>]
  type EffectKind =
    | Buff
    | Debuff
    | DamageOverTime
    | ResourceOverTime
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
    | PermanentLoop of TimeSpan // Interval
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
    | GroundArea of radius: float32
    | GroundPoint
    | AreaRandomTargets of radius: float32 * maxTargets: int
    | ChainTargets of maxChains: int * chainRange: float32
    | ConeTargets of angle: float32 * range: float32 * maxTargets: int
    | AreaRandomPoints of radius: float32 * numPoints: int

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
    Range: float32
    CastingTime: TimeSpan voption
    PreActivationVisualEffectIds: int<ImpactId>[]
    FormulaId: int<FormulaId> voption
    Effects: int<EffectId>[]
    Requirements: AbilityRequirement[]
    ProjectileIds: int<ProjectileId>[]
    AoeIds: int<AoeId>[]
    ImpactIds: int<ImpactId>[]
  }

  [<Struct>]
  type AbilityKind =
    | Passive of passive: PassiveAbilityDefinition
    | Active of active: ActiveAbilityDefinition

module AggregatedEffects =
  [<Struct>]
  type TickResult = {
    Damage: int
    Resources: Attributes.Resources
  }

module CharacterKits =
  [<Struct>]
  type CharacterKit = {
    Profession: Classification.Profession
    Name: string
    BaseStats: Attributes.BaseAttributes
    StarterAbilities: int<AbilityId> HashSet
  }

module AI =
  [<Struct>]
  type BehaviorType =
    | Passive
    | Aggressive
    | Defensive
    | Patrol
    | Turret
    | Ambusher
    | Supporter

  [<Struct>]
  type CueType =
    | Visual
    | Audio
    | Projectile
    | Tactile
    | Memory

  [<Struct>]
  type CueStrength =
    | Weak
    | Moderate
    | Strong
    | Overwhelming

  [<Struct>]
  type ResponseBehavior =
    | Investigate
    | Engage
    | Evade
    | Flee
    | Ignore

  [<Struct>]
  type CuePriority = {
    cueType: CueType
    minStrength: CueStrength
    priority: int
    response: ResponseBehavior
  }

  [<Struct>]
  type AIState =
    | Idle
    | Investigating of position: Position
    | Detecting of entityId: Guid<EntityId>
    | Pursuing of entityId: Guid<EntityId>
    | Engaging of entityId: Guid<EntityId>
    | Evading of projectileId: Guid<ProjectileId>
    | Retreating
    | Patrolling of waypointIndex: int

  [<Struct>]
  type StateCondition =
    | CueDetected of cueType: CueType * strength: CueStrength
    | HealthBelow of threshold: float32
    | TargetInRange of range: float32
    | TimeElapsed of ticks: TimeSpan
    | NoTargetsVisible
    | ReachedDestination

  [<Struct>]
  type StateTransition = {
    fromState: AIState
    condition: StateCondition
    toState: AIState
  }

  [<Struct>]
  type PerceptionConfig = {
    visualRange: float32
    audioSensitivity: float32
    memoryDuration: TimeSpan
    canDetectProjectiles: bool
  }

  type AIArchetype = {
    id: int<AiArchetypeId>
    name: string
    characterKit: CharacterKits.CharacterKit
    behaviorType: BehaviorType
    perceptionConfig: PerceptionConfig
    decisionInterval: TimeSpan
    cuePriorities: CuePriority[]
    patrolWaypoints: Position[] voption
  }

  type MemoryEntry = {
    entityId: Guid<EntityId>
    lastKnownPosition: Position
    confidence: float32
    lastSeenTick: TimeSpan
  }

  [<Struct>]
  type AIController = {
    controlledEntityId: Guid<EntityId>
    archetypeId: int<AiArchetypeId>
    currentState: AIState
    currentTarget: Guid<EntityId> voption
    lastDecisionTime: TimeSpan
    memories: HashMap<Guid<EntityId>, MemoryEntry>
    waypointIndex: int
    stateEnterTime: TimeSpan
    spawnPosition: Position
    absoluteWaypoints: Position[] voption
  }

  [<Struct>]
  type PerceptionCue = {
    cueType: CueType
    strength: CueStrength
    sourceEntityId: Guid<EntityId> voption
    position: Position
    timestamp: TimeSpan
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
    PartyId: Guid<PartyId> voption
    Inventory: HashMap<Guid<InventoryItemInstanceId>, InventoryItem>
    EquippedItems: HashMap<Slot, Guid<InventoryItemInstanceId>>
  }

module Rules =
  [<Struct>]
  type ResolvedDamage = {
    Amount: int
    IsCritical: bool
    IsEvaded: bool
  }

  [<Struct>]
  type AbilityTarget =
    | EntityTargets of targets: Guid<EntityId>[]
    | PositionTarget of position: Position

  [<Struct>]
  type ResolvedTarget =
    | Entity of entityId: Guid<EntityId>
    | Position of position: Position

  [<Struct>]
  type UseAbilityAction = {
    actor: Guid<EntityId>
    target: AbilityTarget
    abilityId: int<AbilityId>
  }

  [<Struct>]
  type NavigateAction = {
    actor: Guid<EntityId>
    destination: Position
  }

  [<Struct>]
  type AdvancePositionAction = {
    actor: Guid<EntityId>
    velocity: Position
    elapsed: float32
  }

  [<Struct>]
  type DuelCommand =
    | Request of requester: Guid<EntityId> * target: Guid<EntityId>
    | PartyRequest of requester: Guid<PartyId> * target: Guid<PartyId>
    | Accept of accepter: Guid<EntityId> * requester: Guid<EntityId>
    | Cancel of canceller: Guid<EntityId> * otherPlayer: Guid<EntityId>

  [<Struct>]
  type ResourceReplenishment = {
    Actor: Guid<EntityId>
    ResourceType: ResourceType
    Amount: int
  }

  [<Struct>]
  type SpawnEntityData = {
    components: Components.EntityComponents
    archetypeId: int<AiArchetypeId> voption
  }

  [<Struct>]
  type UseItemAction = {
    actor: Guid<EntityId>
    itemInstanceId: Guid<InventoryItemInstanceId>
  }

  [<Struct>]
  type EquipItemAction = {
    actor: Guid<EntityId>
    itemInstanceId: Guid<InventoryItemInstanceId>
    slot: Inventory.Slot
  }

  [<Struct>]
  type UnequipItemAction = {
    actor: Guid<EntityId>
    slot: Inventory.Slot
  }

  [<Struct>]
  type Command =
    | UseAbility of abilityAction: UseAbilityAction
    | Navigate of navigateAction: NavigateAction
    | AdvancePosition of advancePositionAction: AdvancePositionAction
    | Duel of duelAction: DuelCommand
    | RemoveEntities of entityIds: Guid<EntityId> seq
    | AddEntities of
      addEntities: HashMap<Guid<EntityId>, Components.EntityComponents>
    | AddEntitiesWithAI of
      addEntitiesWithAI: HashMap<Guid<EntityId>, SpawnEntityData>
    | Teleport of teleportChange: TeleportChange
    | ReplenishResources of replenishEntries: ResourceReplenishment[]
    | UseItem of useItemAction: UseItemAction
    | EquipItem of equipItemAction: EquipItemAction
    | UnequipItem of unequipItemAction: UnequipItemAction



module Audio =

  [<Struct>]
  type AudioCategory =
    | Ability
    | Impact
    | Ambient
    | UI
    | Movement
    | Music

  [<Struct>]
  type AudioClip = {
    Id: int<AudioClipId>
    Name: string
    ContentPath: string
    Category: AudioCategory
    Volume: float32
    Pitch: float32
    Loop: bool
  }

  [<Struct>]
  type SpatialInfo = {
    Position: Position
    MaxDistance: float32
    Rolloff: float32
  }


  [<Struct>]
  type AudioTrigger =
    | AbilityCast of abilityId: int<AbilityId>
    | AbilityImpact of abilityId: int<AbilityId>
    | ProjectileTravel of projectileId: int<ProjectileId>
    | EffectApplied of effectId: int<EffectId>
    | DamageTaken of isCritical: bool
    | MissedHit
    | EntityDeath
    | Movement of speed: float32
    | UIClick of elementName: string
    | AmbienLoop of scenarioId: Guid<ScenarioId>


  type AudioEvent = {
    Id: Guid<AudioEventId>
    ClipId: int<AudioClipId>
    Trigger: AudioTrigger
    SpatialInfo: SpatialInfo voption
    CreationTick: TimeSpan
    EntityId: Guid<EntityId> voption
  }

  [<Struct>]
  type AudioChange =
    | PlayAudio of audioEvent: AudioEvent
    | StopAudio of audioEventId: Guid<AudioEventId>
    | UpdateAudioPosition of
      audioEventId: Guid<AudioEventId> *
      position: Position


module VisualEffects =
  open Visuals

  [<Struct>]
  type FloatingTextColor =
    | Damage
    | Heal
    | Critical
    | MPRecovery
    | Evade
    | SystemMessage

  [<Struct>]
  type FloatingText = {
    Id: Guid<FloatingTextId>
    Text: string
    Position: Position
    Color: FloatingTextColor
    CreationTick: TimeSpan
  }

  [<Struct>]
  type ProjectileTarget =
    | EntityTarget of byEntity: Guid<EntityId>
    | PositionTarget of byPosition: Position

  [<Struct>]
  type ActiveProjectile = {
    Id: Guid<ProjectileId>
    DefinitionId: int<ProjectileId>
    CurrentPosition: Position
    Target: ProjectileTarget
    CreationTick: TimeSpan
    PendingResolutionId: Guid<PendingResolutionId> voption
  }

  [<Struct>]
  type ActiveAoe = {
    Id: Guid<AoeId>
    DefinitionId: int<AoeId>
    Position: Position
    CreationTick: TimeSpan
    PendingResolutionId: Guid<PendingResolutionId> voption
  }

  [<Struct>]
  type ActiveImpact = {
    Id: Guid<ImpactId>
    DefinitionId: int<ImpactId>
    Position: Position
    CreationTick: TimeSpan
    PendingResolutionId: Guid<PendingResolutionId> voption
  }

  [<Struct>]
  type ActiveZone = {
    Id: Guid<ActiveZoneId>
    Position: Position
    Shape: Visuals.Shape
    Radius: float32
    EndTime: TimeSpan
    EffectsToApply: int<EffectId>[]
    EntitiesInside: HashSet<Guid<EntityId>>
  }

  [<Struct>]
  type ActiveRush = {
    Id: Guid
    ActorId: Guid<EntityId>
    TargetId: Guid<EntityId>
    OnArrivalAbilityId: int<AbilityId>
    Speed: float32
    CreationTick: TimeSpan
  }

  [<Struct>]
  type ActiveDash = {
    Id: Guid
    ActorId: Guid<EntityId>
    Velocity: Position
    Duration: TimeSpan
    CreationTick: TimeSpan
  }

  [<Struct>]
  type ActiveObject =
    | Projectile of projectile: ActiveProjectile
    | Aoe of aoe: ActiveAoe
    | Impact of impact: ActiveImpact
    | FloatingText of floatingTxt:FloatingText
    | PendingResolution of pendingResolution: PendingResolution
    | Rush of rush: ActiveRush
    | Dash of dash: ActiveDash

  [<Struct>]
  type VisualEffect =
    | FloatingText of text: FloatingText
    | Projectile of projectile: ActiveProjectile
    | Aoe of aoe: ActiveAoe
    | Impact of impact: ActiveImpact

module Scenario =


  [<Struct>]
  type ScenarioTransition = {
    FromPosition: Position
    ToScenarioId: Guid<ScenarioId>
    ToPosition: Position
  }

  type Scenario = {
    Id: Guid<ScenarioId>
    Name: string
    BoundsWidth: float32
    BoundsHeight: float32
    BattleEnabled: bool
    CombatType: ScenarioCombatType
    EngagementMode: EngagementMode
    TerrainObjects: TerrainObject IndexList
    VisualLayers: VisualLayer[]
    Transitions: ScenarioTransition[]
  }

  type ScenarioState = {
    scenario: Scenario
    entities: cmap<Guid<EntityId>, Components.EntityComponents>
    gameTime: cval<TimeSpan>
    battleContext: BattleContext voption
    battleInstances: cmap<Guid<BattleInstanceId>, BattleInstance>
    pendingDuels: cmap<Guid<EntityId>, Guid<EntityId>>
    pendingPartyDuels: cmap<Guid<PartyId>, Guid<PartyId>>
    parties: cmap<Guid<PartyId>, Party>
    activeObjects: cmap<Guid, VisualEffects.ActiveObject>
    aiControllers: cmap<Guid<EntityId>, AI.AIController>
    activeZones: cmap<Guid<ActiveZoneId>, VisualEffects.ActiveZone>
  }

  type GameStateScenarios = {
    scenarios: cmap<Guid<ScenarioId>, ScenarioState>
    activeScenarioId: Guid<ScenarioId> cval
  }

  [<Struct>]
  type CreateScenarioParams = {
    Id: Guid<ScenarioId>
    Name: string
    BoundsWidth: float32
    BoundsHeight: float32
  }

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
    abstract member tryFind: int<ProjectileId> -> ProjectileDefinition voption
    abstract member find: int<ProjectileId> -> ProjectileDefinition

  type IAoeStore =
    abstract member tryFind: int<AoeId> -> AoeDefinition voption
    abstract member find: int<AoeId> -> AoeDefinition

  type IImpactStore =
    abstract member tryFind: int<ImpactId> -> ImpactDefinition voption
    abstract member find: int<ImpactId> -> ImpactDefinition

  type IAudioStore =
    abstract member tryFind: int<AudioClipId> -> Audio.AudioClip voption
    abstract member find: int<AudioClipId> -> Audio.AudioClip
    abstract member findByTrigger: Audio.AudioTrigger -> int<AudioClipId>[]

    abstract member findMusicForScenario:
      Guid<ScenarioId> -> int<AudioClipId> voption

  type IAIArchetypeStore =
    abstract member tryFind: int<AiArchetypeId> -> AI.AIArchetype voption
    abstract member find: int<AiArchetypeId> -> AI.AIArchetype

  type IItemStore =
    abstract member tryFind:
      int<Inventory.ItemId> -> Inventory.ItemDefinition voption

    abstract member find: int<Inventory.ItemId> -> Inventory.ItemDefinition

  type EngineServices = {
    abilityStore: IAbilityStore
    effectStore: IEffectStore
    formulaStore: IFormulaStore
    projectileStore: IProjectileStore
    aoeStore: IAoeStore
    impactStore: IImpactStore
    audioStore: IAudioStore
    aiArchetypeStore: IAIArchetypeStore
    itemStore: IItemStore
    rng: unit -> float
  }

module State =
  open Components
  open VisualEffects

  type GameState = {
    scenarios: cmap<Guid<ScenarioId>, Scenario.ScenarioState>
    activeScenarioId: Guid<ScenarioId> cval
    players: cmap<Guid<PlayerId>, PlayerContext>
    parties: cmap<Guid<PartyId>, Party>
    services: Services.EngineServices
  }

  [<Struct>]
  type ScenarioChange =
    | AddBattleInstance of battleInstance: BattleInstance
    | UpdateBattleInstance of battleInstance: BattleInstance
    | RemoveBattleInstance of battleInstanceId: Guid<BattleInstanceId>
    | AddPendingDuel of requester: Guid<EntityId> * target: Guid<EntityId>
    | RemovePendingDuel of requester: Guid<EntityId>
    | AddPendingPartyDuel of requester: Guid<PartyId> * target: Guid<PartyId>
    | RemovePendingPartyDuel of requester: Guid<PartyId>
    | AddActiveZone of zone: ActiveZone
    | UpdateActiveZone of zone: ActiveZone
    | RemoveActiveZone of zoneId: Guid<ActiveZoneId>

  [<Struct>]
  type VisualEffectChange =
    | AddObject of id: Guid * obj: ActiveObject
    | UpdateObject of id: Guid * obj: ActiveObject
    | RemoveObject of id: Guid

  [<Struct>]
  type StateChange = {
    updates: HashMap<Guid<EntityId>, Components.EntityComponents>
    additions: HashMap<Guid<EntityId>, Components.EntityComponents>
    removals: Guid<EntityId>[]
    gameTime: TimeSpan voption
    scenarioChanges: ScenarioChange[]
    teleports: TeleportChange[]
    visualEffects: VisualEffectChange[]
    audioChanges: Audio.AudioChange[]
    aiControllers: HashMap<Guid<EntityId>, AI.AIController>
  }
