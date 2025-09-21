# Refactoring Plan: Align Implementation with Game Definitions

## Overview
Before proceeding to Phase 4, we need to align the current implementation with the game definitions to ensure consistency and prevent technical debt.

## Critical Discrepancies to Fix

### 1. Family Names (Classification.fs)
**Current:** Strength, Magic, Sensory, Charm  
**Required:** Power, Magic, Sense, Charm

**Impact:** Low - mainly naming consistency
**Files to update:** Domain.fs, all test files, content definitions

### 2. Stat System Alignment (Domain.fs)
**Current stat names → Required names:**
- AttackPower → AP (Attack Power)
- Accuracy → AC (Accuracy) 
- Dexterity → DX (Dexterity)
- MagicPotential → MP (Mana Pool) - but this conflicts with MP resource
- MagicAttack → MA (Magic Attack)
- MagicDefense → MD (Magic Defense)
- DetectAbility → DA (Detect Ability)
- WillPower → WT (Weight) - **MAJOR CHANGE**
- Luck → LK (Luck)
- HealthPoints → HP (Health Points) - but this conflicts with HP resource
- DefensePotential → DP (Defense Points)
- Hevasion → HV (Evasion)

**Missing stat:** WT (Weight) - needs to be added

**Impact:** High - affects all combat calculations, derived stats, effects system
**Files to update:** Domain.fs, Gameplay.fs, Combat.fs, Effects.fs, all tests

### 3. Elemental System (Domain.fs)
**Current:** Fire, Earth, Water, Air, Light, Dark, Neutral  
**Required:** Fire, Water, Earth, Air, Lightning, Light, Dark

**Changes needed:**
- Add Lightning element
- Remove Neutral element (or keep as default)

**Impact:** Medium - affects combat system and content definitions
**Files to update:** Domain.fs, Combat.fs, Content.fs

### 4. Targeting System (Resolution.fs)
**Current:** Single target only (actor → target)
**Required:** Multi-target capabilities per game definitions

**Missing implementation:**
- Single Target (Self, Ally, Enemy)
- Multi Target (defined amount of allies, enemies, both, or self)
- Target validation and selection logic
- Resolution system updates to handle multiple targets

**Impact:** High - core ability system architecture
**Files to update:** Domain.fs (new targeting types), Resolution.fs, Abilities module

### 5. Damage Calculation System (Combat.fs)
**Current:** Basic physical/magical damage  
**Required:** 4-step process with elemental damage integration

**Missing implementation:**
- Step 2: Elemental damage calculation and application
- Step 3: Proper elemental resistance application
- Integration of elemental damage as additive to base damage

**Impact:** High - core combat mechanic
**Files to update:** Combat.fs, Resolution.fs, new elemental damage module

## Refactoring Strategy

### Approach: Incremental Refactoring with Backward Compatibility

1. **Create new types alongside existing ones**
2. **Update one module at a time**
3. **Maintain test coverage throughout**
4. **Remove old types only after full migration**

### Step-by-Step Plan

#### Step 1: Update Classification System
- [x] Rename Family values: Strength→Power, Sensory→Sense
- [x] Update all references in tests and content
- [x] Verify no breaking changes

#### Step 2: Extend Elemental System  
- [x] Add Lightning element
- [x] Update resistance maps
- [x] Update content definitions

#### Step 3: Stat System Refactoring (Most Complex)
- [ ] Create new DerivedStats record with correct names
- [ ] Handle MP/HP naming conflicts (keep as resources, use different derived stat names)
- [ ] Add WT (Weight) stat derived from Sense
- [ ] Update stat calculation formulas
- [ ] Migrate Effects.Stat enum
- [ ] Update all stat references in combat and effects

#### Step 4: Targeting System Implementation (Critical for Resolution)
- [ ] Define targeting types (Self, SingleTarget, MultiTarget)
- [ ] Add targeting validation logic
- [ ] Update AbilityDefinition to include targeting information
- [ ] Refactor Resolution system to handle multiple targets
- [ ] Update Command types to support target lists

#### Step 5: Implement Elemental Damage System
- [ ] Create elemental damage calculation module
- [ ] Integrate with existing combat system
- [ ] Implement 4-step damage process from game definitions
- [ ] Update ability definitions to include elemental types

#### Step 6: Update Tests and Content
- [ ] Migrate all test cases to new stat names
- [ ] Add tests for multi-target abilities
- [ ] Update content definitions
- [ ] Add tests for elemental damage system
- [ ] Verify all Phase 3 functionality still works

## Risk Assessment

### High Risk Changes:
1. **Stat system refactoring** - touches every module
2. **Targeting system changes** - affects resolution architecture and command handling
3. **Damage calculation changes** - core game mechanic

### Mitigation:
- Maintain comprehensive test coverage
- Incremental changes with validation at each step
- Keep old and new systems running in parallel during transition

## Success Criteria

- [ ] All existing Phase 3 tests pass with new stat names
- [ ] Elemental damage system implemented per game definitions
- [ ] 4-step damage calculation process working
- [ ] No regression in existing functionality
- [ ] Code aligned with game definitions document

## Estimated Effort

- **Step 1 (Classification):** 2-3 hours
- **Step 2 (Elements):** 1-2 hours  
- **Step 3 (Stats):** 6-8 hours (most complex)
- **Step 4 (Elemental Damage):** 4-5 hours
- **Step 5 (Tests/Content):** 3-4 hours

**Total:** 16-22 hours of focused refactoring work

## Next Steps After Refactoring

Once alignment is complete, we can proceed with Phase 4:
- Enhanced targeting system (Self, Multi-target, AoE)
- Data-driven ability formulas
- Advanced ability mechanics (charges, cast time, interruption)
- Expanded content library