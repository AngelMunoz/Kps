# Core Game Plan - MonoGame Integration & Gameplay Systems

**Status**: 🚧 **IN PROGRESS** - Phase 6.1 started (basic rendering and health bars)

**Created**: 2025-10-11

**Prerequisites**: Phases 0-5.5 of RPG-Core-Plan.md completed

---

## Overview

This document outlines the implementation plan for integrating the Pomo.Lib RPG framework with MonoGame to create a playable game experience. The focus is on visual feedback, player interaction, scenario-based gameplay, and battle system engagement.

**Primary Goals**:
1. **Visual Feedback**: See entities on screen with geometric shape placeholders
2. **Player Input**: Mouse/touch-based navigation and targeting
3. **Combat Interaction**: Activate abilities against enemies and see results
4. **Scenario System**: Navigate terrain with walkable/non-walkable areas
5. **Scenario Transitions**: Move between different scenarios
6. **Battle Management**: Engage/disengage battle mechanics for peaceful vs combat scenarios

**Input Design Philosophy**:
- **Mouse/Touch Navigation**: Click/tap screen to move player entities
- **Ability Activation**: Keyboard (0-9) or on-screen buttons
- **Targeting System**: Drag pointer for directional abilities, click entities for targeted abilities
- **Context-Aware**: Targeting UI adapts to ability type (Self, AoE, Single Target)

---

## Architectural Overview: Per-Scenario GameState

**Critical Design Decision**: This implementation uses a **Per-Scenario GameState architecture** to support split-screen and network multiplayer scenarios.

### Why Per-Scenario State?

The game is designed with **split-screen and multiplayer** as core requirements. This means:
- **Multiple players in different scenarios simultaneously**: Player 1 in Forest, Player 2 in Town
- **Independent battle contexts**: One player in combat, another exploring peacefully
- **Independent time progression**: Each scenario can tick independently (future: pause, slow-motion)
- **Efficient rendering**: Direct scenario lookup for split-screen viewports without filtering

### Architecture Structure

```fsharp
// Each scenario owns its own state
type ScenarioState = {
  scenario: Scenario
  entities: cmap<Guid<EntityId>, EntityComponents>  // Scenario-local entities
  gameTime: cval<int64<Tick>>  // Independent time per scenario
  battleContext: BattleContext voption  // Independent battle state
}

// Player tracking across scenarios
type PlayerContext = {
  playerId: Guid<PlayerId>
  currentScenarioId: Guid<ScenarioId>
  controlledEntityId: Guid<EntityId>
  camera: Camera  // Each player has own camera
}

// Global game state
type GameState = {
  scenarios: cmap<Guid<ScenarioId>, ScenarioState>
  players: cmap<Guid<PlayerId>, PlayerContext>
  services: EngineServices
}
```

### Key Benefits

1. **Multiple Active Scenarios**: Each scenario ticks and renders independently
2. **Clean Entity Ownership**: Entities belong to specific scenarios (no sync issues)
3. **Independent Battle State**: Each scenario has its own battle context
4. **Split-Screen Ready**: O(1) lookup for player's current scenario
5. **Network Ready**: Clean state partitioning for client-server architecture
6. **Static Scenario Generation**: Scenarios can be pre-defined with entities (future)

### Entity Management

- **Scenario-Local Entities**: NPCs, enemies belong to specific scenarios
- **Player Entities**: Can move between scenarios (handled by transition system)
- **Transition Flow**: Remove entity from old scenario, add to new scenario

### Implementation Notes

- **Phase 6.1-6.3**: May start with simplified single-scenario state for initial validation
- **Phase 6.4-6.5**: Migrate to full per-scenario architecture
- **Phase 6.6+**: Implement scenario transitions and multi-player support

**See**: Previous issue discussions on Per-Scenario GameState for detailed rationale.

---

## Guiding Principles

- **Iterative Visual Feedback**: Get something visible on screen quickly, refine later
- **Core-First Validation**: Verify Pomo.Lib works correctly in real gameplay before building complex UI
- **Placeholder Graphics**: Use geometric shapes (circles, rectangles) until art assets available
- **Responsive Input**: Immediate visual feedback for all player actions
- **Deterministic Simulation**: Game logic remains in Pomo.Lib, MonoGame handles presentation only
- **Scenario-Driven Design**: All gameplay happens within scenarios with defined rules
- **Battle Context Awareness**: Systems adapt to peaceful vs combat scenarios
- **Per-Scenario State**: Each scenario owns its entities and state independently
- **Test-Driven Development**: Each phase includes unit/integration tests to verify functionality

---

## Phase 6.1 — Basic Rendering & Visual Feedback

Progress Update (2025-10-11):
- Implemented Position component in Pomo.Lib.Domain and added to EntityComponents.
- GameStateOperations.createEntity initializes Position to (0,0).
- Pomo.Core renders entities as colored rectangles with simple HP bars using a 1x1 pixel texture and SpriteBatch.
- Initial positions for a player (green) and an enemy (red) are set so both are visible on screen.
- Not yet implemented: camera follow/zoom, labels, circle rendering, dedicated RenderSystem module.

**Goal**: See entities on screen and verify core library integration

### 6.1.1 Entity Rendering System
- **Geometric Placeholder Rendering**:
  - Circles for entities (player = green, enemy = red, neutral = blue)
  - Size based on entity stage (First = small, Second = medium, Third = large)
  - Position on 2D coordinate system
- **Health Bar Display**:
  - Render HP/MP bars above entities
  - Color coding (HP = red/green gradient, MP = blue)
  - Update in real-time as resources change
- **Entity Labels**:
  - Display entity profession/name
  - Show status effects as small icons/text
- **Camera System**:
  - Simple 2D camera following player entity
  - Zoom controls (mouse wheel or pinch gesture)

### 6.1.2 Domain Extensions for Positioning
- **Add Position Component** (Pomo.Lib):
  ```fsharp
  [<Struct>]
  type Position = {
    X: float32
    Y: float32
  }
  ```
- **Update EntityComponents**:
  - Add Position to EntityComponents record
  - Default position (0, 0) for entities
- **StateChange Updates**:
  - Support position updates in StateChange mechanism

### 6.1.3 Rendering Pipeline
- **RenderSystem.fs** (Pomo.Core):
  - Query alive entities from GameState
  - Render each entity as geometric shape at position
  - Render health bars and labels
  - Use SpriteBatch for 2D rendering
- **Color Palette**:
  - Player: `Color.Green`
  - Enemy: `Color.Red`
  - Neutral: `Color.Blue`
  - Dead entities: `Color.Gray` (faded)

**Deliverables**:
- [ ] Position component added to Domain
- [ ] RenderSystem.fs with entity rendering
- [ ] Health bar rendering
- [ ] Camera system with follow and zoom
- [ ] Visual verification: see 2 entities (player, enemy) on screen with health bars

**Testing Requirements**:
- [ ] Unit tests for Position component initialization and validation
- [ ] Integration test: Create entities with positions and verify rendering output
- [ ] Visual test: Confirm entities render at correct screen positions
- [ ] Camera test: Verify camera follow and zoom functionality
- [ ] Health bar test: Verify health bars update when entity resources change

---

## Phase 6.2 — Input & Targeting System

**Goal**: Interact with entities through mouse/touch and keyboard

### 6.2.1 Input Management
- **InputManager.fs** (Pomo.Core):
  - Mouse/Touch input state tracking
  - Keyboard state for ability hotkeys (0-9)
  - Screen-to-world coordinate conversion
- **Input Types**:
  ```fsharp
  [<Struct>]
  type InputAction =
    | NavigateTo of targetPosition: Vector2
    | SelectEntity of entityId: Guid<EntityId>
    | ActivateAbility of abilityIndex: int
    | ConfirmTarget of targetIds: Guid<EntityId>[]
    | CancelAction
  ```

### 6.2.2 Entity Selection & Targeting
- **Selection System**:
  - Click/tap entity to select
  - Visual highlight for selected entity (outline, glow effect)
  - Display selected entity info panel
- **Targeting Modes** (based on ability targeting type):
  - **Self**: Auto-target, no selection needed
  - **SingleEnemy/SingleAlly**: Click to select target entity
  - **MultiTarget**: Click multiple entities (show count limit)
  - **AoE**: Drag to show area indicator, release to confirm
- **Visual Feedback**:
  - Cursor changes based on targeting mode
  - Highlight valid targets (green) and invalid targets (red/grayed)
  - Show ability range indicators

### 6.2.3 Ability Activation Flow
1. Press ability hotkey (0-9) or click UI button
2. Enter targeting mode based on ability type
3. Select targets (if required)
4. Activate ability via GameStateOperations.activateAbility
5. Visual feedback: animation placeholder, damage numbers, effect icons

**Deliverables**:
- [ ] InputManager.fs with mouse/touch/keyboard handling
- [ ] Entity selection with visual highlighting
- [ ] Targeting system for all ability types
- [ ] Ability activation integrated with core library
- [ ] Visual feedback for ability use (placeholder animations)

**Testing Requirements**:
- [ ] Unit tests for InputAction parsing and screen-to-world coordinate conversion
- [ ] Integration test: Simulate mouse clicks and verify entity selection
- [ ] Targeting test: Verify each targeting mode (Self, SingleTarget, MultiTarget, AoE) works correctly
- [ ] Ability activation test: Trigger ability via input and verify StateChange is applied
- [ ] Input validation test: Verify invalid targets are rejected appropriately

---

## Phase 6.3 — Movement & Navigation

**Goal**: Move entities within a scenario

### 6.3.1 Movement System
- **Movement Component** (Pomo.Lib):
  ```fsharp
  [<Struct>]
  type Movement = {
    Speed: float32  // units per second
    Destination: Position voption
    Path: Position list
  }
  ```
- **Movement Command**:
  - Add to Domain.Rules.Command: `Move of MoveAction`
  - MoveAction: actor, destination
- **Movement Resolution**:
  - Calculate path (straight line for now, pathfinding later)
  - Update position incrementally each frame
  - Stop when destination reached

### 6.3.2 Navigation Input
- **Click-to-Move**:
  - Click screen position
  - Set entity destination
  - Entity moves toward destination
- **Visual Feedback**:
  - Show movement destination marker
  - Trail/path visualization
  - Animate entity position

### 6.3.3 Movement Constraints (Basic)
- **Boundary Checking**:
  - Define scenario bounds
  - Clamp movement to within bounds
- **Collision Detection** (simple):
  - Entities cannot overlap (radius-based)
  - Stop movement if collision detected

**Deliverables**:
- [ ] Movement component in Domain
- [ ] Move command and resolution
- [ ] Click-to-move input handling
- [ ] Movement animation and visual feedback
- [ ] Basic boundary and collision checking

**Testing Requirements**:
- [ ] Unit tests for Movement component and path calculation
- [ ] Movement resolution test: Verify Move command updates entity position correctly
- [ ] Boundary test: Verify entities cannot move outside scenario bounds
- [ ] Collision test: Verify entities cannot overlap (radius-based collision)
- [ ] Integration test: Click-to-move flow from input to position update

---

## Phase 6.4 — Scenario System Foundation

**Goal**: Define scenarios with terrain and rules

### 6.4.1 Scenario Domain Types (Pomo.Lib)

**Note**: These types implement the **Per-Scenario GameState architecture** for split-screen/multiplayer support.

**Collision System**: Polygon-based for organic shapes (not tile grid). This allows for 2.5D graphics with natural boundaries for objects like corals, trees, and rocks.

```fsharp
[<Struct>]
type TerrainType =
  | Walkable
  | Blocked
  | Water
  | Hazard  // causes damage over time

[<Struct>]
type CollisionGeometry =
  | Circle of center: Vector2 * radius: float32
  | Polygon of vertices: Vector2[]  // Arbitrary convex polygon for organic shapes
  | None  // Visual-only objects with no collision

[<Measure>]
type ObjectId

type TerrainObject = {
  Id: Guid<ObjectId>
  Position: Position
  CollisionGeometry: CollisionGeometry
  TerrainType: TerrainType
  DepthLayer: float32  // Z-order for 2.5D rendering (0.0 = background, 1.0 = foreground)
  SpriteId: string voption  // Visual representation (optional)
}

type VisualLayer = {
  SpriteId: string
  Position: Position
  DepthLayer: float32
  Parallax: float32  // For background scrolling effects
}

[<Measure>]
type ScenarioId

// Scenario combat type determines targeting rules (Phase 6.7)
[<Struct>]
type ScenarioCombatType =
  | PvE          // Player vs Environment (default) - players cannot target other players
  | PvP          // Player vs Player - players can target enemy players (not in same party)
  | PvPvE        // Player vs Player vs Environment - players can target both enemy players and NPCs

// Base scenario configuration (polygon-based collision)
type Scenario = {
  Id: Guid<ScenarioId>
  Name: string
  BoundsWidth: float32   // World bounds in units (not tiles)
  BoundsHeight: float32
  TerrainObjects: TerrainObject list  // Polygonal collision objects
  VisualLayers: VisualLayer list      // Background/foreground sprites (no collision)
  BattleEnabled: bool
  CombatType: ScenarioCombatType  // Defines targeting rules (PvE/PvP/PvPvE)
  Transitions: ScenarioTransition list
}

// Per-scenario state (owns entities and time independently)
type ScenarioState = {
  scenario: Scenario
  entities: cmap<Guid<EntityId>, EntityComponents>  // Scenario-local entities
  gameTime: cval<int64<Tick>>  // Independent time per scenario
  battleContext: BattleContext voption  // Independent battle state
}

// Player context for multi-player support
type PlayerContext = {
  playerId: int
  currentScenarioId: Guid<ScenarioId>
  controlledEntityId: Guid<EntityId>
}

// Party system for player grouping (Phase 6.7)
[<Measure>]
type PartyId

type Party = {
  Id: Guid<PartyId>
  Members: HashSet<Guid<EntityId>>  // Player entity IDs in this party
  Name: string
}

// Updated GameState with per-scenario architecture
type GameState = {
  scenarios: cmap<Guid<ScenarioId>, ScenarioState>
  players: cmap<int, PlayerContext>
  parties: cmap<Guid<PartyId>, Party>  // Track player parties (Phase 6.7)
  services: EngineServices
}

type ScenarioTransition = {
  FromPosition: Position
  ToScenarioId: Guid<ScenarioId>
  ToPosition: Position
  RequiresCondition: (GameState -> bool) voption
}
```

### 6.4.2 Scenario Management (Pomo.Lib)
- **ScenarioManager Module**:
  - createScenarioState: Scenario -> EngineServices -> ScenarioState
  - getScenarioState: Guid<ScenarioId> -> GameState -> ScenarioState voption
  - getPlayerScenario: int -> GameState -> ScenarioState voption
  - getScenarioEntities: Guid<ScenarioId> -> GameState -> amap<Guid<EntityId>, EntityComponents>
  - addEntityToScenario: Guid<ScenarioId> -> Guid<EntityId> -> EntityComponents -> GameState -> StateChange
  - removeEntityFromScenario: Guid<ScenarioId> -> Guid<EntityId> -> GameState -> StateChange
- **Per-Scenario State Operations**:
  - Each scenario ticks independently
  - Entities are owned by specific scenarios
  - Battle context is per-scenario

### 6.4.3 Terrain Rendering (Pomo.Core)
- **Polygon-Based Rendering**:
  - Render TerrainObjects with sprites at specified positions
  - Depth-sorted rendering (sort by DepthLayer: background to foreground)
  - Debug visualization: draw collision polygons/circles as wireframes (optional)
  - Color coding for terrain types in debug mode: Walkable = light gray, Blocked = dark gray, Water = blue, Hazard = orange
- **Visual Layers**:
  - Render background/foreground VisualLayer sprites
  - Apply parallax scrolling effects for depth perception
  - No collision processing for visual-only layers
- **2.5D Depth Ordering**:
  - Y-sorting: entities further down render in front (pseudo-3D effect)
  - Combined with DepthLayer for fine control (e.g., tree trunk in front, player behind tree)
- **Scenario Bounds Visualization**:
  - Draw scenario border (BoundsWidth × BoundsHeight rectangle)
  - Show transition points (portals, doors) as visual indicators

**Deliverables**:
- [ ] Scenario domain types in Pomo.Lib (Scenario, ScenarioState, PlayerContext, CollisionGeometry, TerrainObject, VisualLayer, updated GameState)
- [ ] ScenarioManager module with per-scenario operations
- [ ] GameState refactored to per-scenario architecture
- [ ] Polygon-based terrain rendering system with depth sorting
- [ ] Sample scenario definition with TerrainObjects (test map with organic shapes)
- [ ] Migration from single GameState to per-scenario architecture

**Testing Requirements**:
- [ ] Unit tests for Scenario, ScenarioState, CollisionGeometry, and TerrainObject creation
- [ ] ScenarioManager test: Create scenario state and verify entity ownership
- [ ] Per-scenario time test: Verify each scenario has independent gameTime
- [ ] Player context test: Add player to scenario and verify currentScenarioId tracking
- [ ] Entity ownership test: Verify entities belong to correct scenario (no cross-scenario references)
- [ ] Collision geometry test: Verify Circle and Polygon collision shapes work correctly
- [ ] Depth sorting test: Verify DepthLayer correctly orders visual elements
- [ ] Integration test: Create multiple scenarios and verify independent state management

---

## Phase 6.5 — Terrain-Aware Movement & Pathfinding

**Goal**: Entities respect terrain and navigate intelligently

### 6.5.1 Polygon Collision Detection
- **Collision Module** (Pomo.Lib):
  - Point-in-polygon algorithm (ray casting) for movement validation
  - Circle-polygon intersection for entity radius checks
  - Efficient collision queries using spatial partitioning (grid or quadtree)
- **Movement Validation**:
  - Check destination position against all TerrainObjects with CollisionGeometry
  - Block movement if destination intersects Blocked terrain
  - Visual feedback: invalid move indicator (red X or blocked cursor)
- **Hazard Detection**:
  - Check entity position against Hazard TerrainObjects
  - Apply damage/effect when entity overlaps hazard geometry
  - Visual warning for hazard areas (pulsing effect, warning icon)

### 6.5.2 Pathfinding (Grid Overlay or NavMesh)
- **Option A: Grid Overlay + Polygon Validation (Simpler, Recommended for Phase 6.5)**:
  - Generate coarse navigation grid over scenario bounds
  - Mark grid cells as walkable/blocked based on polygon overlaps
  - Use A* on grid for pathfinding
  - Final path validation: ensure waypoints don't intersect collision polygons
  - Cost function: distance, terrain type, entity speed
- **Option B: Navigation Mesh (Advanced, Future)**:
  - Define walkable areas as polygons (NavMesh)
  - A* over polygon graph for precise pathfinding
  - More complex authoring but exact walkable boundaries
- **Movement Integration**:
  - Calculate path when destination set
  - Follow path waypoints with smooth interpolation
  - Recalculate if path blocked or dynamic obstacles appear

### 6.5.3 Movement Visual Refinements
- **Path Preview**:
  - Show path line before movement
  - Indicate path validity (green = valid, red = invalid/blocked)
  - Highlight collision obstacles along path
- **Movement Speed Variation**:
  - Different terrain types affect speed (Water = slower, Walkable = normal)
  - Movement stat (Dexterity) affects base speed
  - Smooth acceleration/deceleration

**Deliverables**:
- [ ] Polygon collision detection module (point-in-polygon, circle-polygon)
- [ ] Spatial partitioning for efficient collision queries
- [ ] Hazard detection and effect application
- [ ] Grid overlay pathfinding with polygon validation (Option A)
- [ ] Path preview visualization
- [ ] Terrain-based movement speed

**Testing Requirements**:
- [ ] Point-in-polygon test: Verify algorithm correctly detects interior/exterior points
- [ ] Circle-polygon test: Verify entity radius collision with polygon boundaries
- [ ] Movement validation test: Verify entities cannot move into Blocked polygons
- [ ] Hazard test: Verify damage/effects applied when entity overlaps Hazard geometry
- [ ] Pathfinding test: Grid overlay A* produces valid paths around polygon obstacles
- [ ] Path validation test: Invalid paths (no route) handled correctly
- [ ] Spatial partitioning test: Verify efficient collision queries (performance)
- [ ] Terrain speed test: Verify movement speed varies by terrain type

---

## Phase 6.6 — Scenario Transitions

**Goal**: Move between scenarios

### 6.6.1 Transition Detection
- **Proximity Check**:
  - Detect when entity near transition point
  - Visual indicator: portal/door highlight
- **Transition Trigger**:
  - Auto-trigger or player confirmation
  - Check transition conditions

### 6.6.2 Transition Execution
- **Scene Loading**:
  - Unload current scenario entities
  - Load target scenario
  - Place entity at target position
- **State Preservation**:
  - Entity stats, effects, equipment preserved
  - Scenario-specific entities removed/added
- **Visual Transition**:
  - Fade out/fade in effect
  - Loading indicator if needed

### 6.6.3 Scenario Definitions
- **Multi-Scenario Content**:
  - Define 3+ test scenarios
  - Various terrain types and layouts
  - Connected via transitions
- **Scenario Types**:
  - Town (peaceful, no battle)
  - Wilderness (battle enabled)
  - Dungeon (battle enabled, hazards)

**Deliverables**:
- [ ] Transition detection and triggering
- [ ] Scenario loading/unloading system (per-scenario architecture)
- [ ] Entity migration between scenarios
- [ ] Visual transition effects
- [ ] 3+ connected scenario definitions
- [ ] State preservation during transitions

**Testing Requirements**:
- [ ] Transition detection test: Verify proximity triggers activate correctly
- [ ] Entity migration test: Verify entity removed from old scenario, added to new scenario (per-scenario architecture)
- [ ] State preservation test: Entity stats, equipment, effects preserved during transition
- [ ] PlayerContext update test: Verify player's currentScenarioId updates correctly
- [ ] Conditional transition test: Verify RequiresCondition blocks invalid transitions
- [ ] Multi-scenario test: Verify multiple scenarios can be active simultaneously (split-screen support)

---

## Phase 6.7 — Battle Engagement System

**Goal**: Control when battle mechanics are active and enforce PvE/PvP/PvPvE targeting rules

### 6.7.1 Battle Context & Combat Types (Pomo.Lib)
```fsharp
// Scenario combat type determines targeting rules
[<Struct>]
type ScenarioCombatType =
  | PvE          // Player vs Environment (default) - players cannot target other players
  | PvP          // Player vs Player - players can target enemy players (not in same party)
  | PvPvE        // Player vs Player vs Environment - players can target both enemy players and NPCs

// Party system for player grouping
[<Measure>]
type PartyId

type Party = {
  Id: Guid<PartyId>
  Members: HashSet<Guid<EntityId>>  // Player entity IDs in this party
  Name: string
}

type BattleContext = {
  IsActive: bool
  Participants: HashSet<Guid<EntityId>>
  StartTick: int64<Tick>
  CanDisengage: bool
}

// Updated Scenario type with combat type (polygon-based collision)
type Scenario = {
  Id: Guid<ScenarioId>
  Name: string
  BoundsWidth: float32   // World bounds in units (not tiles)
  BoundsHeight: float32
  TerrainObjects: TerrainObject list  // Polygonal collision objects
  VisualLayers: VisualLayer list      // Background/foreground sprites (no collision)
  BattleEnabled: bool
  CombatType: ScenarioCombatType  // NEW: Defines targeting rules
  Transitions: ScenarioTransition list
}

// Updated GameState with party tracking
type GameState = {
  scenarios: cmap<Guid<ScenarioId>, ScenarioState>
  players: cmap<int, PlayerContext>
  parties: cmap<Guid<PartyId>, Party>  // NEW: Track player parties
  services: EngineServices
}
```

### 6.7.2 Targeting Rules by Combat Type

**PvE Scenarios (Default)**:
- **Allowed Targets**: Non-player entities (NPCs, enemies, monsters)
- **Forbidden Targets**: Other player entities (regardless of party affiliation)
- **Use Case**: Towns, cooperative dungeons, story scenarios
- **Validation**: `isPlayerEntity(target) = false` for all offensive abilities

**PvP Scenarios**:
- **Allowed Targets**:
  - Enemy players (players not in the same party as the actor)
  - Non-player entities (NPCs, enemies)
- **Forbidden Targets**: Allied players (players in the same party as the actor)
- **Use Case**: Arenas, dueling zones, competitive areas
- **Validation**:
  - If `isPlayerEntity(target)`: Check `notInSameParty(actor, target)`
  - NPCs always valid

**PvPvE Scenarios**:
- **Allowed Targets**:
  - Enemy players (players not in the same party as the actor)
  - Non-player entities (NPCs, enemies, monsters)
- **Forbidden Targets**: Allied players (players in the same party as the actor)
- **Use Case**: Open-world PvP zones, faction warfare, competitive PvE
- **Validation**: Same as PvP

**Friendly Abilities Exception**:
- Healing and buff abilities can target party members in all combat types
- Validation checks `ability.IsFriendly` flag to allow party targeting

### 6.7.3 Battle Engagement Rules
- **Engagement Triggers**:
  - Hostile entity proximity (if scenario allows)
  - Forced battle (boss encounters, story events)
  - Player initiates combat (attack action)
  - Player-to-player aggression in PvP/PvPvE scenarios
- **Engagement Effects**:
  - Lock participants in battle (movement restricted)
  - Enable combat abilities
  - Start effect/cooldown processing
  - Enforce targeting rules based on scenario combat type
- **Disengagement**:
  - All hostiles defeated
  - Flee action (if allowed)
  - Scenario transition
  - PvP combat timeout (optional)

### 6.7.4 Party System
- **Party Formation**:
  - Players can form/join parties
  - Party members share targeting restrictions
  - Party members cannot target each other with offensive abilities
- **Party Benefits**:
  - Shared experience/rewards (future)
  - Friendly fire protection
  - Coordinated strategies
- **Solo Players**:
  - Treated as single-member party
  - Can target any valid enemy based on scenario type

### 6.7.5 Peaceful Scenario Behavior
- **Battle Disabled Scenarios**:
  - No hostile detection
  - Combat abilities disabled/grayed out
  - Effects still process (buffs, passive abilities)
  - Full movement freedom
  - Targeting rules still apply (cannot target players in PvE)
- **Context Switching**:
  - Smooth transition between peaceful and combat
  - UI adapts to current context
  - Targeting validation always enforced

### 6.7.6 Visual Battle Indicators
- **Battle State UI**:
  - Battle engaged indicator
  - Participant list with party affiliation colors
  - Turn/time display
  - Combat type indicator (PvE/PvP/PvPvE)
- **Entity Behavior**:
  - Hostile entities show aggro radius
  - Battle participants highlighted
  - Party members marked with unique color/icon
  - Valid/invalid targets indicated during ability selection

**Deliverables**:
- [ ] ScenarioCombatType and Party domain types (per-scenario)
- [ ] BattleContext domain types (per-scenario)
- [ ] Targeting validation based on combat type and party affiliation
- [ ] Battle engagement/disengagement logic
- [ ] Party system implementation
- [ ] Peaceful scenario enforcement
- [ ] Visual battle state indicators
- [ ] Context-aware UI and abilities

**Testing Requirements**:
- [ ] Per-scenario battle test: Verify each scenario has independent BattleContext
- [ ] PvE targeting test: Verify players cannot target other players in PvE scenarios
- [ ] PvP targeting test: Verify players can target enemy players (not in same party) in PvP scenarios
- [ ] PvPvE targeting test: Verify both player and NPC targeting work in PvPvE scenarios
- [ ] Party targeting test: Verify party members cannot target each other with offensive abilities
- [ ] Friendly ability test: Verify healing/buffs can target party members in all combat types
- [ ] Engagement test: Verify battle engages on hostile proximity or player action
- [ ] Disengagement test: Verify battle ends when all hostiles defeated or fled
- [ ] Peaceful scenario test: Verify combat abilities disabled when scenario.BattleEnabled = false
- [ ] Split-screen battle test: Verify one player in battle, another exploring (independent contexts)
- [ ] Battle restriction test: Verify movement restrictions during battle engagement
- [ ] Combat type validation test: Verify scenario combat type correctly restricts targeting options

---

## Phase 6.8 — Personal Entity Detail Views

**Goal**: Inspect entity stats, equipment, and abilities

### 6.8.1 Character Sheet UI
- **Stats Panel**:
  - Display all derived stats (AP, MA, HP, MP, etc.)
  - Show base attributes (Power, Magic, Sense, Charm)
  - Equipment bonuses highlighted
- **Resources Panel**:
  - Current/Max HP and MP
  - Status effect list with durations
  - Resistances display
- **Profession Info**:
  - Family, Stage display
  - Advancement indicator (if can advance)

### 6.8.2 Equipment View
- **Equipment Slots**:
  - Visual representation of 7 slots (Head, Chest, Legs, Hands, Weapon1, Weapon2, Accessory)
  - Show equipped items or empty slots
  - Item tooltips with stats
- **Equipment Management**:
  - Drag-and-drop or click to equip (placeholder)
  - Swap equipment between slots
  - Unequip items

### 6.8.3 Ability List
- **Ability Panel**:
  - List all known abilities
  - Show cooldown status
  - Resource costs display
  - Targeting type indicator
- **Ability Details**:
  - Description
  - Formula info (damage type, element)
  - Effects list

### 6.8.4 UI Implementation
- **Panel System** (Pomo.Core):
  - Toggleable UI panels (F1 = character sheet)
  - Modal dialogs for detailed views
  - Responsive layout for different screen sizes
- **Data Binding**:
  - Use GameStateOperations queries
  - Update on state change

**Deliverables**:
- [ ] Character sheet UI with stats display
- [ ] Equipment view with slots
- [ ] Ability list panel
- [ ] UI toggle controls (keyboard shortcuts)
- [ ] Tooltips and detail views

**Testing Requirements**:
- [ ] Stats display test: Verify derived stats calculated and displayed correctly
- [ ] Equipment slot test: Verify all 7 slots render with equipped items or empty
- [ ] Ability list test: Verify abilities display with correct cooldown status
- [ ] Data binding test: Verify UI updates when entity state changes (HP, effects, equipment)
- [ ] Query test: Verify GameStateOperations queries (getDerivedStatsSnapshot, getReadyAbilities) work from UI

---

## Phase 6.9 — Enhanced Visual Feedback & Polish

**Goal**: Improve game feel with animations and effects

### 6.9.1 Ability Visual Effects
- **Damage Numbers**:
  - Floating text showing damage/healing amounts
  - Color-coded (damage = red, heal = green, critical = yellow)
  - Animation: rise and fade
- **Ability Animations**:
  - Projectiles for ranged abilities
  - Area indicators for AoE
  - Impact effects on targets
- **Status Effect Indicators**:
  - Particle effects for buffs/debuffs
  - Icon overlays on entities

### 6.9.2 Sound Effects (Optional)
- **Audio Feedback**:
  - Ability activation sounds
  - Hit/miss sounds
  - UI interaction sounds
  - Background music per scenario type

### 6.9.3 UI Polish
- **Transitions & Animations**:
  - Panel slide in/out
  - Button hover effects
  - Health bar smooth updates
- **Consistency**:
  - Unified color scheme
  - Standardized fonts and spacing
  - Responsive feedback for all actions

**Deliverables**:
- [ ] Damage number system
- [ ] Ability visual effects (basic)
- [ ] Status effect indicators
- [ ] Sound effects (optional)
- [ ] UI animations and polish

**Testing Requirements**:
- [ ] Damage numbers test: Verify numbers display with correct values and colors
- [ ] Visual effects test: Verify effects play on ability activation and impact
- [ ] Status indicator test: Verify active effects display as icons/particles on entities
- [ ] Animation test: Verify UI transitions and health bar animations work smoothly
- [ ] Performance test: Verify visual effects don't impact frame rate significantly

---

## Phase 6.10 — Advanced Features & Refinements

**Goal**: Additional features for richer gameplay

### 6.10.1 AI System (Basic)
- **Enemy AI Behavior**:
  - Idle: patrol or stand
  - Detect: chase player if in range
  - Combat: select abilities and targets
  - Flee: retreat if low HP (optional)
- **AI Decision Making**:
  - Simple priority system
  - Target selection (lowest HP, nearest, etc.)
  - Ability usage rules

### 6.10.2 Quest/Objective System (Optional)
- **Objective Types**:
  - Defeat X enemies
  - Reach location
  - Collect items (if inventory added)
- **Objective Tracking**:
  - Display current objectives
  - Progress indicators
  - Completion rewards

### 6.10.3 Save/Load System
- **Game State Serialization**:
  - Save current GameState to file
  - Serialize scenarios, entities, progress
  - Load saved game
- **Save Points**:
  - Designated locations in scenarios
  - Auto-save on scenario transition

### 6.10.4 Inventory System (Future)
- **Item Collection**:
  - Loot drops from enemies
  - Items in scenarios
- **Inventory Management**:
  - Carry capacity (based on Weight stat)
  - Item usage (consumables)
  - Equipment management

**Deliverables**:
- [ ] Basic enemy AI
- [ ] Quest/objective system (optional)
- [ ] Save/load functionality
- [ ] Inventory system (future)

**Testing Requirements**:
- [ ] AI behavior test: Verify enemy AI states (idle, detect, combat, flee) transition correctly
- [ ] AI decision test: Verify target selection and ability usage follow defined rules
- [ ] Save/load test: Verify GameState serialization preserves all scenarios, entities, and player contexts
- [ ] Scenario persistence test: Verify multiple scenarios save/load correctly (per-scenario architecture)
- [ ] Quest tracking test: Verify objectives track progress and trigger completion (if implemented)

---

## Potential Domain Refactoring Needs

Based on the Core Game Plan requirements and **Per-Scenario GameState architecture**, we need to refactor Pomo.Lib:

### Domain Extensions (Phase 6.1-6.7)
1. **Position Component**: Add to EntityComponents (Phase 6.1)
2. **Movement Component**: Speed, destination, path (Phase 6.3)
3. **Scenario Types**: Scenario, ScenarioState, PlayerContext, CollisionGeometry, TerrainObject, ObjectId, VisualLayer, TerrainType, ScenarioTransition (Phase 6.4)
4. **Combat Type System**: ScenarioCombatType enum (PvE/PvP/PvPvE) in Scenario (Phase 6.7)
5. **Party System**: Party, PartyId types for player grouping (Phase 6.7)
6. **Battle Context**: BattleContext type in ScenarioState (per-scenario) (Phase 6.7)
7. **Move Command**: Add to Rules.Command (Phase 6.3)
8. **Camera Type**: Camera for PlayerContext (Phase 6.1)

### GameState Refactoring (Phase 6.4-6.7) - Per-Scenario Architecture
**Before** (Current single-state):
```fsharp
type GameState = {
  entities: cmap<Guid<EntityId>, EntityComponents>
  gameTime: cval<int64<Tick>>
  services: EngineServices
}
```

**After** (Per-scenario state with party system):
```fsharp
type ScenarioState = {
  scenario: Scenario
  entities: cmap<Guid<EntityId>, EntityComponents>
  gameTime: cval<int64<Tick>>
  battleContext: BattleContext voption
}

type GameState = {
  scenarios: cmap<Guid<ScenarioId>, ScenarioState>
  players: cmap<int, PlayerContext>
  parties: cmap<Guid<PartyId>, Party>  // Phase 6.7: Track player parties
  services: EngineServices
}
```

### New Modules
- **ScenarioManager.fs**: Scenario loading, transitions, polygon collision queries
- **Collision.fs**: Polygon collision detection (point-in-polygon, circle-polygon, spatial partitioning)
- **Pathfinding.fs**: Grid overlay A* algorithm with polygon validation
- **Movement.fs**: Movement resolution and polygon collision validation

### GameStateOperations Extensions (Per-Scenario Architecture)
- moveEntity: Guid<EntityId> -> Position -> Guid<ScenarioId> -> GameState -> Result<StateChange, Error>
- canMoveTo: Position -> ScenarioState -> bool  // Polygon collision check
- queryTerrainObjects: Position -> float32 -> ScenarioState -> TerrainObject list  // Query objects within radius
- engageBattle: Guid<EntityId> list -> Guid<ScenarioId> -> GameState -> Result<StateChange, Error>
- disengageBattle: Guid<ScenarioId> -> GameState -> Result<StateChange, Error>
- transitionPlayerScenario: int -> Guid<ScenarioId> -> Position -> GameState -> Result<StateChange, Error>
- getPlayerScenario: int -> GameState -> ScenarioState voption
- tickScenario: int64<Tick> -> Guid<ScenarioId> -> GameState -> aval<StateChange>

---

## Implementation Timeline & Priorities

### Priority 1 (Core Validation)
- Phase 6.1: Basic Rendering ⭐⭐⭐
- Phase 6.2: Input & Targeting ⭐⭐⭐
- Phase 6.3: Movement (basic) ⭐⭐⭐

**Goal**: Validate core library works in real gameplay - see entities, move, use abilities

### Priority 2 (Scenario Foundation)
- Phase 6.4: Scenario System ⭐⭐
- Phase 6.5: Terrain-Aware Movement ⭐⭐
- Phase 6.6: Scenario Transitions ⭐⭐

**Goal**: Build scenario-based gameplay structure

### Priority 3 (Battle System)
- Phase 6.7: Battle Engagement ⭐⭐
- Phase 6.8: Detail Views ⭐

**Goal**: Complete battle system with context awareness

### Priority 4 (Polish & Extensions)
- Phase 6.9: Visual Polish ⭐
- Phase 6.10: Advanced Features (as needed)

**Goal**: Improve game feel and add depth

---

## Success Criteria

### Phase 6.1-6.3 Success (Core Validation)
✅ Can see player and enemy entities on screen
✅ Can move player by clicking screen
✅ Can activate ability using keyboard and target enemy
✅ See visual feedback: damage numbers, health bars update
✅ Core library integration validated
✅ Unit tests pass for Position, Movement, and Input components
✅ Integration tests verify input → StateChange → visual feedback flow

### Phase 6.4-6.6 Success (Scenarios & Per-Scenario Architecture)
✅ Per-scenario GameState architecture implemented and tested
✅ Can navigate terrain with polygon-based collision (walkable/blocked areas)
✅ Polygon collision detection works correctly (point-in-polygon, circle-polygon)
✅ Can move between 3+ different scenarios
✅ Pathfinding works correctly around polygon obstacles
✅ Scenario-specific rules enforced
✅ Entity migration between scenarios works correctly
✅ Multiple scenarios can be active simultaneously (split-screen support verified)
✅ Unit tests pass for Scenario, ScenarioState, PlayerContext, CollisionGeometry, TerrainObject, and ScenarioManager
✅ Integration tests verify scenario transitions preserve entity state

### Phase 6.7 Success (Battle System)
✅ Battle engages automatically when near hostile
✅ Combat abilities only work in battle context
✅ Peaceful scenarios have no battle mechanics
✅ Can disengage and return to exploration
✅ Per-scenario battle contexts work independently (split-screen verified)
✅ Battle engagement/disengagement tests pass

### Phase 6.8-6.10 Success (Complete)
✅ Can view detailed entity stats and equipment
✅ Visual polish makes game feel responsive
✅ Basic AI provides challenge
✅ Game is playable end-to-end
✅ Save/load system preserves per-scenario state correctly
✅ All unit and integration tests pass
✅ Performance tests show acceptable frame rates with visual effects

---

## Testing Strategy

### Unit Tests (Per Phase)
- **Domain Components**: Position, Movement, Scenario, ScenarioState, PlayerContext, CollisionGeometry, TerrainObject, VisualLayer, TerrainType
- **Collision Detection**: Point-in-polygon algorithm, circle-polygon intersection, spatial partitioning efficiency
- **ScenarioManager Operations**: createScenarioState, entity ownership, polygon collision queries
- **Per-Scenario Architecture**: Independent time, battle contexts, entity isolation
- **GameStateOperations**: All API functions with per-scenario parameters (canMoveTo, queryTerrainObjects)
- **Pathfinding**: Grid overlay A* with polygon validation, path correctness
- **AI Behavior**: State transitions, decision making

### Functional Tests
- Entity rendering at correct positions
- Input handling for all action types
- Ability activation through UI
- Movement respects terrain
- Scenario transitions preserve state (entity migration between scenarios)
- Battle engagement/disengagement rules (per-scenario contexts)
- Per-scenario time progression (independent ticking)
- Multi-scenario active state (split-screen support)

### Integration Tests
- MonoGame Update/Draw loop performance
- GameState queries from rendering (per-scenario entity lookups)
- Input → StateChange → Visual feedback flow
- Scenario loading and unloading (per-scenario state management)
- Entity migration during transitions (remove from old, add to new scenario)
- Split-screen rendering (multiple PlayerContext, multiple active scenarios)
- Save/load with per-scenario state preservation

### Per-Scenario Architecture Testing
- **Independent State**: Verify each scenario has own entities, time, battleContext
- **Entity Ownership**: Verify entities belong to correct scenario, no cross-references
- **Player Transitions**: Verify entity migration between scenarios preserves state
- **Multi-Player Support**: Verify multiple players in different scenarios simultaneously
- **Battle Isolation**: Verify one scenario in battle doesn't affect another
- **Performance**: Verify O(1) scenario lookup, no global entity filtering

### Playtesting Goals
- Core mechanics feel responsive
- Visual feedback is clear
- Navigation is intuitive
- Battle system is engaging
- Per-scenario architecture is transparent to player (no bugs, smooth transitions)
- Split-screen gameplay works smoothly (future)
- No major bugs or crashes

---

## Next Steps

1. **Start Phase 6.1**: Implement Position component and basic entity rendering
2. **Create Test Scenario**: Define simple test map for movement validation
3. **Establish Rendering Pipeline**: Set up SpriteBatch and camera system
4. **Verify Core Integration**: Ensure GameState queries work correctly in Draw loop

---

**Ready to begin implementation!**
