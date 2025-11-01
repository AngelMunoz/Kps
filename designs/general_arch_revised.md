# Pomo Engine Architecture Design - ECS-like with FSharp.Data.Adaptive

**Version**: 2.0  
**Target**: `Pomo.Lib2` - A greenfield implementation  
**Goal**: Build a complete, performant game engine from scratch with all features from the current prototype, avoiding its architectural pitfalls and leveraging FSharp.Data.Adaptive correctly from day one.

## 1. Core Principles

### Lessons from Pomo.Lib Prototype

**What Worked:**
- ✅ DOP with immutable data structures
- ✅ FDA collections (cmap, cval) for state
- ✅ Adaptive drawing context
- ✅ Service boundaries (IAbilityStore, IEffectStore, etc.)
- ✅ Clear separation of data (Domain) and logic

**What Failed:**
- ❌ Multiple `AVal.force` calls per frame (3-50+)
- ❌ N transactions per frame (one per command)
- ❌ Monolithic `EntityComponents` record (changing position invalidates everything)
- ❌ No spatial indexing (O(n²) collision/perception queries)
- ❌ Adaptive projections recreated every frame instead of persistent
- ❌ Monolithic `tick` function doing too much
- ❌ No event system for inter-system communication
- ❌ Multiplayer bolted on as afterthought
- ❌ **Circular dependency**: DerivedStats needs Effects, Effects need DerivedStats (DynamicMod)

### Design Principles for Pomo.Lib2

1. **Single Transaction per Frame**: All state mutations batched and applied in one `transact` block.
2. **Data-Oriented & Modular**: Logic separated into independent **Systems** that operate on streams of data held in **Components**.
3. **Event-Driven Communication**: Systems decoupled via **Event Bus** - they listen for and publish semantic events.
4. **Persistent Adaptive Projections**: Systems maintain their own adaptive state (indexes, derived stats) that update incrementally.
5. **Spatial Partitioning**: Efficient spatial queries via adaptive spatial indexes for O(log n) collision/perception.
6. **Multiplayer-First**: Architecture manages multiple independent scenarios naturally (split-screen by design).
7. **Acyclic Dependencies**: No circular dependencies between systems - achieved through staged DerivedStats calculation.

## 2. Data Flow

**One-way data flow** ensures predictability and debuggability:

```
Input/AI → Publish Events → [EventBus] → Systems Subscribe → 
Systems Produce StateChanges → [Batch Apply in Single Transaction] → 
GameState → Adaptive Projections Update (DerivedStats, SpatialIndex, etc.)
```

## 3. Core Types (Pomo.Lib2/)

### 3.1 Event Bus (`Events.fs`)

For decoupled, semantic communication. Systems publish these to announce that something **has happened**.

```fsharp
[<Struct>]
type GameEvent =
    // User/AI Intent (replaces Command DU)
    | NavigateRequested of actor: Guid<EntityId> * destination: Position
    | AbilityUseRequested of actor: Guid<EntityId> * abilityId: int<AbilityId> * target: AbilityTarget
    | ItemUseRequested of actor: Guid<EntityId> * itemInstanceId: Guid<InventoryItemInstanceId>
    | ItemEquipRequested of actor: Guid<EntityId> * itemInstanceId: Guid<InventoryItemInstanceId> * slot: Slot
    | ItemUnequipRequested of actor: Guid<EntityId> * slot: Slot
    | DuelRequested of requester: Guid<EntityId> * target: Guid<EntityId>
    | PartyDuelRequested of requester: Guid<PartyId> * target: Guid<PartyId>
    
    // World State Events (result of actions)
    | DamageDealt of source: Guid<EntityId> * target: Guid<EntityId> * amount: int * isCritical: bool
    | EffectApplied of target: Guid<EntityId> * effectId: int<EffectId> * sourceId: Guid<EntityId>
    | EffectRemoved of target: Guid<EntityId> * effectId: int<EffectId>
    | CollisionOccurred of entityA: Guid<EntityId> * entityB: Guid<EntityId>
    | ProjectileHit of projectileId: Guid<ProjectileId> * target: Guid<EntityId>
    | EntityDied of Guid<EntityId>
    | AIStateTransitioned of entityId: Guid<EntityId> * from: AIState * to: AIState
    | ResourcesChanged of entityId: Guid<EntityId> * hp: int * mp: int
    | CooldownTriggered of entityId: Guid<EntityId> * abilityId: int<AbilityId> * readyTime: TimeSpan

type EventBus = {
    Events: clist<GameEvent>
    Subscribe: SystemId -> (GameEvent -> unit) -> unit
    Publish: GameEvent -> unit
    Clear: unit -> unit  // Called once per frame after processing
}
```

### 3.2 State Changes (`State.fs`)

For direct, batched data mutations. These are the **output** of systems that calculate new state.

```fsharp
[<Struct>]
type GameStateChange =
    // Entity Lifecycle
    | Entities_Added of HashMap<Guid<EntityId>, ComponentSet>
    | Entities_Removed of HashSet<Guid<EntityId>>
    
    // Batched Component Updates
    | Positions_Updated of HashMap<Guid<EntityId>, Position>
    | Resources_Updated of HashMap<Guid<EntityId>, Resources>
    | Cooldowns_Updated of HashMap<Guid<EntityId>, Cooldowns>
    | ActiveEffects_Updated of HashMap<Guid<EntityId>, ActiveEffects>
    | Inventories_Updated of HashMap<Guid<EntityId>, Inventory>
    | Equipment_Updated of HashMap<Guid<EntityId>, Equipment>
    | Abilities_Updated of HashMap<Guid<EntityId>, GrantedAbilities>
    | Movement_Updated of HashMap<Guid<EntityId>, Movement>
    | AIControllers_Updated of HashMap<Guid<EntityId>, AIController>
    | BaseAttributes_Updated of HashMap<Guid<EntityId>, BaseAttributes>
    
    // Visual & Audio Object Mutations
    | ActiveVisuals_Added of VisualObject[]
    | ActiveVisuals_Removed of Guid[]
    | AudioEvents_Triggered of AudioEvent[]
    
    // Scenario State Updates
    | BattleInstances_Updated of BattleInstanceChange[]
    | Duels_Updated of DuelChange[]
    | ActiveZones_Updated of ActiveZoneChange[]

// Helper type for entity creation
type ComponentSet = {
    Position: Position
    Resources: Resources
    BaseAttributes: BaseAttributes
    Movement: Movement voption
    Cooldowns: Cooldowns voption
    ActiveEffects: ActiveEffects voption
    Inventory: Inventory voption
    Equipment: Equipment voption
    GrantedAbilities: GrantedAbilities voption
    Factions: Factions
    PartyMembership: PartyMembership voption
    Profession: Profession
    AIController: AIController voption
}
```

### 3.3 Component-Based State (`Components.fs`)

**Core Design Decision**: Each component is a separate struct, stored in its own `cmap`. This enables:
- Fine-grained FDA invalidations (changing position doesn't invalidate inventory)
- Efficient system queries (systems only subscribe to components they care about)
- Better cache locality
- Entities are defined by which component maps contain their ID

**Why Not Monolithic Records?**
- Pomo.Lib's `EntityComponents` record bundles everything together
- Changing position invalidates derived stats, inventory, effects - everything!
- FDA can't track fine-grained dependencies
- Result: massive over-invalidation

```fsharp
// Base components (1:1 mapping from current EntityComponents)
[<Struct>] type Position = { X: float32; Y: float32 }

[<Struct>] type Resources = { HP: int; MP: int; Status: Status }

[<Struct>] type BaseAttributes = {
    Power: int
    Magic: int
    Sense: int
    Charm: int
}

[<Struct>] type Movement = {
    Destination: Position voption
    Path: Position list
}

[<Struct>] type Cooldowns = {
    Cooldowns: HashMap<int<AbilityId>, TimeSpan>
}

[<Struct>] type ActiveEffects = {
    Effects: HashMap<int<EffectId>, ActiveEffect>
}

[<Struct>] type Inventory = {
    Items: HashMap<Guid<InventoryItemInstanceId>, InventoryItem>
}

[<Struct>] type Equipment = {
    Slots: HashMap<Slot, Guid<InventoryItemInstanceId>>
}

[<Struct>] type GrantedAbilities = {
    Abilities: HashSet<int<AbilityId>>
}

[<Struct>] type Factions = {
    Factions: HashSet<Faction>
}

[<Struct>] type PartyMembership = {
    PartyId: Guid<PartyId> voption
}

[<Struct>] type Profession = {
    Identity: Classification.Profession
}

// AI component (unchanged from current implementation)
[<Struct>] type AIController = {
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

// Component storage in ScenarioState
type ComponentMaps = {
    positions: cmap<Guid<EntityId>, Position>
    resources: cmap<Guid<EntityId>, Resources>
    baseAttributes: cmap<Guid<EntityId>, BaseAttributes>
    movements: cmap<Guid<EntityId>, Movement>
    cooldowns: cmap<Guid<EntityId>, Cooldowns>
    activeEffects: cmap<Guid<EntityId>, ActiveEffects>
    inventories: cmap<Guid<EntityId>, Inventory>
    equipment: cmap<Guid<EntityId>, Equipment>
    grantedAbilities: cmap<Guid<EntityId>, GrantedAbilities>
    factions: cmap<Guid<EntityId>, Factions>
    partyMembership: cmap<Guid<EntityId>, PartyMembership>
    professions: cmap<Guid<EntityId>, Profession>
    aiControllers: cmap<Guid<EntityId>, AIController>
}

type ScenarioState = {
    scenario: Scenario
    components: ComponentMaps
    gameTime: cval<TimeSpan>
    battleInstances: cmap<Guid<BattleInstanceId>, BattleInstance>
    pendingDuels: cmap<Guid<EntityId>, Guid<EntityId>>
    pendingPartyDuels: cmap<Guid<PartyId>, Guid<PartyId>>
    parties: cmap<Guid<PartyId>, Party>
    activeObjects: cmap<Guid, VisualEffects.ActiveObject>
    activeZones: cmap<Guid<ActiveZoneId>, VisualEffects.ActiveZone>
}
```

### 3.4 System Interface (`Pipeline.fs`)

```fsharp
[<Struct>]
type SystemId = 
    | Time
    | DerivedStats     // NEW: Critical adaptive system
    | Effects
    | Movement
    | Collision
    | Combat
    | AI
    | Projectiles
    | Abilities
    | Inventory
    | Engagement
    | SpatialIndex
    | PathPreview
    | Transitions
    | Camera
    | Cleanup
    | Rendering
    | Audio

type SystemContext = {
    GameTime: TimeSpan
    DeltaTime: TimeSpan
    ScenarioId: Guid<ScenarioId>
    Services: EngineServices
    SpatialIndex: ISpatialIndex
    EventBus: EventBus
    DerivedStats: amap<Guid<EntityId>, DerivedStats>  // Shared adaptive projection (auto-updates)
}

type SystemOutput = {
    Changes: GameStateChange[]
    ActivateSystems: HashSet<SystemId>
    DeactivateSystems: HashSet<SystemId>
}

type ISystem =
    abstract member Id: SystemId
    abstract member Update: SystemContext -> ScenarioState -> SystemOutput
    abstract member Initialize: ScenarioState -> unit
    abstract member Cleanup: unit -> unit
```

## 4. System Pipeline

### 4.1 Execution Flow

```
Frame Start
  ↓
[PHASE 1: Simulation Tick]
  - Time System (increment game time)
  - Effects System (tick effects, expire effects)
  - Projectiles System (move projectiles, check impacts)
  - Visual Effects System (cleanup expired effects)
  - AI System (perception via SpatialIndex, decisions → events)
  ↓
[PHASE 2: Event Collection]
  - Input System (player input → events)
  - EventBus collects all events (AI + Player)
  ↓
[PHASE 3: Event Resolution]
  - Batch ALL events together
  - Abilities System (ability events → damage/effects/cooldowns)
  - Movement System (navigate events → position changes)
  - Inventory System (item events → inventory/equipment changes)
  - Engagement System (duel events → battle state changes)
  - Combat System (collision/damage events → HP/effect application)
  ↓
[PHASE 4: State Application]
  - **Single transact** for all changes
  - Spatial Index System (rebuild affected cells)
  ↓
[PHASE 5: Reactive Systems]
  - **DerivedStats System** (adaptive projection updates) ← CRITICAL
  - Path Preview System (adaptive path updates)
  - Transition System (check scenario transitions)
  - Camera System (follow entities)
  ↓
[PHASE 6: Rendering Prep]
  - Drawing Context (adaptive - only recomputes if state changed)
  ↓
Frame End
```

### 4.2 Single Transaction Design

**Pomo.Lib Mistake:**
```fsharp
// PomoGame.fs - processes commands in loop
for cmd in commandList do
    let change = CommandHandler.evaluate state cmd |> AVal.force  // N forces!
    GameState.apply state change  // N transactions!
// Result: With 10 commands, FDA recomputes derived stats 10 times
```

**Pomo.Lib2 Approach:**
```fsharp
// Collect all events from all systems
let allChanges = 
    eventList
    |> Pipeline.processEvents context state  // Returns aval<GameStateChange[]>
    |> AVal.force  // ONE force at the end

// Apply in SINGLE transaction
transact (fun () ->  
    GameState.applyBatch state allChanges
)
// Result: FDA recomputes derived stats ONCE, only for changed entities
```

**Performance Impact**: 10-100x improvement on frames with multiple commands

### 4.3 System Ordering

```fsharp
let DefaultPipeline = [|
    // Phase 1: Simulation (time-based updates)
    TimeSystem          
    EffectsSystem       // Modifies activeEffects component
    ProjectilesSystem   
    VisualEffectsSystem 
    AISystem            
    
    // Phase 2: Event Processing
    InputSystem         
    
    // Phase 3: Event Resolution (deterministic)
    AbilitiesSystem     
    MovementSystem      
    InventorySystem     // May modify equipment component
    EngagementSystem    
    CombatSystem        
    
    // Phase 4: Reactive Systems (triggered by state changes)
    SpatialIndexSystem  
    // NOTE: DerivedStatsProjection is NOT in pipeline - it's a passive adaptive projection
    // that auto-updates when baseAttributes, equipment, or activeEffects change
    PathPreviewSystem   
    TransitionSystem    
    CameraSystem        
|]
```

## 5. Critical System Implementations

### 5.1 DerivedStats System (`DerivedStats.fs`)

**Most Critical Adaptive System**

This system computes all derived stats (HP, MP, AP, AC, etc.) from base attributes + equipment + active effects. It **must** be an adaptive projection that incrementally updates when any input changes.

**Feature Requirements** (from Pomo.Lib prototype):
- Calculate 13 derived stats: AP, AC, DX, MP, MA, MD, WT, DA, LK, HP, DP, HV, MovementSpeed
- Aggregate stat bonuses from equipped items
- Apply effect modifiers (additive, subtractive, multiplicative, divisive)
- Support dynamic modifiers (formula-based)
- Handle effect stacking
- Compute elemental attributes and resistances

**Pomo.Lib Mistake:**
- `DerivedStats.byScenario` is adaptive (good!) but forced N times per frame
- Recomputes entire entity's stats when only one effect changes
- No caching between frames

**Pomo.Lib2 Design:**

```fsharp
// In Pomo.Lib2/Systems/DerivedStatsSystem.fs

type DerivedStatsSystem(services: EngineServices) =
    // Persistent adaptive projection (lives across frames)
    let mutable derivedStatsProjection: amap<Guid<EntityId>, DerivedStats> voption = ValueNone
    
    member _.Initialize(state: ScenarioState) =
        // Create adaptive projection once
        derivedStatsProjection <- ValueSome (
            state.components.positions.Keys  // All entities
            |> ASet.mapA (fun entityId ->
                // Adaptive computation per entity
                adaptive {
                    let! baseAttrs = state.components.baseAttributes |> AMap.find entityId
                    and! effects = 
                        state.components.activeEffects 
                        |> AMap.tryFind entityId 
                        |> AVal.map (ValueOption.defaultValue { Effects = HashMap.empty })
                    and! equipment =
                        state.components.equipment
                        |> AMap.tryFind entityId
                        |> AVal.map (ValueOption.defaultValue { Slots = HashMap.empty })
                    and! inventory =
                        state.components.inventories
                        |> AMap.tryFind entityId
                        |> AVal.map (ValueOption.defaultValue { Items = HashMap.empty })
                        
                    // Get wearable items from equipment + inventory
                    let wearableItems = 
                        getWearableItems services.itemStore equipment inventory
                        
                    // Apply modifiers (same logic as current Gameplay.fs)
                    let derived = 
                        applyModifiers 
                            services.effectStore 
                            services.formulaStore 
                            baseAttrs 
                            effects.Effects 
                            wearableItems
                            
                    return (entityId, derived)
                }
            )
            |> ASet.toAMap
        )
        
    member _.GetProjection() =
        match derivedStatsProjection with
        | ValueSome proj -> proj
        | ValueNone -> failwith "DerivedStatsSystem not initialized"
        
    member _.Update(ctx: SystemContext) (state: ScenarioState) =
        // This system doesn't produce state changes, it maintains adaptive projection
        // The projection updates automatically when components change
        {
            Changes = [||]
            ActivateSystems = HashSet.empty
            DeactivateSystems = HashSet.empty
        }
        
    interface ISystem with
        member this.Id = SystemId.DerivedStats
        member this.Update ctx state = this.Update ctx state
        member this.Initialize state = this.Initialize state
        member this.Cleanup() = derivedStatsProjection <- ValueNone

// Helper functions (implement stat calculation logic)
module private DerivedStatsHelpers =
    let inline addInt stat value =
        HashMap.alterV stat (fun existing ->
            match existing with
            | ValueSome e -> ValueSome(e + value)
            | ValueNone -> ValueSome value)
    
    let aggregateEquipment (equipment: HashMap<Slot, EquipmentProperties>) =
        // Aggregate stat bonuses, elemental attributes, and resistances from all equipped items
        let mutable statBonuses = HashMap.empty<Stat, int>
        let mutable elemAttr = HashMap.empty<Element, float>
        let mutable elemRes = HashMap.empty<Element, float>
        
        for _, item in equipment do
            for bonus in item.StatBonuses do
                statBonuses <- addInt bonus.Stat bonus.Value statBonuses
            for e, v in item.ElementalAttributes do
                elemAttr <- HashMap.alterV e (fun existing -> 
                    ValueSome(ValueOption.defaultValue 0.0 existing + v)) elemAttr
            for e, v in item.ElementalResistances do
                elemRes <- HashMap.alterV e (fun existing -> 
                    ValueSome(ValueOption.defaultValue 0.0 existing + v)) elemRes
        
        struct(statBonuses, elemAttr, elemRes)
    
    let applyModifiers 
        (effectStore: IEffectStore)
        (formulaStore: IFormulaStore)
        (baseStats: BaseAttributes)
        (effects: HashMap<int<EffectId>, ActiveEffect>)
        (equipment: HashMap<Slot, EquipmentProperties>)
        : DerivedStats =
        
        let struct(equipStatBonuses, equipElemAttr, equipElemRes) = aggregateEquipment equipment
        
        // Build modifier maps from active effects
        let mutable addMap = HashMap.empty<Stat, int>
        let mutable factorMap = HashMap.empty<Stat, float>
        let mutable dynamics = ResizeArray<struct(int<FormulaId> * Stat * int)>()
        
        for _, effect in effects do
            let stacks = effect.Stacks
            for modifier in effect.Definition.Modifiers do
                match modifier with
                | StaticMod sm ->
                    match sm with
                    | Additive(stat, v) -> addMap <- addInt stat (v * stacks) addMap
                    | Subtractive(stat, v) -> addMap <- addInt stat (-v * stacks) addMap
                    | Multiplicative(stat, v) ->
                        let stacked = if stacks > 1 then Math.Pow(v, float stacks) else v
                        factorMap <- HashMap.alterV stat (fun e -> 
                            ValueSome(ValueOption.defaultValue 1.0 e * stacked)) factorMap
                    | Divisive(stat, v) ->
                        let stacked = if stacks > 1 then Math.Pow(v, float stacks) else v
                        let inv = 1.0 / stacked
                        factorMap <- HashMap.alterV stat (fun e -> 
                            ValueSome(ValueOption.defaultValue 1.0 e * inv)) factorMap
                | DynamicMod(formulaId, stat) -> dynamics.Add(struct(formulaId, stat, stacks))
                | _ -> ()
        
        // Calculate base derived stats from base attributes
        let initial = {
            AP = baseStats.Power * 2
            AC = baseStats.Power + int(float baseStats.Power * 1.25)
            DX = baseStats.Power
            MP = baseStats.Magic * 5
            MA = baseStats.Magic * 2
            MD = baseStats.Magic + int(float baseStats.Magic * 1.25)
            WT = baseStats.Sense * 5
            DA = baseStats.Sense * 2
            LK = baseStats.Sense + int(float baseStats.Sense * 0.5)
            HP = baseStats.Charm * 10
            DP = baseStats.Charm + int(float baseStats.Charm * 1.25)
            HV = baseStats.Charm * 2
            MovementSpeed = 100
            ElementAttributes = equipElemAttr
            ElementResistances = equipElemRes
        }
        
        // Apply dynamic modifiers
        for struct(formulaId, stat, stacks) in dynamics do
            match formulaStore.tryFind formulaId with
            | ValueSome formula ->
                let ctx = {
                    InvokerStats = initial
                    InvokerElementalAttributes = initial.ElementAttributes
                    TargetElementalResistances = HashMap.empty
                }
                let value = formula.Calculate(ctx).BaseDamage
                addMap <- addInt stat (value * stacks) addMap
            | ValueNone -> ()
        
        // Apply all modifiers to stats
        let inline applyAll stat current =
            let addV = HashMap.tryFindV stat addMap |> ValueOption.defaultValue 0
            let equipV = HashMap.tryFindV stat equipStatBonuses |> ValueOption.defaultValue 0
            let factor = HashMap.tryFindV stat factorMap |> ValueOption.defaultValue 1.0
            int(float(current + addV + equipV) * factor)
        
        {
            initial with
                HP = applyAll HP initial.HP
                MP = applyAll MP initial.MP
                AP = applyAll AP initial.AP
                MA = applyAll MA initial.MA
                MD = applyAll MD initial.MD
                DA = applyAll DA initial.DA
                DX = applyAll DX initial.DX
                WT = applyAll WT initial.WT
                LK = applyAll LK initial.LK
                DP = applyAll DP initial.DP
                AC = applyAll AC initial.AC
                HV = applyAll HV initial.HV
                MovementSpeed = applyAll MovementSpeed initial.MovementSpeed
        }
    
    let getWearableItems 
        (itemStore: IItemStore) 
        (equipment: Equipment) 
        (inventory: Inventory) =
        equipment.Slots
        |> HashMap.chooseV (fun _ itemInstanceId ->
            inventory.Items
            |> HashMap.tryFindV itemInstanceId
            |> ValueOption.bind (fun item ->
                itemStore.tryFind item.ItemId
                |> ValueOption.bind (fun def ->
                    match def.Kind with
                    | Inventory.Wearable props -> ValueSome props
                    | _ -> ValueNone)))
```

**Key Benefits:**
- Computed **once** per entity per frame (or less if nothing changed)
- FDA automatically tracks dependencies (baseAttrs, effects, equipment, inventory)
- Only recomputes entities whose components changed
- Shared across all systems via `SystemContext.DerivedStats`
- **10-100x faster** than Pomo.Lib's approach

**Implementation Complexity**: Medium (3-4 days)
- Core stat calculation logic is straightforward
- FDA adaptive projection setup requires care
- Testing requires validating all modifier types work correctly

**Usage in Other Systems:**

```fsharp
// In AbilitiesSystem or CombatSystem
member _.Update(ctx: SystemContext) (state: ScenarioState) =
    // Use derived stats from context (already computed)
    let! actorStats = ctx.DerivedStats |> AMap.find actorId
    let! targetStats = ctx.DerivedStats |> AMap.find targetId
    
    // Calculate damage using derived stats
    let damage = calculateDamage actorStats targetStats ability
    ...
```

### 5.2 Effects System (`EffectsSystem.fs`)

**Feature Requirements** (from Pomo.Lib prototype):
- Tick all active effects (countdown timers)
- Expire effects when duration ends
- Handle effect stacking rules (NoStack, RefreshDuration, AddStack)
- Support timed, looping, and permanent effects
- Apply DoT/HoT damage on tick intervals
- Publish `EffectRemoved` events when effects expire

**Implementation Strategy:**

```fsharp
type EffectsSystem(services: EngineServices) =
    member _.Update(ctx: SystemContext) (state: ScenarioState) =
        adaptive {
            let! entities = state.components.activeEffects |> AMap.toAVal
            
            let mutable updated = HashMap.empty
            let mutable removals = ResizeArray()
            let mutable eventsToPublish = ResizeArray()
            
            for entityId, activeEffects in entities do
                let mutable newEffects = HashMap.empty
                
                for effectId, effect in activeEffects.Effects do
                    // Tick effect (same logic as EffectApplication.tickEffect)
                    let remainingTicks = effect.RemainingTicks - ctx.DeltaTime
                    
                    if remainingTicks <= TimeSpan.Zero then
                        // Effect expired
                        removals.Add(entityId, effectId)
                        eventsToPublish.Add(EffectRemoved(entityId, effectId))
                    else
                        // Update ticks and next tick time
                        let nextTickIn = effect.NextTickIn - ctx.DeltaTime
                        let updatedEffect = 
                            if nextTickIn <= TimeSpan.Zero then
                                // Trigger effect tick (e.g., DoT damage)
                                match effect.Definition.Duration with
                                | Loop(interval, _) | PermanentLoop interval ->
                                    // Apply effect (damage, healing, etc.)
                                    // This would publish DamageDealt or ResourcesChanged events
                                    { effect with 
                                        RemainingTicks = remainingTicks
                                        NextTickIn = interval }
                                | _ ->
                                    { effect with RemainingTicks = remainingTicks }
                            else
                                { effect with 
                                    RemainingTicks = remainingTicks
                                    NextTickIn = nextTickIn }
                        
                        newEffects <- HashMap.add effectId updatedEffect newEffects
                
                if not (HashMap.isEmpty newEffects) || removals.Count > 0 then
                    updated <- HashMap.add entityId { Effects = newEffects } updated
            
            // Publish events
            for event in eventsToPublish do
                ctx.EventBus.Publish event
            
            return {
                Changes = 
                    if HashMap.isEmpty updated then [||]
                    else [| ActiveEffects_Updated updated |]
                ActivateSystems = HashSet.empty
                DeactivateSystems = HashSet.empty
            }
        }
        |> AVal.force
        
    interface ISystem with
        member this.Id = SystemId.Effects
        member this.Update ctx state = this.Update ctx state
        member _.Initialize _ = ()
        member _.Cleanup() = ()
```

### 5.3 Abilities System (`AbilitiesSystem.fs`)

**Feature Requirements** (from Pomo.Lib prototype):
- Support active and passive abilities
- Validate ability usage (cooldown, resources, stun/silence, range, requirements)
- Handle 9 targeting types: Self, SingleAlly, SingleEnemy, GroundArea, GroundPoint, AreaRandomTargets, ChainTargets, ConeTargets, StraightLine
- Apply resource costs (HP/MP)
- Calculate damage using derived stats + formulas
- Apply effects to targets
- Trigger cooldowns
- Spawn projectiles/AoE/impacts
- Check faction targeting rules (offensive vs support)
- Validate ability requirements (stat thresholds, prerequisite abilities, formulas)

**Implementation Strategy:**

```fsharp
type AbilitiesSystem(services: EngineServices) =
    let mutable pendingEvents = ResizeArray<GameEvent>()
    
    member _.Initialize(state: ScenarioState) =
        // Subscribe to ability use events
        pendingEvents <- ResizeArray()
        
    member _.Update(ctx: SystemContext) (state: ScenarioState) =
        adaptive {
            let! gameTime = state.gameTime
            
            let mutable resourceUpdates = HashMap.empty
            let mutable cooldownUpdates = HashMap.empty
            let mutable effectUpdates = HashMap.empty
            let mutable visualsToAdd = ResizeArray()
            let mutable eventsToPublish = ResizeArray()
            
            // Process all AbilityUseRequested events
            for event in pendingEvents do
                match event with
                | AbilityUseRequested(actorId, abilityId, target) ->
                    // Validate ability use (same logic as CommandHandler.validateAction)
                    let! actor = state.components.positions |> AMap.tryFind actorId
                    
                    match actor with
                    | ValueSome actorPos ->
                        let! actorResources = state.components.resources |> AMap.find actorId
                        let! actorCooldowns = state.components.cooldowns |> AMap.tryFind actorId
                        let! actorEffects = state.components.activeEffects |> AMap.tryFind actorId
                        let! actorAbilities = state.components.grantedAbilities |> AMap.find actorId
                        
                        // Check if actor has ability
                        if HashSet.contains abilityId actorAbilities.Abilities then
                            match services.abilityStore.tryFind abilityId with
                            | ValueSome abilityKind ->
                                match abilityKind with
                                | Active abilityDef ->
                                    // Validation (same as CommandHandler)
                                    let isStunned = checkStun actorEffects
                                    let isSilenced = checkSilence actorEffects abilityDef
                                    let onCooldown = checkCooldown actorCooldowns abilityId gameTime
                                    let hasResources = checkResourceCost actorResources abilityDef
                                    
                                    if not isStunned && not isSilenced && not onCooldown && hasResources then
                                        // Apply resource cost
                                        match abilityDef.Cost with
                                        | ValueSome cost ->
                                            let newResources = subtractCost actorResources cost
                                            resourceUpdates <- HashMap.add actorId newResources resourceUpdates
                                        | ValueNone -> ()
                                        
                                        // Trigger cooldown
                                        let readyTime = gameTime + abilityDef.Cooldown
                                        let newCooldowns = updateCooldown actorCooldowns abilityId readyTime
                                        cooldownUpdates <- HashMap.add actorId newCooldowns cooldownUpdates
                                        eventsToPublish.Add(CooldownTriggered(actorId, abilityId, readyTime))
                                        
                                        // Resolve targets (same as CommandHandler.TargetResolution)
                                        let! resolvedTargets = resolveTargets state actorPos target abilityDef.Targeting
                                        
                                        // Calculate damage/effects for each target
                                        for targetId in resolvedTargets do
                                            let! actorStats = ctx.DerivedStats |> AMap.find actorId
                                            let! targetStats = ctx.DerivedStats |> AMap.find targetId
                                            
                                            // Calculate damage using formula
                                            match abilityDef.FormulaId with
                                            | ValueSome formulaId ->
                                                match services.formulaStore.tryFind formulaId with
                                                | ValueSome formula ->
                                                    let calcCtx = {
                                                        InvokerStats = actorStats
                                                        InvokerElementalAttributes = actorStats.ElementAttributes
                                                        TargetElementalResistances = targetStats.ElementResistances
                                                    }
                                                    let damageResult = formula.Calculate calcCtx
                                                    
                                                    // Apply damage (publish event for CombatSystem to handle)
                                                    eventsToPublish.Add(
                                                        DamageDealt(actorId, targetId, damageResult.BaseDamage, false)
                                                    )
                                                | ValueNone -> ()
                                            | ValueNone -> ()
                                            
                                            // Apply effects to target
                                            for effectId in abilityDef.Effects do
                                                match services.effectStore.tryFind effectId with
                                                | ValueSome effectDef ->
                                                    let activeEffect = createActiveEffect effectDef actorId gameTime
                                                    eventsToPublish.Add(EffectApplied(targetId, effectId, actorId))
                                                    
                                                    // Add to effect updates
                                                    effectUpdates <- addEffectToEntity targetId activeEffect effectUpdates
                                                | ValueNone -> ()
                                        
                                        // Spawn projectiles
                                        for projectileId in abilityDef.ProjectileIds do
                                            let projectile = createProjectile projectileId actorPos target
                                            visualsToAdd.Add(ActiveObject.Projectile projectile)
                                            
                                | Passive _ -> ()  // Passives don't get used via events
                            | ValueNone -> ()
                    | ValueNone -> ()
                | _ -> ()
            
            // Clear processed events
            pendingEvents.Clear()
            
            // Publish generated events
            for event in eventsToPublish do
                ctx.EventBus.Publish event
            
            return {
                Changes = [|
                    if not (HashMap.isEmpty resourceUpdates) then
                        Resources_Updated resourceUpdates
                    if not (HashMap.isEmpty cooldownUpdates) then
                        Cooldowns_Updated cooldownUpdates
                    if not (HashMap.isEmpty effectUpdates) then
                        ActiveEffects_Updated effectUpdates
                    if visualsToAdd.Count > 0 then
                        ActiveVisuals_Added (visualsToAdd.ToArray())
                |]
                ActivateSystems = HashSet.empty
                DeactivateSystems = HashSet.empty
            }
        }
        |> AVal.force
        
    // Event handler (called by EventBus)
    member this.OnEvent(event: GameEvent) =
        match event with
        | AbilityUseRequested _ -> pendingEvents.Add event
        | _ -> ()
        
    interface ISystem with
        member this.Id = SystemId.Abilities
        member this.Update ctx state = this.Update ctx state
        member _.Initialize _ = ()
        member _.Cleanup() = pendingEvents.Clear()
```

### 5.4 Movement System (`MovementSystem.fs`)

**Feature Requirements** (from Pomo.Lib prototype):
- Process navigation requests (set destination)
- Calculate paths using A* pathfinding
- Move entities along paths based on MovementSpeed
- Handle scenario bounds (clamp positions)
- Update movement component when destination reached
- Trigger spatial index updates when positions change

**Implementation Strategy:**

```fsharp
type MovementSystem() =
    let mutable navigationEvents = ResizeArray<struct(Guid<EntityId> * Position)>()
    
    member _.Update(ctx: SystemContext) (state: ScenarioState) =
        adaptive {
            let! bounds = state.scenario |> AVal.map (fun s -> 
                { Width = s.BoundsWidth; Height = s.BoundsHeight })
            
            let mutable positionUpdates = HashMap.empty
            let mutable movementUpdates = HashMap.empty
            
            // Process navigation events
            for struct(entityId, destination) in navigationEvents do
                // Calculate path (same as current Movement.fs)
                let! currentPos = state.components.positions |> AMap.find entityId
                let path = calculatePath currentPos destination state.scenario
                
                movementUpdates <- HashMap.add entityId 
                    { Destination = ValueSome destination; Path = path } 
                    movementUpdates
            
            navigationEvents.Clear()
            
            // Move entities with active paths
            let! movingEntities = 
                state.components.movements 
                |> AMap.toAVal
                |> AVal.map (HashMap.filter (fun _ m -> not (List.isEmpty m.Path)))
            
            for entityId, movement in movingEntities do
                let! currentPos = state.components.positions |> AMap.find entityId
                let! derivedStats = ctx.DerivedStats |> AMap.find entityId
                
                // Calculate new position (same logic as current Movement.fs)
                let newPos = advanceAlongPath currentPos movement.Path derivedStats.MovementSpeed ctx.DeltaTime
                let newPath = updatePath movement.Path newPos
                
                positionUpdates <- HashMap.add entityId newPos positionUpdates
                
                if List.isEmpty newPath then
                    // Reached destination
                    movementUpdates <- HashMap.add entityId 
                        { Destination = ValueNone; Path = [] } 
                        movementUpdates
                else
                    movementUpdates <- HashMap.add entityId 
                        { movement with Path = newPath } 
                        movementUpdates
            
            return {
                Changes = [|
                    if not (HashMap.isEmpty positionUpdates) then
                        Positions_Updated positionUpdates
                    if not (HashMap.isEmpty movementUpdates) then
                        Movement_Updated movementUpdates
                |]
                ActivateSystems = 
                    if HashMap.isEmpty positionUpdates then HashSet.empty
                    else HashSet.ofList [SystemId.SpatialIndex; SystemId.Collision]
                DeactivateSystems = HashSet.empty
            }
        }
        |> AVal.force
        
    member this.OnEvent(event: GameEvent) =
        match event with
        | NavigateRequested(actorId, destination) ->
            navigationEvents.Add(struct(actorId, destination))
        | _ -> ()
        
    interface ISystem with
        member this.Id = SystemId.Movement
        member this.Update ctx state = this.Update ctx state
        member _.Initialize _ = ()
        member _.Cleanup() = navigationEvents.Clear()
```

### 5.5 Spatial Index System (`SpatialIndexSystem.fs`)

**Purpose**: Provide O(log n) spatial queries for collision detection and AI perception

**Why This is Critical:**
- Pomo.Lib likely iterates all entities for collision/perception (O(n²))
- With 100 entities: 10,000 checks per frame
- With spatial indexing: ~100 checks per frame (100x improvement)

**Implementation Strategy:**

```fsharp
type AdaptiveSpatialIndex(cellSize: float32) =
    let cells: cmap<struct(int * int), cset<Guid<EntityId>>> = cmap.empty
    
    let getCellCoords (pos: Position) =
        struct(int(pos.X / cellSize), int(pos.Y / cellSize))
    
    member _.Insert(entityId: Guid<EntityId>, pos: Position) =
        transact (fun () ->
            let cell = getCellCoords pos
            match cells.TryGetValue(cell) with
            | true, set -> set.Add entityId |> ignore
            | false, _ -> 
                let newSet = cset [entityId]
                cells.Add(cell, newSet) |> ignore
        )
    
    member _.Update(entityId: Guid<EntityId>, oldPos: Position, newPos: Position) =
        let oldCell = getCellCoords oldPos
        let newCell = getCellCoords newPos
        
        if oldCell <> newCell then
            transact (fun () ->
                // Remove from old cell
                match cells.TryGetValue(oldCell) with
                | true, set -> set.Remove entityId |> ignore
                | false, _ -> ()
                
                // Add to new cell
                match cells.TryGetValue(newCell) with
                | true, set -> set.Add entityId |> ignore
                | false, _ -> 
                    let newSet = cset [entityId]
                    cells.Add(newCell, newSet) |> ignore
            )
    
    member _.QueryRadius(pos: Position, radius: float32) : aset<Guid<EntityId>> =
        adaptive {
            let centerCell = getCellCoords pos
            let cellRadius = int(radius / cellSize) + 1
            
            let mutable result = ASet.empty
            
            for x in -cellRadius .. cellRadius do
                for y in -cellRadius .. cellRadius do
                    let cell = struct(fst centerCell + x, snd centerCell + y)
                    match cells.TryGetValue(cell) with
                    | true, entitiesInCell ->
                        result <- ASet.union result (entitiesInCell |> ASet.ofCSet)
                    | false, _ -> ()
            
            return result
        }
        |> ASet.ofAVal
        |> ASet.flatMap id
    
    interface ISpatialIndex with
        member this.Insert id pos = this.Insert(id, pos)
        member this.Update id oldPos newPos = this.Update(id, oldPos, newPos)
        member this.QueryRadius pos radius = this.QueryRadius(pos, radius)
        member _.Remove _ = ()  // Implement if needed
        member _.QueryRect _ = ASet.empty  // Implement if needed
        member _.QueryNearest _ _ = [||]  // Implement if needed

type SpatialIndexSystem(spatialIndex: AdaptiveSpatialIndex) =
    member _.Update(ctx: SystemContext) (state: ScenarioState) =
        // This system updates the spatial index when positions change
        // It doesn't produce state changes, just maintains the index
        {
            Changes = [||]
            ActivateSystems = HashSet.empty
            DeactivateSystems = HashSet.empty
        }
        
    interface ISystem with
        member this.Id = SystemId.SpatialIndex
        member this.Update ctx state = this.Update ctx state
        member _.Initialize _ = ()
        member _.Cleanup() = ()
```

## 6. Complete System Breakdown

This table shows **every** system and the features it implements from Pomo.Lib:

| System | Features from Pomo.Lib | Components Read | Components Write | Events Subscribe | Events Publish |
|--------|-------------------------|-----------------|------------------|------------------|----------------|
| **TimeSystem** | Game time tracking | gameTime | gameTime | - | - |
| **DerivedStatsSystem** | Stat calculation (13 derived stats from base attrs + equipment + effects) | baseAttributes, activeEffects, equipment, inventories | - (maintains adaptive projection) | - | - |
| **EffectsSystem** | Effect ticking, expiration, stacking, DoT/HoT | activeEffects | activeEffects | - | EffectRemoved, DamageDealt (DoT) |
| **ProjectilesSystem** | Projectile movement, seeker behavior, collision | activeObjects (projectiles), positions | activeObjects | - | ProjectileHit, CollisionOccurred |
| **VisualEffectsSystem** | Cleanup expired visuals (impacts, AoEs, floating text) | activeObjects (impacts, aoes, floatingTexts) | activeObjects | - | - |
| **AISystem** | AI perception, decision making, state machines, memory | aiControllers, positions, resources, factions | aiControllers | DamageDealt, EntityDied | NavigateRequested, AbilityUseRequested, AIStateTransitioned |
| **InputSystem** | Player input to game events | - | - | - | NavigateRequested, AbilityUseRequested, ItemUseRequested |
| **AbilitiesSystem** | Ability validation, 9 targeting types, damage calc, cooldowns | positions, resources, cooldowns, activeEffects, grantedAbilities, factions | resources, cooldowns, activeEffects, activeObjects | AbilityUseRequested | DamageDealt, EffectApplied, CooldownTriggered |
| **MovementSystem** | Pathfinding (A*), movement along paths, bounds checking | positions, movements | positions, movements | NavigateRequested | - |
| **InventorySystem** | Item usage, equipment, consumables, weight management | inventories, equipment | inventories, equipment | ItemUseRequested, ItemEquipRequested, ItemUnequipRequested | AbilityUseRequested (consumables), EffectApplied (equipment) |
| **EngagementSystem** | Duel system, party duels, battle instances, engagement rules | factions, partyMembership | battleInstances, pendingDuels | DuelRequested, PartyDuelRequested | - |
| **CombatSystem** | Damage application, death detection, elemental damage | resources, activeEffects, factions | resources, activeEffects | DamageDealt, CollisionOccurred | EntityDied, ResourcesChanged |
| **CollisionSystem** | Spatial collision detection, terrain collision | positions (via SpatialIndex) | - | - | CollisionOccurred |
| **SpatialIndexSystem** | O(log n) spatial queries (NEW: addresses O(n²) perf issue) | positions | - (maintains index) | - | - |
| **PathPreviewSystem** | Visual path display for player navigation | positions, movements, scenario terrain | - (maintains adaptive path) | - | - |
| **TransitionSystem** | Scenario transitions, teleportation | positions, scenario.Transitions | - | - | TeleportRequested |
| **CameraSystem** | Camera follow, zoom, viewport | positions | - (updates camera state) | - | - |

**Total**: 16 systems, each with clear responsibilities and boundaries

## 7. State Application (Batch Processing)

### 7.1 The Problem We're Avoiding

**Pomo.Lib's Mistake:**
```fsharp
// PomoGame.fs - processes commands in loop
for cmd in commandList do
    let stateChange = CommandHandler.evaluate state cmd |> AVal.force  // Force N times!
    GameState.apply state stateChange  // N separate transactions!
```

**Impact**: 
- With 10 commands, FDA recomputes derived stats **10 times**
- Drawing context recomputed **10 times**
- Spatial queries invalidated **10 times**
- Frame time dominated by unnecessary recomputation

### 7.2 Pomo.Lib2 Design

```fsharp
// Pomo.Lib2/Pipeline.fs

let applyBatch (state: ScenarioState) (changes: GameStateChange[]) : unit =
    transact (fun () ->
        // Group changes by type for efficient processing
        let positionChanges = ResizeArray()
        let resourceChanges = ResizeArray()
        let effectChanges = ResizeArray()
        let cooldownChanges = ResizeArray()
        let inventoryChanges = ResizeArray()
        let equipmentChanges = ResizeArray()
        let additions = ResizeArray()
        let removals = ResizeArray()
        
        // First pass: collect changes by type
        for change in changes do
            match change with
            | Positions_Updated updates -> positionChanges.Add(updates)
            | Resources_Updated updates -> resourceChanges.Add(updates)
            | ActiveEffects_Updated updates -> effectChanges.Add(updates)
            | Cooldowns_Updated updates -> cooldownChanges.Add(updates)
            | Inventories_Updated updates -> inventoryChanges.Add(updates)
            | Equipment_Updated updates -> equipmentChanges.Add(updates)
            | Entities_Added entities -> additions.Add(entities)
            | Entities_Removed entityIds -> removals.Add(entityIds)
            | _ -> ()  // Other change types
            
        // Second pass: apply in optimal order
        
        // 1. Removals first
        for entityIds in removals do
            for entityId in entityIds do
                state.components.positions.Remove(entityId) |> ignore
                state.components.resources.Remove(entityId) |> ignore
                state.components.baseAttributes.Remove(entityId) |> ignore
                state.components.movements.Remove(entityId) |> ignore
                state.components.cooldowns.Remove(entityId) |> ignore
                state.components.activeEffects.Remove(entityId) |> ignore
                state.components.inventories.Remove(entityId) |> ignore
                state.components.equipment.Remove(entityId) |> ignore
                state.components.grantedAbilities.Remove(entityId) |> ignore
                state.components.factions.Remove(entityId) |> ignore
                state.components.partyMembership.Remove(entityId) |> ignore
                state.components.professions.Remove(entityId) |> ignore
                state.components.aiControllers.Remove(entityId) |> ignore
                
        // 2. Additions
        for entities in additions do
            for entityId, componentSet in entities do
                state.components.positions.Add(entityId, componentSet.Position) |> ignore
                state.components.resources.Add(entityId, componentSet.Resources) |> ignore
                state.components.baseAttributes.Add(entityId, componentSet.BaseAttributes) |> ignore
                state.components.factions.Add(entityId, componentSet.Factions) |> ignore
                state.components.professions.Add(entityId, componentSet.Profession) |> ignore
                
                componentSet.Movement |> ValueOption.iter (fun m ->
                    state.components.movements.Add(entityId, m) |> ignore)
                componentSet.Cooldowns |> ValueOption.iter (fun c ->
                    state.components.cooldowns.Add(entityId, c) |> ignore)
                componentSet.ActiveEffects |> ValueOption.iter (fun e ->
                    state.components.activeEffects.Add(entityId, e) |> ignore)
                componentSet.Inventory |> ValueOption.iter (fun i ->
                    state.components.inventories.Add(entityId, i) |> ignore)
                componentSet.Equipment |> ValueOption.iter (fun e ->
                    state.components.equipment.Add(entityId, e) |> ignore)
                componentSet.GrantedAbilities |> ValueOption.iter (fun a ->
                    state.components.grantedAbilities.Add(entityId, a) |> ignore)
                componentSet.PartyMembership |> ValueOption.iter (fun p ->
                    state.components.partyMembership.Add(entityId, p) |> ignore)
                componentSet.AIController |> ValueOption.iter (fun ai ->
                    state.components.aiControllers.Add(entityId, ai) |> ignore)
                    
        // 3. Updates
        for positions in positionChanges do
            for entityId, newPos in positions do
                state.components.positions.[entityId] <- newPos
                
        for resources in resourceChanges do
            for entityId, newRes in resources do
                state.components.resources.[entityId] <- newRes
                
        for effects in effectChanges do
            for entityId, newEffects in effects do
                state.components.activeEffects.[entityId] <- newEffects
                
        for cooldowns in cooldownChanges do
            for entityId, newCooldowns in cooldowns do
                state.components.cooldowns.[entityId] <- newCooldowns
                
        for inventories in inventoryChanges do
            for entityId, newInventory in inventories do
                state.components.inventories.[entityId] <- newInventory
                
        for equipment in equipmentChanges do
            for entityId, newEquipment in equipment do
                state.components.equipment.[entityId] <- newEquipment
    )
```

**Impact**:
- **Single transaction** per frame
- FDA recomputes derived stats **once** (only for changed entities)
- Drawing context recomputed **once**
- **10-100x performance improvement**

## 8. Integration with Existing Code

### 8.1 PomoGame.fs Update Function (New)

```fsharp
override this.Update gameTime =
    match gameState with
    | ValueNone -> ()
    | ValueSome state ->
      let elapsed = gameTime.ElapsedGameTime
      
      // PHASE 1: Run System Pipeline
      let stateChanges = Pipeline.run state elapsed
      
      // PHASE 2: Apply all changes in single transaction
      Pipeline.applyBatch state stateChanges
      
      // PHASE 3: Update camera (uses adaptive position)
      let! playerPos = 
          state 
          |> Scenario.ActiveScenario 
          |> AVal.bind (fun scenario -> 
              scenario.components.positions |> AMap.find playerId)
          |> AVal.force
      
      camera <- CameraSystem.setPosition (Position.toVector2 playerPos) camera
      
      base.Update gameTime
```

**Lines of code**: ~15 (down from ~120)
**Transactions per frame**: 1 (down from N+1)
**AVal.force calls**: 1 (down from N+3)

### 8.2 Drawing (Unchanged)

The `GetDrawingContext` function remains adaptive and works perfectly with the new architecture:

```fsharp
override this.Draw gameTime =
    match gameState with
    | ValueSome state ->
      let drawCtx =
        state
        |> Scenario.ActiveScenario
        |> GameState.GetDrawingContext state.services
        |> AVal.force  // Only force ONCE per frame
      
      // Rendering code unchanged...
```

## 9. Implementation Roadmap

### Phase 1: Foundation & Core Types
**Goal**: Set up Pomo.Lib2 infrastructure and prove FDA usage is correct  
**Effort**: 3-4 days

**Tasks:**
1. Create project structure: `Pomo.Lib2/` with subdirectories (Systems/, Components/, etc.)
2. Implement `Events.fs` (EventBus with clist backing)
3. Implement `State.fs` (GameStateChange DU, ComponentMaps, ScenarioState)
4. Implement `Components.fs` (all 13 component structs)
5. Implement `Pipeline.fs` (ISystem interface, SystemContext, Pipeline runner with single transaction)
6. Implement `applyBatch` function with proper change grouping

**Validation Criteria:**
- ✅ Can instantiate all types
- ✅ Can run empty pipeline (no systems)
- ✅ `applyBatch` applies changes in single transaction
- ✅ Unit tests for EventBus (publish, subscribe, clear)

**Deliverables:**
- Pomo.Lib2.fsproj with FDA dependency
- All core types defined
- Empty pipeline runs without error

### Phase 2: DerivedStats System (Critical)
**Goal**: First real system, proves adaptive projections work correctly  
**Effort**: 4-5 days

**Tasks:**
1. Implement `DerivedStatsSystem.fs` with persistent adaptive projection
2. Implement stat calculation logic (13 derived stats)
3. Implement equipment aggregation
4. Implement effect modifier application (additive, multiplicative, etc.)
5. Support dynamic modifiers (formula-based)
6. Create comprehensive unit tests:
   - Base stat calculation (no equipment, no effects)
   - Equipment bonuses
   - Effect modifiers (all 4 types)
   - Effect stacking
   - Dynamic modifiers
   - Elemental attributes/resistances
7. Integration test: Add effect → verify derived stats update → remove effect → verify revert

**Validation Criteria:**
- ✅ Stat calculations match Pomo.Lib's logic
- ✅ Adaptive projection updates when components change
- ✅ Adaptive projection does NOT recompute when unrelated components change
- ✅ Only changed entities recompute
- ✅ Can be accessed from SystemContext

**Deliverables:**
- Working DerivedStatsSystem
- 50+ unit tests covering all modifier types
- Performance test showing incremental updates work

### Phase 3: Effects System
**Goal**: Implement effect lifecycle management  
**Effort**: 4-5 days

**Tasks:**
1. Implement `EffectsSystem.fs` with ticking logic
2. Handle all duration types: Instant, Timed, Loop, PermanentLoop, Permanent
3. Implement effect stacking rules: NoStack, RefreshDuration, AddStack(max)
4. Handle effect expiration and removal
5. Publish `EffectRemoved` events
6. Support DoT/HoT (damage over time / healing over time)
7. Unit tests:
   - Timed effects expire after duration
   - Looping effects tick at intervals
   - Permanent effects never expire
   - Stacking rules work correctly
   - DoT publishes DamageDealt events

**Validation Criteria:**
- ✅ Effects tick down correctly
- ✅ Effects expire and are removed from component map
- ✅ Looping effects trigger at correct intervals
- ✅ Stacking rules enforced
- ✅ Events published for expiration and DoT ticks

**Deliverables:**
- Working EffectsSystem
- 30+ unit tests
- Integration test: Apply stacking effects → verify only correct number present

### Phase 4: Spatial Index System
**Goal**: Build performance foundation for AI and collision  
**Effort**: 4-6 days

**Tasks:**
1. Implement `AdaptiveSpatialIndex.fs` with grid-based partitioning
2. Implement core operations:
   - Insert(entityId, position)
   - Update(entityId, oldPos, newPos) - move between cells
   - Remove(entityId)
   - QueryRadius(position, radius) → aset<EntityId>
   - QueryRect(bounds) → aset<EntityId>
3. Implement `SpatialIndexSystem.fs` to keep index synchronized
4. Unit tests:
   - Entities in correct cells after insert
   - Entities move between cells correctly
   - QueryRadius returns only entities in range
   - QueryRect returns only entities in bounds
5. Performance tests:
   - 1000 entities, random positions
   - QueryRadius should be 10-100x faster than brute force
   - Updates should be near-instant

**Validation Criteria:**
- ✅ Spatial queries return correct entities
- ✅ Adaptive queries update when entities move
- ✅ O(log n) query performance
- ✅ Cell updates don't trigger full index rebuild

**Deliverables:**
- Working AdaptiveSpatialIndex
- Working SpatialIndexSystem
- 20+ unit tests
- Performance benchmark showing O(log n) scaling

### Phase 5: Movement System
**Goal**: Implement entity movement and pathfinding  
**Effort**: 3-4 days

**Tasks:**
1. Implement `MovementSystem.fs`
2. Handle `NavigateRequested` events → set destination and calculate path
3. Implement path-following logic (advance along path based on MovementSpeed)
4. Handle scenario bounds (clamp positions)
5. Clear movement when destination reached
6. Publish position updates
7. Activate SpatialIndex and Collision systems when positions change
8. Unit tests:
   - Setting destination calculates path
   - Entity moves along path
   - Entity stops at destination
   - Bounds checking works
   - MovementSpeed affects movement rate

**Validation Criteria:**
- ✅ Entities navigate to destinations
- ✅ Paths are calculated correctly
- ✅ Entities stop at destination
- ✅ Triggers spatial index updates

**Deliverables:**
- Working MovementSystem
- 15+ unit tests
- Integration test with SpatialIndexSystem

### Phase 6: AI System
**Goal**: Implement AI perception and decision making  
**Effort**: 5-7 days

**Tasks:**
1. Implement `AISystem.fs`
2. Implement AI perception using SpatialIndex (O(log n) queries)
3. Implement AI state machine (Idle, Investigating, Pursuing, Engaging, etc.)
4. Implement cue detection (Visual, Audio, Projectile, Tactile, Memory)
5. Implement behavior types (Passive, Aggressive, Defensive, Patrol, etc.)
6. Implement memory system (track last known positions)
7. Decision making → publish NavigateRequested and AbilityUseRequested events
8. Subscribe to DamageDealt and EntityDied for reactive behavior
9. Unit tests:
   - AI detects entities within perception range
   - AI transitions states based on conditions
   - AI pursues detected threats
   - AI returns to patrol when threats gone
   - Memory system tracks entities correctly

**Validation Criteria:**
- ✅ AI perceives entities using spatial queries
- ✅ AI state machine transitions correctly
- ✅ AI makes reasonable decisions (navigate/attack)
- ✅ AI reacts to events (damage taken)

**Deliverables:**
- Working AISystem
- 25+ unit tests
- Integration test: AI detects player → pursues → engages

### Phase 7: Abilities System
**Goal**: Implement ability resolution with all targeting types  
**Effort**: 6-8 days (most complex system)

**Tasks:**
1. Implement `AbilitiesSystem.fs`
2. Subscribe to `AbilityUseRequested` events
3. Implement validation logic:
   - Cooldown check
   - Resource check (HP/MP cost)
   - Stun/Silence check
   - Range check
   - Requirement check (stats, abilities, formulas)
4. Implement all 9 targeting types:
   - Self
   - SingleAlly / SingleEnemy
   - GroundArea / GroundPoint
   - AreaRandomTargets
   - ChainTargets
   - ConeTargets
   - StraightLine (with terrain collision)
5. Implement damage calculation using DerivedStats + formulas
6. Apply resource costs
7. Trigger cooldowns
8. Apply effects to targets
9. Spawn projectiles/AoEs/impacts
10. Publish events: DamageDealt, EffectApplied, CooldownTriggered
11. Unit tests for each targeting type
12. Unit tests for validation (cooldown, resources, stun, silence)
13. Integration tests with DerivedStatsSystem

**Validation Criteria:**
- ✅ All targeting types resolve correctly
- ✅ Validation prevents invalid ability usage
- ✅ Damage calculated correctly using derived stats
- ✅ Cooldowns enforced
- ✅ Effects applied to correct targets
- ✅ Projectiles spawned for projectile abilities

**Deliverables:**
- Working AbilitiesSystem
- 50+ unit tests (targeting types, validation, edge cases)
- Integration test: Full ability resolution flow

### Phase 8: Inventory System
**Goal**: Implement item and equipment management  
**Effort**: 4-5 days

**Tasks:**
1. Implement `InventorySystem.fs`
2. Handle `ItemUseRequested` → trigger item ability
3. Handle `ItemEquipRequested` → validate slot, equip item
4. Handle `ItemUnequipRequested` → unequip item
5. Support consumable items (decrease usage count)
6. Support equipment items (stat bonuses, elemental bonuses)
7. Weight management (optional, if in Pomo.Lib)
8. Unit tests:
   - Equip item to correct slot
   - Unequip item
   - Use consumable (usage count decreases)
   - Equipment triggers DerivedStats update
   - Cannot equip invalid item type to slot

**Validation Criteria:**
- ✅ Items equip/unequip correctly
- ✅ Consumables decrease usage count
- ✅ Equipment changes trigger derived stats update
- ✅ Invalid operations rejected

**Deliverables:**
- Working InventorySystem
- 20+ unit tests

### Phase 9: Collision & Combat Systems
**Goal**: Implement collision detection and damage application  
**Effort**: 4-5 days

**Tasks:**
1. Implement `CollisionSystem.fs`
   - Use SpatialIndex for efficient queries
   - Check entities in same cell for overlap
   - Handle terrain collision
   - Publish `CollisionOccurred` events
2. Implement `CombatSystem.fs`
   - Subscribe to `DamageDealt` and `CollisionOccurred` events
   - Apply damage to resources
   - Check for death (HP <= 0)
   - Apply damage resistance/mitigation using DerivedStats
   - Handle elemental damage
   - Publish `EntityDied` and `ResourcesChanged` events
3. Unit tests:
   - Collision detection finds overlapping entities
   - Damage reduces HP correctly
   - Entity dies when HP reaches 0
   - Elemental resistance reduces damage
   - Critical hits calculate correctly

**Validation Criteria:**
- ✅ Collisions detected efficiently using spatial index
- ✅ Damage applied correctly
- ✅ Death triggered at HP <= 0
- ✅ Elemental damage/resistance works

**Deliverables:**
- Working CollisionSystem
- Working CombatSystem
- 25+ unit tests

### Phase 10: Projectiles & Visual Effects Systems
**Goal**: Implement projectile movement and visual cleanup  
**Effort**: 3-4 days

**Tasks:**
1. Implement `ProjectilesSystem.fs`
   - Move projectiles based on speed and behavior (Linear/Seeker)
   - Handle terrain collision
   - Detect entity hits using SpatialIndex
   - Trigger pending resolutions on impact
   - Publish `ProjectileHit` events
2. Implement `VisualEffectsSystem.fs`
   - Cleanup expired impacts, AoEs, floating texts
   - Tick durations
3. Unit tests:
   - Linear projectiles move in straight line
   - Seeker projectiles home to target
   - Projectiles detect hits
   - Visual effects expire correctly

**Validation Criteria:**
- ✅ Projectiles move and collide correctly
- ✅ Seeker behavior works
- ✅ Visual effects cleanup when expired

**Deliverables:**
- Working ProjectilesSystem
- Working VisualEffectsSystem
- 20+ unit tests

### Phase 11: Engagement, Path Preview & Transition Systems
**Goal**: Implement remaining game systems  
**Effort**: 4-5 days

**Tasks:**
1. Implement `EngagementSystem.fs`
   - Handle duel requests
   - Manage battle instances
   - Validate engagement rules (faction targeting)
2. Implement `PathPreviewSystem.fs`
   - Maintain adaptive path visualization
   - Update when terrain or entities change
3. Implement `TransitionSystem.fs`
   - Check for scenario transitions
   - Handle teleportation
4. Implement `TimeSystem.fs`
   - Increment game time each frame
5. Unit tests for each system

**Validation Criteria:**
- ✅ Duels can be initiated and managed
- ✅ Path preview updates reactively
- ✅ Scenario transitions trigger correctly

**Deliverables:**
- 4 working systems
- 25+ unit tests

### Phase 12: Integration & Testing
**Goal**: Wire up all systems and validate end-to-end  
**Effort**: 5-7 days

**Tasks:**
1. Implement `InputSystem.fs` to translate player input to events
2. Update `PomoGame.Update` to:
   - Collect events from InputSystem and AISystem
   - Run Pipeline with all systems
   - Apply changes in single transaction
   - Force drawing context once
3. Full integration tests:
   - Player navigates → entity moves → camera follows
   - Player casts ability → damage dealt → enemy dies
   - AI detects player → pursues → attacks
   - Equip item → stats update → damage increases
   - Multiple scenarios run independently (split-screen test)
4. Performance validation:
   - Single transaction per frame confirmed
   - Single AVal.force confirmed
   - FDA invalidations minimal
5. Feature parity check against Pomo.Lib

**Validation Criteria:**
- ✅ All 17 systems working together
- ✅ Game playable end-to-end
- ✅ Performance metrics met (1 transaction, 1 force)
- ✅ All features from Pomo.Lib present

**Deliverables:**
- Fully integrated Pomo.Lib2
- 50+ integration tests
- Performance report

### Phase 13: Optimization & Polish
**Goal**: Tune performance and add polish  
**Effort**: Ongoing / 2-3 weeks

**Tasks:**
1. Profile system execution times
2. Optimize spatial index cell size (test different sizes)
3. Implement system activation/deactivation:
   - Deactivate ProjectilesSystem when no projectiles
   - Deactivate MovementSystem when no moving entities
   - etc.
4. Add telemetry:
   - Track FDA invalidation counts
   - Track system execution times
   - Track entity counts per system
5. Optimize hot paths identified by profiler
6. Add visual debugging tools:
   - Spatial index visualization
   - Path preview
   - Collision bounds
   - AI perception radius

**Validation Criteria:**
- ✅ 60 FPS with 1000+ entities
- ✅ System activation/deactivation working
- ✅ Profiler shows no bottlenecks

**Deliverables:**
- Performance optimized Pomo.Lib2
- Debug visualization tools
- Performance report

**Total Estimated Effort**: 10-14 weeks (2.5-3.5 months)

## 10. Success Metrics

- ✅ **Systems are independent**: Each system can be tested in isolation
- ✅ **Pipeline is explicit**: System execution order is clear and configurable
- ✅ **Spatial queries are fast**: O(log n) collision/perception instead of O(n²)
- ✅ **FDA is used correctly**: Only 1 transaction and 1 force per frame
- ✅ **Derived stats are reactive**: Update incrementally when dependencies change
- ✅ **Multiplayer works**: Multiple scenarios run independently
- ✅ **Performance improved**: 10-100x faster on frames with many commands
- ✅ **All features preserved**: 1:1 migration, zero features lost

## 11. Open Questions

1. **System state persistence**: How do we serialize adaptive projections for save/load?
2. **Cross-scenario events**: Should systems in different scenarios communicate?
3. **Hot reload**: Can we replace system implementations at runtime?
4. **Networking**: How do we sync GameStateChanges to clients?

## 12. Key Takeaways

**Why Build From Scratch Instead of Refactoring?**

1. **FDA Misuse is Fundamental**: Fixing N-transactions-per-frame requires changing how state updates work at the core
2. **Component Decomposition**: Breaking apart EntityComponents requires rewriting every system
3. **Spatial Index**: No spatial indexing means rewriting collision/AI from scratch anyway
4. **Event Bus**: Adding event-driven communication means rethinking system boundaries
5. **Cleaner Codebase**: Starting fresh avoids accumulation of workarounds and tech debt

**What We're Keeping from Pomo.Lib:**
- ✅ All game features (abilities, effects, inventory, AI, etc.)
- ✅ Service boundaries (IAbilityStore, IEffectStore, etc.)
- ✅ Formula system
- ✅ Ability targeting types
- ✅ Effect modifiers
- ✅ AI archetypes and behavior
- ✅ Pathfinding logic

**What We're Fixing:**
- ❌ N transactions → ✅ 1 transaction
- ❌ N AVal.force → ✅ 1 AVal.force  
- ❌ Monolithic EntityComponents → ✅ Decomposed components
- ❌ O(n²) spatial queries → ✅ O(log n) with spatial index
- ❌ Monolithic tick function → ✅ Modular systems
- ❌ No event system → ✅ Event bus
- ❌ Ephemeral projections → ✅ Persistent adaptive projections

**Core Insight:**
Building Pomo.Lib2 from scratch is faster than refactoring Pomo.Lib because we're changing fundamental assumptions about how state updates work. The architecture documented here gives us a clear path forward with all lessons learned from the prototype.
