# Pomo.Lib

Pomo.Lib main goal is to an adaptive library for real time rpg games.

It should provide a way to define characters, abilities, items and provide game systems to easily integrate with different game engines like MonoGame, Godot, etc.

## Architecture

### Per-Scenario GameState Architecture

The game uses a **Per-Scenario GameState architecture** to support split-screen and network multiplayer scenarios. Each scenario owns its own state independently, allowing:

- **Multiple active scenarios simultaneously**: Different players in different locations
- **Independent battle contexts**: One scenario in combat, another in peaceful exploration
- **Independent time progression**: Each scenario ticks independently
- **Efficient rendering**: Direct scenario lookup for split-screen viewports

**Key Types**:
- **ScenarioState**: Contains scenario definition, entities, gameTime, and battleContext
- **PlayerContext**: Tracks player's current scenario, controlled entity, and camera
- **GameState**: Root state containing all scenarios, players, parties, and services

## Spatial System

### Position Component

Entities have a **Position** component that defines their location in 2D space:
- **X**: Horizontal coordinate (float32)
- **Y**: Vertical coordinate (float32)

Position is used for rendering, collision detection, and movement calculations.

### Movement Component

Entities can move through scenarios with the **Movement** component:
- **Speed**: Movement rate in units per second (float32)
- **Destination**: Target position (Position voption)
- **Path**: List of waypoints to follow (Position list)

Movement respects terrain constraints and collision boundaries. Different terrain types can affect movement speed (e.g., Water slows movement).

### Camera System

Each player has a **Camera** for viewing the game world:
- Follows the player's controlled entity
- Supports zoom controls
- Converts between screen and world coordinates

## Scenario System

### Scenario

A **Scenario** represents a distinct game area with its own rules and content:
- **ScenarioId**: Unique identifier (Guid<ScenarioId>)
- **Name**: Human-readable name
- **BoundsWidth/BoundsHeight**: World bounds in units (float32)
- **TerrainObjects**: List of collision objects with terrain properties
- **VisualLayers**: Background/foreground sprites without collision
- **BattleEnabled**: Whether combat mechanics are active (bool)
- **CombatType**: Targeting rules for this scenario (ScenarioCombatType)
- **Transitions**: Portals/doors to other scenarios

### Terrain Types

Terrain defines the properties of different areas:
- **Walkable**: Standard passable terrain
- **Blocked**: Impassable obstacles
- **Water**: Passable but slower movement
- **Hazard**: Causes damage over time

### Collision Geometry

Collision uses **polygon-based shapes** for organic, natural boundaries (not tile-based grids):
- **Circle**: Defined by center point and radius
- **Polygon**: Arbitrary convex polygon with vertex list
- **None**: Visual-only objects without collision

This allows for 2.5D graphics with natural boundaries for objects like corals, trees, and rocks.

### Terrain Objects

**TerrainObject** represents physical objects in a scenario:
- **ObjectId**: Unique identifier (Guid<ObjectId>)
- **Position**: Location in world space
- **CollisionGeometry**: Shape for collision detection
- **TerrainType**: Terrain properties (Walkable, Blocked, etc.)
- **DepthLayer**: Z-order for 2.5D rendering (0.0 = background, 1.0 = foreground)
- **SpriteId**: Visual representation reference (optional)

### Visual Layers

**VisualLayer** provides background/foreground graphics without collision:
- **SpriteId**: Visual asset reference
- **Position**: Location in world space
- **DepthLayer**: Z-order for rendering
- **Parallax**: Scrolling speed multiplier for depth effects

### Scenario Transitions

**ScenarioTransition** defines connections between scenarios:
- **FromPosition**: Trigger location in current scenario
- **ToScenarioId**: Target scenario identifier
- **ToPosition**: Arrival location in target scenario
- **RequiresCondition**: Optional validation function (e.g., requires key item)

Transitions allow entity migration between scenarios while preserving stats, equipment, and effects.

## Combat System

### Scenario Combat Types

**ScenarioCombatType** determines targeting rules for each scenario:

- **PvE (Player vs Environment)**: Default mode
  - Players can target NPCs and monsters only
  - Players **cannot** target other players
  - Use case: Towns, cooperative dungeons, story scenarios

- **PvP (Player vs Player)**:
  - Players can target enemy players (not in same party)
  - Players can target NPCs and monsters
  - Players **cannot** target party members with offensive abilities
  - Use case: Arenas, dueling zones, competitive areas

- **PvPvE (Player vs Player vs Environment)**:
  - Players can target both enemy players and NPCs
  - Players **cannot** target party members with offensive abilities
  - Use case: Open-world PvP zones, faction warfare

**Friendly Abilities Exception**: Healing and buff abilities can target party members in all combat types.

### Party System

**Party** groups players together for cooperative play:
- **PartyId**: Unique identifier (Guid<PartyId>)
- **Members**: Set of player entity IDs (HashSet<Guid<EntityId>>)
- **Name**: Party name

**Party Benefits**:
- Friendly fire protection (cannot target party members with offensive abilities)
- Shared targeting restrictions
- Coordinated strategies

Solo players are treated as single-member parties.

### Battle Context

**BattleContext** manages combat engagement state per scenario:
- **IsActive**: Whether battle mechanics are currently engaged (bool)
- **Participants**: Entities involved in combat (HashSet<Guid<EntityId>>)
- **StartTick**: When battle began (int64<Tick>)
- **CanDisengage**: Whether participants can flee (bool)

**Battle Engagement**:
Triggered by hostile entity proximity, forced encounters, or player-initiated combat. When engaged:
- Movement may be restricted
- Combat abilities are enabled
- Effect and cooldown processing is active
- Targeting rules enforced based on scenario combat type

**Battle Disengagement**:
Occurs when all hostiles are defeated, flee action succeeds, or scenario transition happens.

**Peaceful Scenarios**:
When `BattleEnabled = false`:
- No hostile detection
- Combat abilities are disabled/grayed out
- Passive effects still process
- Full movement freedom
- Targeting rules still apply

## Input System

### Input Actions

**InputAction** represents player input commands:
- **NavigateTo**: Click/tap to move to target position (Vector2)
- **SelectEntity**: Click/tap to select entity (Guid<EntityId>)
- **ActivateAbility**: Press hotkey (0-9) or UI button to use ability (int)
- **ConfirmTarget**: Finalize target selection (Guid<EntityId>[])
- **CancelAction**: Cancel current action/targeting

### Targeting Modes

Ability activation uses context-aware targeting based on ability type:
- **Self**: Auto-targets actor, no selection needed
- **SingleAlly/SingleEnemy**: Click to select one target entity
- **MultiTarget**: Click multiple entities (with max count limit)
- **AoE (Area of Effect)**: Drag to show area indicator, release to confirm

Visual feedback indicates valid (green) and invalid (red/grayed) targets during selection.

## Rendering System

### 2.5D Depth Ordering

The rendering system uses **DepthLayer** for pseudo-3D visual ordering:
- **DepthLayer**: Float value where 0.0 = background, 1.0 = foreground
- **Y-Sorting**: Entities further down (higher Y) render in front (pseudo-3D effect)
- **Combined Ordering**: DepthLayer and Y-position provide fine control

Example: Tree trunk in front of player, but player in front of tree leaves.

### Pathfinding

Movement uses **polygon-aware pathfinding**:
- **Grid Overlay Approach**: Generate coarse navigation grid over scenario bounds
- **Polygon Validation**: Mark grid cells as walkable/blocked based on polygon overlaps
- **A* Algorithm**: Calculate optimal path on grid
- **Path Validation**: Ensure waypoints don't intersect collision polygons
- **Dynamic Recalculation**: Update path if obstacles change

Cost function considers distance, terrain type, and entity speed.

## Damage Types

There are two primary damage types in the engine:

- Physical

  Physical damage is dealt by physical weapons, swords, guns, etc.

- Magical

  Magical damage is dealt by spells, magic weapons, etc.

Elemental damage is calculated as additional damage that can be applied to both physical and magical attacks through formulas. Each formula returns both base damage and elemental damage components.

### Elemental Types

- Fire
- Water
- Earth
- Air
- Lightning
- Light
- Dark
- Neutral

## Character related Definitions

### Stats

Stats are a set of numbers that define a character's growth potential. These are defined in integers.

Stats are derived from a base value and a growth value. The base value is the starting point for the stat, while the growth value defines how much the stat increases as the character levels up.

#### Stat Archetypes

In this game there are four stat archetypes that define a character's "Family" or "Class".

- Power

  Power is the archetype that focuses on physical strength and direct fighting abilities.

- Magic

  Magic is the archetype that focuses on magical abilities and spellcasting.

- Sense

  Sense is the archetype that focuses on perception, utility and support skills.

- Charm

  Charm is the archetype that focuses on health, defense, evasion, and resilience.

Given that we know the archetypes, we can define the basic stats.

- AP: Attack Power. Defines the physical damage a character can deal. Scales with Power.

  AP is used to calculate physical damage and is the main stat for power skills. Charm and Sense skills may also use AP as a secondary stat.

- AC: Accuracy. Defines the chance to hit a target. Scales with Power.

  AC is used to calculate the chance to hit a target and may potentiate power based skills. Sense skills may also use AC as a secondary stat.

- DX: Dexterity. Defines the attack speed. Scales with Power.

  DX is used to calculate the attack speed and may potentiate power based skills. Sense skills may also use DX as a secondary stat.

- MP: Mana Pool. Defines the amount of mana a character has to cast spells. Scales with Magic.

  MP is used to determine if a character can invoke a skill. This is not specific for magic skills, as power, sense and charm skills may also use MP.

- MA: Magic Attack. Defines the magical damage a character can deal. Scales with Magic.

  MA is used to calculate magical damage and is the main stat for magic skills. Elemental skills from other families may also use MA as a secondary stat.

- MD: Magic Defense. Defines the resistance to magical damage. Scales with Magic.

- WT: Weight. Defines how much a character can carry. Scales with Sense.

  Weight is used to determine how much a character can carry. This is not specific for sense skills, as power, magic and charm skills may also use WT. It can be used as a secondary stat for some sense skills.

- DA: Detect Ability: Defines the potential to detect hidden objects or enemies. Scales with Sense.

  DA is the main stat for sense skills.

- LK: Luck. Defines the chance of critical hits and magic evasion. Scales with Sense.

- HP: Health Points. Defines the amount of health a character has. Scales with Charm.

  HP is used to determine if a character is alive or dead. This is not specific for charm skills, as power, magic and sense skills may also use HP. It can be used as a secondary stat for some charm skills.

- DP: Defense Points. Defines the resistance to physical damage. Scales with Charm.

  DP is used to calculate physical damage reduction and is the main stat for charm skills. Power skills may also use DP as a secondary stat.

- HV: Evasion. Defines the chance to evade physical attacks. Scales with Charm.

  HV is the main stat for charm skills, it is also used to calculate the chance to evade physical attacks. Sense skills may also use HV as a secondary stat.

### Archetype Interactions

Stat archetypes are meant to be used for balancing purposes.

- Power Types should counter Sense Types
- Sense Types should counter Charm Types
- Charm Types should counter Magic Types
- Magic Types should counter Power Types

### Effects (in term of skills)

Effects are defined as changes to an entity.

Effects may perform:

- Stat increment
- Stat decrement
- Enable actions
- Disable actions
- Effect Removal
  - Single
  - Multiple
  - All
- Resource amount replenish over time

Effects may be:

- Instant
- Over Time
- Stackable

### Skills

Skills are defined as actions that a character can perform.

Abilities may:

- Deal Damage
- Apply Effects
- Target Self, Allies, or Enemies

Abilities have:

- Cooldown (in ticks)
- Resource Cost (HP or MP)
- Targeting Type:
  - Self
  - SingleAlly
  - SingleEnemy
  - MultiTarget (with max target count)
- FormulaId (optional reference to damage calculation formula)
- Effects (list of effect IDs to apply)

### Formula System

Damage calculation uses a separate formula system:

- **FormulaDefinition**: Contains ID, name, and calculation function
- **CalculationContext**: Provides invoker stats, elemental attributes, and target resistances
- **DamageResult**: Returns base damage, elemental damage, element type, and damage type

Formulas are referenced by abilities through FormulaId and are not hardcoded to abilities.

### Damage Calculation

Damage calculation is done in 4 steps:

1.  **Validation & Hit/Miss Check**:

    - Validate action availability:
      - Has enough resources?
      - Is it on cooldown?
      - Is the entity able to perform the action?
        - Stunned -> No actions
        - Silenced -> No MP-based abilities
      - Is the target valid?
      - Is the actor alive?
    - Hit or Miss calculation:
      - A base hit chance of 50% is adjusted by the difference between attacker and defender stats.
      - The formula is: `chance = 0.5 + (attackerStat - defenderStat) / 100.0`.
      - The final chance is clamped between 5% and 95%.
      - If Physical damage: uses Attacker's AC vs Defender's HV.
      - If Magical damage: uses Attacker's LK vs Defender's LK.
      - Neutral damage always hits.

2.  **Formula-Based Damage Calculation**:

    - Build CalculationContext:
      - InvokerStats (DerivedStats)
      - InvokerElementalAttributes (HashMap<Element, float>)
      - TargetElementalResistances (HashMap<Element, float>)
    - Invoke formula with context to get DamageResult:
      - BaseDamage: int
      - ElementalDamage: int
      - Element: Element type
      - DamageType: Physical or Magical

3.  **Apply Damage Modifiers**:

    - Elemental Resistance:
      - Apply target's elemental resistance to elemental damage
      - FinalElementalDamage = ElementalDamage \* (1.0 - resistance)
      - Neutral element ignores resistances

4.  **Apply Final Damage**:

    - Calculate total damage: BaseDamage + FinalElementalDamage
    - Apply defense reduction based on damage type:
      - Physical Damage: `totalDamage - defender.DP`
      - Magical Damage: `totalDamage - defender.MD`
    - Critical Hit Calculation:
      - Roll based on invoker's LK stat (LK \* 0.01 chance)
      - Critical bonus: 10% of (BaseDamage + ElementalDamage)
    - Apply critical hit bonus to the damage after defense reduction.
    - Subtract final damage from target's HP.
    - Check for death (HP <= 0)

### Resource System

Entities have three resource types:

- **HP**: Health Points (derived from Charm \* 10)
- **MP**: Mana Points (derived from Magic \* 5)

Resource costs are defined per ability and deducted when abilities are used.

### Equipment

Equipment system is defined but not yet implemented. Will provide stat bonuses and elemental attributes/resistances.
