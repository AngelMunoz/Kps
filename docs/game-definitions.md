# Pomo.Lib

Pomo.Lib main goal is to an adaptive library for real time rpg games.

It should provide a way to define characters, skills, items and provide game systems to easily integrate with different game engines like MonoGame, Godot, etc.

## Damage Types

There are three damage types the engine:

- Physical

  Physical damage is dealt by physical weapons, swords, guns, etc.

- Magical

  Magical damage is dealt by spells, magic weapons, etc.

- Elemental

  Elemental damage is an additive damage type that can be applied to both physical and magical damage.

Damage Calculation will be done by first calculating the base damage (Physical or Magical) and then applying any Elemental modifiers.

### Elemental Types

- Fire
- Water
- Earth
- Air
- Lightning
- Light
- Dark

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

Skills may:

- Deal Damage
- Heal
- Apply Effects
- Remove Effects
- Active
- Passive
- Be Interruptible

Skills may have:

- Cooldown
- Resource Cost
- Range
- Targeting Type
  - Single Target (Self, Ally, Enemy)
  - Multi Target (A defined amount of allies, enemies, both or self)
- Cast Time
- Charges (A skill may have a limited amount of uses before going on cooldown)

Skills have their own formula calculation which may take into account the following parameters:

- Stats
- Derived Stats
- Elemental Type

Other parameters may be added in the future.

A note on skill damage calculation formula:

calculation formula is defined in a separate type and is not attached or harddcoded to the skill itself.

### Damage Calculation

Damage calculation is done in 4 steps:

1. Check for availability:

- Has enough resources?
- Is it on cooldown?
- Is the entity able to perform the action?
  - Stunned -> No actions
  - Silenced -> No magic actions
  - Bound -> No Physical actions
- Is the target valid?
- Hit or Miss calculation
  - If magic skill, do a roll based on the invoker's LK stat and the target's LK stat.
  - If physical skill, do a roll based on the invoker's AC stat and the target's HV stat.

2. Calculate Base Damage:

- Build calculation context which includes:
  - Invoker stats
  - Invoker derived stats
  - Invoker Elemental attribute map
- After building the context, invoke the skill's damage formula with the context.
- The formula will return the base damage and elemental damage.

3. Apply Damage Modifiers:

- Calculate critical hit:
  - Do a roll based on the invoker's LK stat and RNG of 5-10% crit chance and bonus damage.
- Take off target's damage reduction:
  - If physical skill, use target's DP stat.
  - If magical skill, use target's MD stat.
  - If elemental damage, use the target's elemental resistances and take off the amount from the elemental damage.

4. Apply Damage:

- Subtract the final damage from the target's HP.

> Effects are applied to the target and are calculated in the same way as skills.
> Meaning that effects may have their own calculation formula which is also not hardcoded to the effect itself.
> Both effects and skills should use the same calculation context.

### Equipment

Equipment are items that can be equipped by a character to provide stat bonuses or other effects.
