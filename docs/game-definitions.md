# Pomo.Lib

Pomo.Lib main goal is to an adaptive library for real time rpg games.

It should provide a way to define characters, abilities, items and provide game systems to easily integrate with different game engines like MonoGame, Godot, etc.

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

1. **Validation & Hit/Miss Check**:

- Validate action availability:
  - Has enough resources?
  - Is it on cooldown?
  - Is the entity able to perform the action?
    - Stunned -> No actions
    - Silenced -> No MP-based abilities
  - Is the target valid?
  - Is the actor alive?
- Hit or Miss calculation:
  - If Physical damage: AC vs HV (AC / (AC + HV))
  - If Magical damage: LK vs LK (LK / (LK + LK))

2. **Formula-Based Damage Calculation**:

- Build CalculationContext:
  - InvokerStats (DerivedStats)
  - InvokerElementalAttributes (HashMap<Element, float>)
  - TargetElementalResistances (HashMap<Element, float>)
- Invoke formula with context to get DamageResult:
  - BaseDamage: int
  - ElementalDamage: int
  - Element: Element type
  - DamageType: Physical or Magical

3. **Apply Damage Modifiers**:

- Critical Hit Calculation:
  - Roll based on invoker's LK stat (LK \* 0.01 chance)
  - Critical bonus: 10% of (BaseDamage + ElementalDamage)
- Elemental Resistance:
  - Apply target's elemental resistance to elemental damage
  - FinalElementalDamage = ElementalDamage \* (1.0 - resistance)
  - Neutral element ignores resistances

4. **Apply Final Damage**:

- Calculate total damage: BaseDamage + FinalElementalDamage + CriticalBonus
- Subtract remaining damage from target's HP
- Check for death (HP <= 0)

### Resource System

Entities have three resource types:

- **HP**: Health Points (derived from Charm \* 10)
- **MP**: Mana Points (derived from Magic \* 5)

Resource costs are defined per ability and deducted when abilities are used.

### Equipment

Equipment system is defined but not yet implemented. Will provide stat bonuses and elemental attributes/resistances.
