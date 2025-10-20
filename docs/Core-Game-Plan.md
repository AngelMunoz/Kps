# Core Game Plan - MonoGame Integration & Gameplay Systems

**Status**: 🚧 **PHASE 6.9 IN PROGRESS** - Implemented deferred resolution system for abilities. Projectiles now dynamically track targets and apply effects on collision, synchronizing visual feedback with gameplay impact.

**Created**: 2025-10-11
**Updated**: 2025-10-17

**Prerequisites**: Phases 0-5.5 of RPG-Core-Plan.md completed

---

## Overview

This document outlines the implementation plan for integrating the Pomo.Lib RPG framework with MonoGame to create a playable game experience. The focus is on visual feedback, player interaction, scenario-based gameplay, and battle system engagement.

**Primary Goals**:

1. **Visual Feedback**: See entities on screen with geometric shape placeholders
2. **Player Input**: Mouse/touch-based navigation and targeting
3. **Combat Interaction**: Activate abilities against enemies and see results
4. **Scenario System**: Navigate terrain with walkable/non-walkable areas
5. **Scenario Transitions & Teleports**: Move between different scenarios (supports explicit teleport command for dedicated engagement maps)
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
- Implemented simple camera follow (centers on player) and mouse wheel zoom (0.5x–2x).
- Implemented basic entity labels (Profession Family/Stage) above health bars using Hud font.
- Implemented circle rendering sized by stage and extracted a dedicated RenderSystem module.

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

- [x] Position component added to Domain
- [x] RenderSystem.fs with entity rendering
- [x] Health bar rendering
- [x] Camera system with follow and zoom
- [x] Visual verification: see 2 entities (player, enemy) on screen with health bars

**Testing Requirements**:

- [x] Integration test: Create entities with positions and verify rendering output
- [x] Visual test: Confirm entities render at correct screen positions
- [x] Camera test: Verify camera follow and zoom functionality
- [x] Health bar test: Verify health bars update when entity resources change

---

## Phase 6.2 — Input & Targeting System

**Goal**: Interact with entities through mouse/touch and keyboard

**Status**: ✅ **COMPLETE** (2025-10-11)

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

- [x] InputManager.fs with mouse/touch/keyboard handling
- [x] Entity selection with visual highlighting
- [x] Targeting system for all ability types
- [x] Ability activation integrated with core library
- [x] Visual feedback for ability use (placeholder animations)

**Testing Requirements**:

- [x] Unit tests for InputAction parsing and screen-to-world coordinate conversion
- [x] Integration test: Simulate mouse clicks and verify entity selection
- [x] Targeting test: Verify each targeting mode (Self, SingleTarget, MultiTarget, AoE) works correctly
- [x] Ability activation test: Trigger ability via input and verify StateChange is applied
- [x] Input validation test: Verify invalid targets are rejected appropriately

---

## Phase 6.3 — Movement & Navigation

**Goal**: Move entities within a scenario

**Status**: 🚧 **IN PROGRESS**

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
  - Right-click screen position to set player destination.
  - Entity moves toward destination.
- **Visual Feedback**:
  - Animate entity position.
  - Show movement destination marker (future).
  - Trail/path visualization (future).

### 6.3.3 Movement Constraints (Basic)

- **Boundary Checking**:
  - Define scenario bounds
  - Clamp movement to within bounds
- **Collision Detection** (simple):
  - Entities cannot overlap (radius-based)
  - Stop movement if collision detected

**Deliverables**:

- [x] Movement component in Domain
- [x] Move command and resolution
- [x] Click-to-move input handling
- [x] Movement animation and visual feedback
- [x] Basic boundary and collision checking (implemented via temporary `GameState.bounds` and simple radius-based entity collision)

**Testing Requirements**:

- [x] Unit tests for Movement component and path calculation
- [x] Movement resolution test: Verify Move command updates entity position correctly
- [x] Boundary test: Verify entities cannot move outside scenario bounds (`ScenarioBounds` surrogate on `GameState`)
- [x] Collision test: Verify entities cannot overlap (radius-based collision)
- [x] Integration test: Click-to-move flow from input to position update

### 6.3.4 Interim Bounds Implementation Rationale

For Phase 6.3 we introduced a lightweight rectangular bounds record (`ScenarioBounds`) directly on `GameState` (`gameState.bounds`) to enable early boundary clamping and collision without committing to the full per-scenario architecture. This is an intentionally transitional design:

- Keeps movement/collision logic simple while validating gameplay feel.
- Avoids premature refactor to multi-scenario state before rendering/input loops are stable.
- The field will be migrated/replaced in Phase 6.4 when the first `Scenario` is introduced; at that point bounds become part of `Scenario` and `GameState.bounds` is removed.
- Collision currently uses only entity radii + rectangle clamping; polygon terrain collision will supersede this in Phase 6.5.

No long-term coupling is created: movement update function already accepts bounds as a value parameter, making the migration to per-scenario trivial (pass `scenario.bounds` instead of `gameState.bounds`).

---

## Phase 6.4 — Scenario System Foundation

**Goal**: Define scenarios with terrain and rules

**Status**: ✅ **COMPLETE** (2025-10-12) - Full Phase 6.4 scaffolding implemented

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
  | PvP          // Player vs Player - players can target enemy players (not in the same party)
  | PvH        // Player vs Player vs Environment - players can target both enemy players and NPCs

// Base scenario configuration (polygon-based collision)
type Scenario = {
  Id: Guid<ScenarioId>
  Name: string
  BoundsWidth: float32   // World bounds in units (not tiles)
  BoundsHeight: float32
  TerrainObjects: TerrainObject list  // Polygonal collision objects
  VisualLayers: VisualLayer list      // Background/foreground sprites (no collision)
   bool
  CombatType: ScenarioCombatType  // Defines targeting rules (PvE/PvP/PvH)
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

- [x] Scenario domain types in Pomo.Lib (Scenario, ScenarioState, ScenarioId, CollisionGeometry, TerrainObject, ObjectId, VisualLayer, TerrainType, ScenarioTransition all implemented)
- [x] ScenarioManager module with per-scenario operations (createScenarioState, getScenarioState, addEntityToScenario, removeEntityFromScenario, listScenarios)
- [x] GameState refactored to per-scenario architecture (scenarios: cmap, activeScenarioId implemented; temporary `GameState.bounds` removed)
- [ ] Polygon-based terrain rendering system with depth sorting
- [ ] Sample scenario definition with TerrainObjects (test map with organic shapes)
- [x] Migration from single GameState to per-scenario architecture (entities relocated into ScenarioState)

**Testing Requirements**:

- [x] Unit tests for Scenario, ScenarioState, CollisionGeometry, and TerrainObject creation
- [x] ScenarioManager test: Create scenario state and verify entity ownership
- [x] Per-scenario time test: Verify each scenario has independent gameTime
- [x] Player context test: Add player to scenario and verify currentScenarioId tracking
- [x] Entity ownership test: Verify entities belong to correct scenario (no cross-scenario references)
- [x] Collision geometry test: Verify Circle and Polygon collision shapes work correctly
- [x] Depth sorting test: Verify DepthLayer correctly orders visual elements
- [x] Integration test: Create multiple scenarios and verify independent state management

### 6.4.4 Phase 6.4 Implementation Summary (2025-10-12)

**✅ COMPLETED DELIVERABLES**:

1. **Core Domain Types** (Domain.fs):

   - `ObjectId` measure type for terrain objects
   - `TerrainType` struct DU: `Walkable | Blocked | Water | Hazard`
   - `CollisionGeometry` struct DU: `Circle | Polygon | NoCollision`
   - `TerrainObject` struct record with collision, terrain type, depth layer
   - `VisualLayer` struct record with parallax support

2. **Enhanced Scenario System** (Scenario.fs):

   - `ScenarioCombatType`: `PvE | PvP | PvH` for targeting rules
   - `ScenarioTransition`: Portal/door connections between scenarios
   - Extended `Scenario` type with terrain objects, visual layers, battle settings
   - Consolidated `ScenarioManager` module with core operations

3. **Per-Scenario Architecture**:

   - `GameState.create'` initializes default "Test Scenario" with new structure
   - All existing movement/collision code updated to work with new bounds structure
   - Legacy `ScenarioBounds` references migrated to separate width/height fields

4. **Comprehensive Testing** (ScenarioTests.fs):

   - Unit tests for scenario creation, entity management, and domain types
   - **80 total tests passing** (8 new scenario tests + existing suite)
   - Full build verification across Pomo.Lib, Pomo.Core, Pomo.DesktopGL

5. **Documentation Updates**:
   - `game-definitions.md` updated to reflect `NoCollision` vs `None`
   - All type definitions align with implementation

**🎯 PHASE 6.4 SUCCESS CRITERIA MET**:

- ✅ Domain scaffolding complete for polygon-based collision system
- ✅ Per-scenario terrain object management ready
- ✅ Visual layer system foundation established
- ✅ Scenario transition framework implemented
- ✅ Combat type system (PvE/PvP/PvH) ready for Phase 6.7
- ✅ ScenarioManager operations for entity management working
- ✅ All tests passing (80/80) with no build errors
- ✅ Existing movement/input/rendering functionality preserved

**🚀 READY FOR PHASE 6.5**: Terrain-Aware Movement & Pathfinding

---

## Phase 6.5 — Terrain-Aware Movement & Pathfinding

**Goal**: Entities respect terrain and navigate intelligently

Progress Update (2025-10-13) - **PR #5 COMPLETE**:

✅ **Phase 6.5 - Terrain-Aware Movement & Pathfinding (COMPLETE)**:

- **Collision Detection**: Full `Collision.fs` module with point-in-polygon, circle-polygon, circle-circle algorithms
- **Advanced Pathfinding**: Complete A\* implementation with entity-aware grid generation, diagonal movement, cost penalties (water 2x, proximity to entities +1.5x)
- **Terrain Speed Modifiers**: Water areas reduce speed to 0.5x, hazard areas to 0.7x, plus dexterity-based scaling
- **Path Preview System**: Real-time validity checking with terrain type detection and segment-by-segment visualization
- **Dynamic Path Recalculation**: Automatic rerouting when blocked by moving entities or terrain changes
- **Enhanced Movement System**: Waypoint following, path continuity validation, entity collision avoidance
- **Visual Integration**: Grid overlay toggle (Key 2), path preview lines, waypoint markers, terrain object rendering

✅ **Phase 6.6 - Scenario Transitions (COMPLETE)**:

- **Transition Detection**: Proximity-based triggers with configurable ranges and conditional requirements
- **Full Entity Migration**: Complete state preservation during scenario switches (HP, MP, equipment intact; movement reset)
- **Visual Transition Effects**: Fade in/out system with progress tracking and alpha blending
- **Multi-Scenario Definitions**: Connected Town → Wilderness → Dungeon with varied terrain, combat types, and portal networks
- **Comprehensive Test Coverage**: 12 new transition tests + 10 pathfinding tests (22 tests total; 101 total suite)

🎮 **Gameplay Integration**: Right-click pathfinding movement, Key 2 grid toggle, visual portal markers, terrain rendering, real-time path calculation with entity awareness, complete scenario transition system.

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
  - Use A\* on grid for pathfinding
  - Final path validation: ensure waypoints don't intersect collision polygons
  - Cost function: distance, terrain type, entity speed
- **Option B: Navigation Mesh (Advanced, Future)**:
  - Define walkable areas as polygons (NavMesh)
  - A\* over polygon graph for precise pathfinding
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

**Deliverables (✅ COMPLETE)**:

- [x] **Collision detection module** — Complete `Collision.fs` with geometry algorithms and optimized IndexList queries
- [x] **Terrain-aware pathfinding** — Full A\* implementation with entity avoidance and dynamic recalculation
- [x] **Movement speed variation** — Water (0.5x), Hazard (0.7x) modifiers + dexterity scaling integrated into `Movement.fs`
- [x] **Path preview visualization** — Real-time segment validity + terrain type detection with green/red rendering
- [x] **Entity-aware navigation** — Pathfinding considers other entity positions and radii with proximity penalties
- [x] **Advanced movement system** — Waypoint following, path continuity checks, dynamic obstacle avoidance

**Testing Requirements (✅ COMPLETE)**:

- [x] **Comprehensive pathfinding tests** — 10 new tests covering grid creation, world/grid conversion, terrain detection, A\* algorithms, entity-aware pathfinding, impossible path handling
- [x] **Collision geometry validation** — Grid cell walkability based on polygon/circle collision detection
- [x] **Path preview testing** — Segment validity, terrain type identification, blocked/valid path visualization
- [x] **Terrain cost integration** — Water penalty (2.0x cost), entity proximity penalties (+1.5x per nearby entity)
- [x] **Movement system validation** — Waypoint following, path continuity, dynamic recalculation when blocked
- [x] **Edge case handling** — Unreachable destinations return `ValueNone`, impossible paths correctly rejected

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

**Status (2025-10-13)**: ✅ **COMPLETE** - Full scenario transition system implemented with comprehensive testing.

**Deliverables (✅ COMPLETE)**:

- [x] **Proximity detection system** — `TransitionDetection.checkProximity` with 32.0f range and conditional validation
- [x] **Complete transition execution** — `TransitionExecution.executeTransition` with entity migration and scenario switching
- [x] **Entity state preservation** — Resources, equipment, effects preserved; movement state reset appropriately
- [x] **Visual transition effects** — Complete fade in/out system with progress tracking and alpha blending
- [x] **Connected scenario network** — Town (peaceful, PvE) ↔ Wilderness (battle, PvE) ↔ Dungeon (battle, PvH)
- [x] **Advanced scenario definitions** — Varied terrain objects, visual layers, combat types, and portal connections
- [x] **Runtime transition detection** — Integrated into game loop with automatic proximity checking
- [x] **Portal visualization** — Purple-framed transition points with interior highlights

**Testing Requirements (✅ COMPLETE)**:

- [x] **Transition detection test** (distance threshold correctness, nearby vs distant entities)
- [x] **Entity migration test** (verify entity migration preserves ID and state integrity)
- [x] **State preservation test** (resources, equipment, movement state correctly handled)
- [x] **Conditional transition test** (RequiresCondition validation with passing/failing scenarios)
- [x] **Visual transition effects test** (fade alpha calculation and progress tracking)
- [x] **Connected scenario network test** (town ↔ wilderness ↔ dungeon with proper links)
- [x] **Scenario layout validation** (terrain objects, combat types, transitions per scenario type)

**Note**: PlayerContext update and multi-scenario isolation require full game state integration (future work).

---

## Phase 6.7 — Battle Engagement System

**Goal**: Control when battle mechanics are active and enforce targeting rules while introducing engagement models that extend (not replace) existing combat type and party logic.

**Status**: ✅ **COMPLETE** (2025-10-15)

**Progress Update (2025-10-15):**

- The domain types for `EngagementMode`, `AbilityIntent`, and `BattleInstance` are fully defined and integrated into the `Scenario` and `ScenarioState` records.
- The `Engagement.canUseAbility` function correctly reads the `EngagementMode` and `AbilityIntent` to gate actions.
- Logic for `EngagementMode.Peaceful` (blocking offensive actions) and `EngagementMode.AlwaysOn` (enforcing `ScenarioCombatType` rules) is implemented and functional.
- Core targeting rules for PvE, PvP, and PvH, including party-based friendly-fire prevention, are correctly enforced.
- Implemented the logic for `EngagementMode.Structured` to validate that combatants are part of an active `BattleInstance`.
- Created the system for managing `BattleInstance` lifecycles (creation, joining, leaving) in `BattleManager.fs`.
- Added tests for the `BattleInstance` lifecycle management.
- Implemented the duel request/accept/cancel API in a pure, functional way.
- Introduced `ScenarioChange` to represent scenario-level state changes, and integrated it into the `StateChange` record.
- The `Resolution` module now handles `DuelCommand` and produces `ScenarioChange` objects.
- The `Gameplay` module's `apply` function now processes `ScenarioChange` objects, applying them to the active scenario.
- Added comprehensive tests for the duel API, covering different scenarios and engagement modes.
- Added `PartyId` to `EntityComponents` to associate entities with parties.
- Added a `parties` collection to `ScenarioState` to manage parties within a scenario.
- Implemented `accept` and `cancel` functions for party duels in `BattleManager.fs`.
- Added comprehensive tests validating all success criteria.

### 6.7.1 Existing Foundations

- ScenarioCombatType (PvE | PvP | PvH) defines base target eligibility.
- flag toggles whether combat processing can occur at all.
- BattleContext (single per scenario) concept for scenario-wide battles.
- Party system (PartyId, Party) prevents offensive actions against members.

These remain unchanged; new features layer on top without removing prior behavior.

### 6.7.2 Augmented Concepts

- EngagementMode: Peaceful | AlwaysOn | Structured

  - Peaceful: No offensive actions; support allowed.
  - AlwaysOn (Wild): Offensive actions allowed immediately; no structured engagements (duels/party battles blocked). Combines with PvE or PvH for ambient hostility.
  - Structured: Offensive actions require an active BattleInstance (applies only if CombatType is PvP or PvH). PvE scenarios and AlwaysOn mode cannot host structured engagements.

- AbilityIntent: Offensive | Support | Neutral used to gate activation early. (may be needed in AbilityDefinition)
- BattleInstance: Adds instance-local engagements for duels/party battles coexisting with ambient scenario state.
- scenarioWideContext vs battleInstances: scenarioWideContext retains original single-context model for large events; battleInstances provide isolated structured engagements.

### 6.7.3 Targeting Rules (Preserved + Extended)

Base validation (existing): CombatType + party membership + ability targeting definition.

Extended pipeline order:

1. AbilityIntent classification.
2. EngagementMode gate (Peaceful reject Offensive; AlwaysOn allow; Structured require membership in active BattleInstance where allowed).
3. CombatType rules (unchanged logic for PvE/PvP/PvH).
4. Party friendly-fire rejection (applies in all modes).
5. Ability-specific targeting (range, count limits, AoE).

### 6.7.4 Lifecycle Scenarios

Wild Zone (AlwaysOn): retains original hostile proximity triggers; new rule: duel requests auto-reject.
Peaceful Zone: existing false semantics reinforced by EngagementMode Peaceful gating Offensive early.
Structured In-Place Duel: layered on top of PvP/PvH; original targeting still applies; only participants can execute Offensive against each other.
Structured Teleport Duel: uses existing ScenarioTransition system; ambient Plaza scenario unchanged for non-participants.

### 6.7.5 Party System

Parties continue to form in any CombatType and EngagementMode; new logic does not alter membership rules—only integrates party check into extended pipeline.

### 6.7.6 Peaceful Enforcement

Original peaceful mechanics (no battle) extended by AbilityIntent gating; supportive abilities continue unaffected; effects/cooldowns process normally.

### 6.7.7 Teleport vs In-Place Duel

Adds structured choice without removing baseline scenario combat. Teleport leverages existing transition; in-place leverages new BattleInstance list.

### 6.7.8 Domain Additions

Add EngagementMode, AbilityIntent, BattleInstance, scenarioWideContext (optional predecessor retained), battleInstances collection. Existing ScenarioCombatType and BattleContext unchanged.

### 6.7.9 Deliverables

- [x] Preserve ScenarioCombatType, BattleContext functionality.
- [x] Implement EngagementMode with restrictions (no Structured in PvE or AlwaysOn).
- [x] Implement AbilityIntent classification.
- [x] Implement BattleInstance (duel/party) list on ScenarioState.
- [x] Validation function canUseAbility applying extended pipeline.
- [x] Duel request/accept/cancel API rejecting invalid mode/combat type combos.
- [x] Tests covering legacy behavior (combat type targeting, party friendly-fire) plus new engagement gating.

### 6.7.10 Success Criteria

- [x] Peaceful zones block Offensive (EngagementMode) while support remains.
- [x] AlwaysOn zones allow immediate hostile actions and reject structured duel creation.
- [x] Structured PvP/PvH zones allow duel/party BattleInstances without affecting non-participants.
- [x] Teleport duel isolates combat; scenarios unaffected.
- [x] Party friendly-fire blocked across all modes.
- [x] Tests validate both pre-existing and new pathways.

#### Legacy: Battle Context & Combat Types

```fsharp
// Scenario combat type determines targeting rules
[<Struct>]
type ScenarioCombatType =
  | PvE          // Player vs Environment (default) - players cannot target other players but can target NPCs
  | PvP          // Player vs Player - players can target enemy players only (not in the same party or NPCs)
  | PvH          // Player vs Hostile - players can target both enemy players and NPCs


// Party system for player grouping
[<Measure>]
type PartyId

type Party = {
  Id: Guid<PartyId>
  Members: HashSet<Guid<EntityId>>
  Name: string
}

type BattleContext = {
  IsActive: bool
  Participants: HashSet<Guid<EntityId>>
  StartTick: int64<Tick>
  CanDisengage: bool
}
```

#### Scenario and GameState Fields

```fsharp
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

#### Targeting Rules by Combat Type

**PvE Scenarios (Default)**:

- **Allowed Targets**: Non-player entities (NPCs, enemies, monsters)
- **Forbidden Targets**: Other player entities (regardless of party affiliation) and Allied Faction NPCs
- **Use Case**: Towns, cooperative dungeons, story scenarios
- **Validation**: `isPlayerEntity(target) = false`, `isAllyFaction(target)` for all offensive abilities

**PvP Scenarios**:

- **Allowed Targets**:
  - Enemy players (players not in the same party as the actor)
- **Forbidden Targets**: Allied players (players in the same party as the actor), NPCs
- **Use Case**: Arenas, dueling zones, competitive areas
- **Validation**:
  - If `isPlayerEntity(target)`: Check `notInSameParty(actor, target)`
  - NPCs always invalid target

**PvH Scenarios**:

- **Allowed Targets**:
  - Enemy players (players not in the same party as the actor)
  - Non-player entities (NPCs, enemies, monsters)
- **Forbidden Targets**: Allied players (players in the same party as the actor), Allied Faction NPCs
- **Use Case**: Open-world PvP zones, faction warfare, competitive PvE
- **Validation**:
  - If `isPlayerEntity(target)`: Check `notInSameParty(actor, target)`
  - If `isNPC(target)`: Check `notAllyFaction(target)`

**Friendly Abilities Exception**: Abilities have this `ActiveAbilityDefinition.TargetingType` field where you can check whether the ability is friendly or offensive.

```fsharp
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
```

Please check `Pomo.Lib/Domain.fs` for the full definition.

If multi-target abilities that are ally/party specific require disambiguation, a new DU case may be aded e.g

```fsharp
    | MultiTargetAllies of int
```

But first check when implementing if such case is required.

**NOTE**: Healing/support abilities are exempt from targeting restrictions and can always target allies (including self).

### 6.7.3 Battle Engagement Rules

- **Engagement Triggers**:
  - User Enters into a hostile scenario, the battle mechanics are enabled always.
  - Forced battle (boss encounters, story events) when battle is triggered only.
  - Player-to-player aggression in PvP/PvPvE scenarios
  - Hostile enemies within aggro range (configurable, e.g., 10.0f units)
  - Player requested engagement (e.g., attacking a hostile entity)
  - First hostile action (ability use, attack) if the scenario is battle-enabled
- **Engagement Effects**:
  - Enable combat abilities
  - Start effect/cooldown processing
  - Enforce targeting rules based on scenario combat type
- **Disengagement**:
  - All hostiles defeated (boss encounters, story events)
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
- **Solo Players**:
  - Treated as single-member party
  - Can target any valid enemy based on scenario type

### 6.7.5 Peaceful Scenario Behavior

- Battle disabled scenarios: no hostile detection, combat abilities disabled, support abilities allowed, passive effects continue, targeting rules enforced for supportive abilities.

#### Visual Indicators

- Battle engaged indicator, participant highlighting, party member markers, valid/invalid target feedback.

All of the above remains valid; new EngagementMode and BattleInstance mechanics do not alter these foundations but add gating and structuring on top.

---

## Phase 6.8 — Personal Entity Detail Views

**Goal**: Inspect entity stats, equipment, and abilities

**Status**: ✅ **COMPLETE** (2025-01-15)

**Progress Update (2025-01-15):**

- Complete UI system implemented with toggleable panels and keyboard controls
- Character Sheet displays profession, resources (HP/MP), base stats, and derived stats
- Equipment View shows all 7 equipment slots with equipped items or "(Empty)" status
- Ability List displays known abilities with cooldown status (Ready/Cooldown)
- Clean, readable UI with proper panel borders, titles, and color coding
- Real-time data binding from GameState with entity selection integration
- Keyboard shortcuts: V (Character Sheet), E (Equipment), A (Abilities)

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
  - Toggleable UI panels ("V" keyboard shortcut = character sheet)
  - Modal dialogs for detailed views
  - Responsive layout for different screen sizes
- **Data Binding**:
  - Use GameStateOperations queries
  - Update on state change

**Deliverables (✅ COMPLETE)**:

- [x] **Character sheet UI with stats display** — Complete UISystem.fs with drawCharacterSheet function
- [x] **Equipment view with slots** — All 7 equipment slots displayed with equipped items or "(Empty)" status
- [x] **Ability list panel** — Known abilities displayed with cooldown status (Ready/Cooldown)
- [x] **UI toggle controls (keyboard shortcuts)** — V, E, A keys toggle Character Sheet, Equipment, Abilities
- [x] **Data binding and real-time updates** — GameState integration with entity selection

**Testing Requirements (✅ VERIFIED)**:

- [x] **Stats display test** — Derived stats (AP: 30, AC: 33, DX: 15, etc.) calculated and displayed correctly
- [x] **Equipment slot test** — All 7 slots render correctly (Head, Chest, Legs, Hands, Weapon1, Weapon2, Accessory)
- [x] **Ability list test** — Abilities display with correct cooldown status and color coding
- [x] **Data binding test** — UI updates correctly when entity is selected, real-time GameState integration
- [x] **UI interaction test** — Keyboard shortcuts (V, E, A) toggle panels correctly, multiple panels can be active

### 6.8.5 Phase 6.8 Implementation Summary (2025-01-15)

**✅ COMPLETED DELIVERABLES**:

1. **Complete UI System** (UISystem.fs):

   - UIState management with active panels and selected entity tracking
   - Panel toggle functions with keyboard integration
   - SpriteBatch rendering with proper Begin/End calls

2. **Character Sheet Panel**:

   - Profession display (Power/First)
   - Resource display (HP: 120, MP: 25, Status: Alive)
   - Base stats (Power: 15, Magic: 5, Sense: 8, Charm: 12)
   - Derived stats (AP: 30, AC: 33, DX: 15, MA: 10, MD: 11, WT: 40)

3. **Equipment View Panel**:

   - All 7 equipment slots with proper labeling
   - "(Empty)" status for unequipped slots
   - Equipment name display for equipped items

4. **Ability List Panel**:

   - Known abilities display with ID numbers
   - Cooldown status with color coding (Green = Ready, Gray = Cooldown)
   - Real-time cooldown tracking

5. **UI Integration** (PomoGame.fs):
   - Keyboard controls (V, E, A) with state tracking
   - Entity selection integration
   - Console output for UI control instructions

**🎯 PHASE 6.8 SUCCESS CRITERIA MET**:

- ✅ Character sheet displays comprehensive entity information
- ✅ Equipment view shows all slots with proper status
- ✅ Ability list shows cooldown status with visual feedback
- ✅ Keyboard shortcuts work correctly (V, E, A)
- ✅ Multiple panels can be active simultaneously
- ✅ Real-time data binding from GameState
- ✅ Clean, readable UI with proper formatting
- ✅ Entity selection integration working

**🚀 READY FOR PHASE 6.9**: Enhanced Visual Feedback & Polish

---

## Phase 6.9 — Enhanced Visual Feedback & Polish

**Goal**: Improve game feel with animations and effects

### 6.9.1 Ability Visual Effects

- **Damage Numbers**:
  - Floating text showing damage/healing amounts
  - Color-coded (damage = red, heal = green, critical = yellow)
  - Animation: rise and fade
- **Ability Animations**:
  - **Dynamic Projectiles**: Projectiles for ranged abilities now dynamically follow their targets. The visual effect is synchronized with the gameplay impact, which is determined by collision detection. The resolution of the ability's effects (e.g., damage) is deferred until the projectile physically hits the target, ensuring visual and gameplay consistency even if the target moves.
  - Area indicators for AoE
  - Impact effects on targets
- **Status Effect Indicators**:
  - Icon overlays on entities for buffs/debuffs implemented.
  - Particle effects for buffs/debuffs

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

- [x] Damage number system
- [x] Ability visual effects (basic)
- [x] Status effect indicators
- [x] Sound effects (optional)
- [ ] UI animations and polish

**Testing Requirements**:

- [x] Damage numbers test: Verify numbers display with correct values and colors
- [x] Visual effects test: Verify effects play on ability activation and impact
- [x] Status indicator test: Verify active effects display as icons/particles on entities
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
4. **Combat Type System**: ScenarioCombatType enum (PvE/PvP/PvH) in Scenario (Phase 6.7)
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
- **Pathfinding.fs**: Grid overlay A\* algorithm with polygon validation
- **Movement.fs**: Movement resolution and polygon collision validation

### GameStateOperations Extensions (Per-Scenario Architecture)

- moveEntity: Guid<EntityId> -> Position -> Guid<ScenarioId> -> GameState -> Result<StateChange, Error>
- canMoveTo: Position -> ScenarioState -> bool // Polygon collision check
- queryTerrainObjects: Position -> float32 -> ScenarioState -> TerrainObject list // Query objects within radius
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
✅ Pathfinding works correctly around polygon obstacles (grid A\* implemented; further validation for unreachable scenarios pending)
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
  -- **Collision Detection**: Point-in-polygon, circle-polygon, circle-circle; spatial partitioning performance (future).
  -- **ScenarioManager Operations**: createScenarioState, entity ownership, polygon collision queries (IndexList linear scan now; partitioning later).
  -- **Per-Scenario Architecture**: Independent time; battle contexts pending addition.
  -- **GameStateOperations Extensions Needed**: moveEntity, transitionPlayerScenario, engageBattle/disengageBattle.
  -- **Pathfinding**: Grid overlay A\*; cost influence & unreachable goal handling.
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
