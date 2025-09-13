namespace Pomo.Lib.Tests

open System
open Xunit
open FSharp.Data.Adaptive
open Pomo.Lib
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.GameEvent
open Pomo.Lib.Content
open Pomo.Lib.Rules

module private Phase3Helpers =
  let baseStats = {
    Strength = 12
    Agility = 5
    Intellect = 4
    Vitality = 10
    Willpower = 4
    Luck = 3
  }

  let makeEntity
    (id: EntityId)
    (baseStats: BaseAttributes)
    hp
    mp
    stamina
    (abilities: Abilities.AbilityId list)
    (effects: (EffectId * int * int64<ticks>) list)
    =
    let emptySeq: seq<Abilities.AbilityId * int64<ticks>> = Seq.empty
    let cooldowns: cmap<Abilities.AbilityId, int64<ticks>> = cmap emptySeq

    transact(fun _ ->
      for a in abilities do
        cooldowns.Add(a, 0L<ticks>) |> ignore)

    let activeEffects =
      effects
      |> List.map(fun (effectId, stacks, remaining) -> {
        EffectId = effectId
        SourceId = id
        RemainingTicks = remaining
        NextTickIn = 0L<ticks>
        Stacks = stacks
      })
      |> clist

    {
      Identity = {
        Family = Classification.Family.Strength
        Stage = Classification.Stage.First
      }
      BaseStats = baseStats
      Resources = {
        HP = hp
        MP = mp
        Stamina = stamina
        Status = Status.Alive
      }
      Effects = (activeEffects :> alist<_>)
      Abilities = (clist abilities :> alist<_>)
      AbilityCooldowns = (cooldowns :> amap<_, _>)
    }

  let addEntity
    (state: Gameplay.GameState)
    (id: EntityId)
    (all: Components.All)
    =
    transact(fun _ -> state.entities.Add(id, all) |> ignore)

  let derivedOf (state: Gameplay.GameState) (id: EntityId) =
    Gameplay.GameState.getDerivedStats state |> AMap.force |> (fun m -> m.[id])

open Phase3Helpers

// T1 Shield absorption (basic) -------------------------------------------------

type ``Phase3 - Shield``() =
  [<Fact>]
  member _.``T1 Shield absorbs damage before HP until depleted``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5) // deterministic RNG
    let attackerId = EntityId 1
    let targetId = EntityId 2
    let melee = Abilities.AbilityId 1

    let attacker = makeEntity attackerId baseStats 100 30 100 [ melee ] []

    // Give target 3 stacks of Shield (EffectId 102) -> 30 shield points total
    let shieldEffectId = EffectId 102

    let target =
      makeEntity targetId baseStats 100 30 100 [] [
        (shieldEffectId, 3, 10000L<ticks>)
      ]

    addEntity state attackerId attacker
    addEntity state targetId target

    // Act: perform melee attacks until shield gone
    let perform() =
      Resolution.apply
        state
        (MeleeAttack {
          actor = attackerId
          target = targetId
          abilityId = melee
        })

    // After each attack, track shield stacks and HP
    let getShieldStacks() =
      state.entities.[targetId].Effects
      |> AList.force
      |> Seq.choose(fun e ->
        let def = EffectStore.definitions.[e.EffectId]

        match def.Kind with
        | EffectKind.Shield _ -> Some e.Stacks
        | _ -> None)
      |> Seq.sum

    let hp() = state.entities.[targetId].Resources.HP

    let initialStacks = getShieldStacks()
    Assert.Equal(3, initialStacks)
    let initialHp = hp()
    Assert.Equal(100, initialHp)

    // Deterministic damage calculation with rng=0.5 and stats yields 19 damage per swing.
    // Shield starts at 3 stacks (30 points). First hit consumes 19 -> stacks drop by 2 (ceil(19/10)) to 1, HP unchanged.
    perform() // 1
    let stacksAfter1 = getShieldStacks()
    let hpAfter1 = hp()
    Assert.Equal(1, stacksAfter1)
    Assert.Equal(100, hpAfter1)

    // Second hit: 1 stack (10 pts) absorbs 10, remaining 9 goes to HP. Stacks become 0, HP=91.
    Gameplay.GameState.tick state 2000L<ticks> // advance past cooldown
    perform() // 2
    let stacksAfter2 = getShieldStacks()
    let hpAfter2 = hp()
    Assert.Equal(0, stacksAfter2)
    Assert.Equal(91, hpAfter2)

    // Third hit: no shield, full 19 damage to HP => 72.
    Gameplay.GameState.tick state 2000L<ticks>
    perform() // 3
    let stacksAfter3 = getShieldStacks()
    let hpAfter3 = hp()
    Assert.Equal(0, stacksAfter3)
    Assert.Equal(72, hpAfter3)

    // Validate at least one DamageApplied event exists
    let damageEventCount =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied e when e.target = targetId -> Some e
        | _ -> None)
      |> AList.force
      |> Seq.length

    Assert.True(damageEventCount >= 1)

// T2 Stun prevents all actions -----------------------------------------------

type ``Phase3 - Stun``() =
  [<Fact>]
  member _.``T2 Stun prevents all actions``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let attackerId = EntityId 1
    let targetId = EntityId 2
    let melee = Abilities.AbilityId 1
    let stunEffectId = EffectId 100 // Stun

    let attacker =
      makeEntity attackerId baseStats 100 30 100 [ melee ] [
        (stunEffectId, 1, 10000L<ticks>)
      ]

    let target = makeEntity targetId baseStats 100 30 100 [] []

    addEntity state attackerId attacker
    addEntity state targetId target

    let initialTargetHp = state.entities.[targetId].Resources.HP
    let initialAttackerStamina = state.entities.[attackerId].Resources.Stamina

    // Act
    let action =
      MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      }

    Resolution.apply state action

    // Assert
    let finalTargetHp = state.entities.[targetId].Resources.HP
    Assert.Equal(initialTargetHp, finalTargetHp)

    let finalAttackerStamina = state.entities.[attackerId].Resources.Stamina
    Assert.Equal(initialAttackerStamina, finalAttackerStamina)

    let damageEventCount =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied _ -> Some()
        | _ -> None)
      |> AList.force
      |> Seq.length

    Assert.Equal(0, damageEventCount)

    let cooldown =
      (state.entities.[attackerId].AbilityCooldowns |> AMap.force).[melee]

    Assert.Equal(0L<ticks>, cooldown)

// T3 Silence blocks spell but allows melee -----------------------------------

type ``Phase3 - Silence``() =
  [<Fact>]
  member _.``T3 Silence blocks spell but allows melee``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let attackerId = EntityId 1
    let targetId = EntityId 2
    let melee = Abilities.AbilityId 1
    let spell = Abilities.AbilityId 2 // Assuming a spell ability
    let silenceEffectId = EffectId 101 // Silence

    let attacker =
      makeEntity attackerId baseStats 100 30 100 [ melee; spell ] [
        (silenceEffectId, 1, 10000L<ticks>)
      ]

    let target = makeEntity targetId baseStats 100 30 100 [] []

    addEntity state attackerId attacker
    addEntity state targetId target

    let initialTargetHp = state.entities.[targetId].Resources.HP
    let initialAttackerMp = state.entities.[attackerId].Resources.MP
    let initialAttackerStamina = state.entities.[attackerId].Resources.Stamina

    // Act 1: Attempt to cast a spell (should fail)
    let spellAction =
      CastSpell {
        actor = attackerId
        target = targetId
        abilityId = spell
      }

    Resolution.apply state spellAction

    // Assert 1
    let targetHpAfterSpell = state.entities.[targetId].Resources.HP
    Assert.Equal(initialTargetHp, targetHpAfterSpell)
    let attackerMpAfterSpell = state.entities.[attackerId].Resources.MP
    Assert.Equal(initialAttackerMp, attackerMpAfterSpell)

    let spellDamageEvents =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied _ -> Some()
        | _ -> None)
      |> AList.force
      |> Seq.length

    Assert.Equal(0, spellDamageEvents)

    // Act 2: Perform a melee attack (should succeed)
    let meleeAction =
      MeleeAttack {
        actor = attackerId
        target = targetId
        abilityId = melee
      }

    Resolution.apply state meleeAction

    // Assert 2
    let targetHpAfterMelee = state.entities.[targetId].Resources.HP

    Assert.True(
      targetHpAfterMelee < initialTargetHp,
      "Melee should deal damage"
    )

    let attackerStaminaAfterMelee =
      state.entities.[attackerId].Resources.Stamina

    Assert.True(
      attackerStaminaAfterMelee < initialAttackerStamina,
      "Melee should cost stamina"
    )

    let meleeDamageEvents =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied _ -> Some()
        | _ -> None)
      |> AList.force
      |> Seq.length

    Assert.True(meleeDamageEvents > 0, "Melee should generate a damage event")

// T4 Taunt redirection -------------------------------------------------------

type ``Phase3 - Taunt``() =
  [<Fact>]
  member _.``T4 Taunt redirection forces target to taunter``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let attackerId = EntityId 1
    let intendedTargetId = EntityId 2
    let taunterId = EntityId 3
    let melee = Abilities.AbilityId 1
    let tauntEffectId = EffectId 103 // Taunt

    let attacker = makeEntity attackerId baseStats 100 30 100 [ melee ] []
    let intendedTarget = makeEntity intendedTargetId baseStats 100 30 100 [] []
    let taunter = makeEntity taunterId baseStats 100 30 100 [] []

    addEntity state attackerId attacker
    addEntity state intendedTargetId intendedTarget
    addEntity state taunterId taunter

    // Apply taunt to the attacker from the taunter
    let tauntEffect = {
      EffectId = tauntEffectId
      SourceId = taunterId
      RemainingTicks = 10000L<ticks>
      NextTickIn = 0L<ticks>
      Stacks = 1
    }

    transact(fun _ ->
      let attacker = state.entities[attackerId]

      state.entities[attackerId] <-
        {
          attacker with
              Effects = clist [ tauntEffect ]
        })

    let initialIntendedTargetHp = state.entities.[intendedTargetId].Resources.HP
    let initialTaunterHp = state.entities.[taunterId].Resources.HP

    // Act
    let action =
      MeleeAttack {
        actor = attackerId
        target = intendedTargetId
        abilityId = melee
      }

    Resolution.apply state action

    // Assert
    let finalIntendedTargetHp = state.entities.[intendedTargetId].Resources.HP
    Assert.Equal(initialIntendedTargetHp, finalIntendedTargetHp)

    let finalTaunterHp = state.entities.[taunterId].Resources.HP

    Assert.True(
      finalTaunterHp < initialTaunterHp,
      "Taunter should have taken damage"
    )

    let damageEvent =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied e -> Some e
        | _ -> None)
      |> AList.force
      |> Seq.tryHead

    match damageEvent with
    | Some de -> Assert.Equal(taunterId, de.target)
    | None -> Assert.True(false, "A damage event should have been emitted")

// T5 Effect stacking: NoStack ------------------------------------------------

type ``Phase3 - Effect Stacking``() =
  [<Fact>]
  member _.``T5 NoStack ignores second application``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = EntityId 1
    let targetId = EntityId 2
    let spellId = Abilities.AbilityId 3 // A spell that applies a NoStack effect
    let noStackEffectId = EffectId 104 // Assuming this is a NoStack effect

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      Resolution.apply
        state
        (CastSpell {
          actor = casterId
          target = targetId
          abilityId = spellId
        })

    // Act
    applySpell() // First application
    let effectsAfterFirst = state.entities.[targetId].Effects |> AList.force

    let firstEffect =
      effectsAfterFirst |> Seq.find(fun e -> e.EffectId = noStackEffectId)

    Gameplay.GameState.tick state 1000L<ticks> // Advance time slightly

    applySpell() // Second application
    let effectsAfterSecond = state.entities.[targetId].Effects |> AList.force

    // Assert
    Assert.Equal(1, effectsAfterFirst.Count)
    Assert.Equal(1, effectsAfterSecond.Count)

    let secondEffect =
      effectsAfterSecond |> Seq.find(fun e -> e.EffectId = noStackEffectId)

    let remaining = firstEffect.RemainingTicks - 1000L<ticks>
    let secondRemaining = secondEffect.RemainingTicks

    Assert.Equal(remaining, secondRemaining)

    Assert.Equal(1, secondEffect.Stacks)

  // T6 Effect stacking: RefreshDuration --------------------------------------
  [<Fact>]
  member _.``T6 RefreshDuration resets timer, stack count unchanged``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = EntityId 1
    let targetId = EntityId 2
    let spellId = Abilities.AbilityId 4 // A spell that applies a RefreshDuration effect
    let refreshEffectId = EffectId 1 // Minor Strength Buff

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      Resolution.apply
        state
        (CastSpell {
          actor = casterId
          target = targetId
          abilityId = spellId
        })

    // Act
    applySpell() // First application
    let effectsAfterFirst = state.entities.[targetId].Effects |> AList.force

    let firstEffect =
      effectsAfterFirst |> Seq.find(fun e -> e.EffectId = refreshEffectId)

    Gameplay.GameState.tick state 1000L<ticks> // Advance time

    applySpell() // Second application (should refresh)
    let effectsAfterSecond = state.entities.[targetId].Effects |> AList.force

    let secondEffect =
      effectsAfterSecond |> Seq.find(fun e -> e.EffectId = refreshEffectId)

    // Assert
    let effectDef = EffectStore.definitions.[refreshEffectId]

    let expectedDuration =
      match effectDef.Duration with
      | Timed d -> d
      | _ -> failwith "Expected timed duration"

    Assert.Equal(1, effectsAfterFirst.Count)
    Assert.Equal(1, effectsAfterSecond.Count)
    Assert.Equal(1, secondEffect.Stacks) // Stacks should not change

    // The duration should be reset to the full value
    Assert.True(
      secondEffect.RemainingTicks > firstEffect.RemainingTicks - 1000L<ticks>,
      "Duration should be refreshed"
    )

    Assert.Equal(expectedDuration, secondEffect.RemainingTicks)

  // T7 Effect stacking: AddStack ---------------------------------------------
  [<Fact>]
  member _.``T7 AddStack increments up to cap then stops``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = EntityId 1
    let targetId = EntityId 2
    let spellId = Abilities.AbilityId 5 // Shield Spell
    let addStackEffectId = EffectId 102 // Shield effect

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      Resolution.apply
        state
        (CastSpell {
          actor = casterId
          target = targetId
          abilityId = spellId
        })

    let getEffectStacks() =
      state.entities.[targetId].Effects
      |> AList.force
      |> Seq.tryFind(fun e -> e.EffectId = addStackEffectId)
      |> Option.map(fun e -> e.Stacks)
      |> Option.defaultValue 0

    // Act & Assert
    applySpell() // 1
    Assert.Equal(1, getEffectStacks())

    Gameplay.GameState.tick state 1000L<ticks> // Wait for cooldown
    applySpell() // 2
    Assert.Equal(2, getEffectStacks())

    Gameplay.GameState.tick state 1000L<ticks> // Wait for cooldown
    applySpell() // 3
    Assert.Equal(3, getEffectStacks())

    Gameplay.GameState.tick state 1000L<ticks> // Wait for cooldown
    applySpell() // 4
    Assert.Equal(4, getEffectStacks())

    Gameplay.GameState.tick state 1000L<ticks> // Wait for cooldown
    applySpell() // 5
    Assert.Equal(5, getEffectStacks())

    Gameplay.GameState.tick state 1000L<ticks> // Wait for cooldown
    applySpell() // 6 - Should not exceed cap
    Assert.Equal(5, getEffectStacks())

  // T8 DoT ticking applies periodic damage -----------------------------------
  [<Fact>]
  member _.``T8 DoT ticking applies periodic damage and expires``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = EntityId 1
    let targetId = EntityId 2
    let spellId = Abilities.AbilityId 6 // Poison Spell
    let dotEffectId = EffectId 105 // Poison

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      Resolution.apply
        state
        (CastSpell {
          actor = casterId
          target = targetId
          abilityId = spellId
        })

    let hp() = state.entities.[targetId].Resources.HP
    let initialHp = hp()

    // Act
    applySpell()
    let hpAfterApply = hp()
    Assert.Equal(initialHp, hpAfterApply) // No initial damage

    // Tick forward to trigger DoT
    Gameplay.GameState.tick state 2000L<ticks> // 1st tick
    let hpAfterTick1 = hp()
    Assert.Equal(initialHp - 5, hpAfterTick1)

    Gameplay.GameState.tick state 2000L<ticks> // 2nd tick
    let hpAfterTick2 = hp()
    Assert.Equal(initialHp - 10, hpAfterTick2)

    Gameplay.GameState.tick state 2000L<ticks> // 3rd tick
    let hpAfterTick3 = hp()
    Assert.Equal(initialHp - 15, hpAfterTick3)

    Gameplay.GameState.tick state 2000L<ticks> // 4th tick
    let hpAfterTick4 = hp()
    Assert.Equal(initialHp - 20, hpAfterTick4)

    // Effect should have expired now (8000L<ticks> total duration)
    let effects = state.entities.[targetId].Effects |> AList.force
    Assert.Empty(effects)

  // T9 HoT ticking applies periodic healing ------------------------------------
  [<Fact>]
  member _.``T9 HoT ticking applies periodic healing and expires``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = EntityId 1
    let targetId = EntityId 2
    let spellId = Abilities.AbilityId 7 // Regen Spell
    let hotEffectId = EffectId 106 // Regeneration

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 50 100 100 [] [] // Start with 50 HP
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      Resolution.apply
        state
        (CastSpell {
          actor = casterId
          target = targetId
          abilityId = spellId
        })

    let hp() = state.entities.[targetId].Resources.HP
    let initialHp = hp()

    // Act
    applySpell()
    let hpAfterApply = hp()
    Assert.Equal(initialHp, hpAfterApply) // No initial healing

    // Tick forward to trigger HoT
    Gameplay.GameState.tick state 2000L<ticks> // 1st tick
    let hpAfterTick1 = hp()
    Assert.Equal(initialHp + 5, hpAfterTick1)

    Gameplay.GameState.tick state 2000L<ticks> // 2nd tick
    let hpAfterTick2 = hp()
    Assert.Equal(initialHp + 10, hpAfterTick2)

    Gameplay.GameState.tick state 2000L<ticks> // 3rd tick
    let hpAfterTick3 = hp()
    Assert.Equal(initialHp + 15, hpAfterTick3)

    Gameplay.GameState.tick state 2000L<ticks> // 4th tick
    let hpAfterTick4 = hp()
    Assert.Equal(initialHp + 20, hpAfterTick4)

    // Effect should have expired now (8000L<ticks> total duration)
    let effects = state.entities.[targetId].Effects |> AList.force
    Assert.Empty(effects)
