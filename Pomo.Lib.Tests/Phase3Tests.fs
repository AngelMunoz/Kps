namespace Pomo.Lib.Tests

open System
open Xunit
open FSharp.Data.Adaptive
open Pomo.Lib
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.GameEvent
open Pomo.Lib.Content
open Pomo.Lib.Rules
open Pomo.Lib.Domain.Rules

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
    (id: int<EntityId>)
    (baseStats: BaseAttributes)
    hp
    mp
    stamina
    (abilities: int<AbilityId> list)
    (effects: (int<EffectId> * int * int64<Tick>) list)
    =
    let emptySeq: seq<int<AbilityId> * int64<Tick>> = Seq.empty
    let cooldowns: cmap<int<AbilityId>, int64<Tick>> = cmap emptySeq

    transact(fun _ ->
      for a in abilities do
        cooldowns.Add(a, 0L<Tick>) |> ignore)

    let activeEffects =
      effects
      |> List.map(fun (effectId, stacks, remaining) -> {
        EffectId = effectId
        SourceId = id
        RemainingTicks = remaining
        NextTickIn = 0L<Tick>
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
    (id: int<EntityId>)
    (all: Components.All)
    =
    transact(fun _ -> state.entities.Add(id, all) |> ignore)

  let derivedOf (state: Gameplay.GameState) (id: int<EntityId>) =
    Gameplay.GameState.getDerivedStats state |> AMap.force |> (fun m -> m.[id])

open Phase3Helpers

// T1 Shield absorption (basic) -------------------------------------------------

type ``Phase3 - Shield``() =
  [<Fact>]
  member _.``T1 Shield absorbs damage before HP until depleted``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5) // deterministic RNG
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>

    let attacker = makeEntity attackerId baseStats 100 30 100 [ melee ] []

    // Give target 3 stacks of Shield (102<EffectId>) -> 30 shield points total
    let shieldEffectId = 102<EffectId>

    let target =
      makeEntity targetId baseStats 100 30 100 [] [
        (shieldEffectId, 3, 10000L<Tick>)
      ]

    addEntity state attackerId attacker
    addEntity state targetId target

    // Act: perform melee attacks until shield gone
    let perform() =
      let delta =
        Resolution.step
          state
          (MeleeAttack {
            actor = attackerId
            target = targetId
            abilityId = melee
          })

      let change = delta |> AVal.force
      Resolution.apply state change

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
    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance
    perform() // 2
    let stacksAfter2 = getShieldStacks()
    let hpAfter2 = hp()
    Assert.Equal(0, stacksAfter2)
    Assert.Equal(91, hpAfter2)

    // Third hit: no shield, full 19 damage to HP => 72.
    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance
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
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let stunEffectId = 100<EffectId> // Stun

    let attacker =
      makeEntity attackerId baseStats 100 30 100 [ melee ] [
        (stunEffectId, 1, 10000L<Tick>)
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

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

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

    Assert.Equal(0L<Tick>, cooldown)

// T3 Silence blocks spell but allows melee -----------------------------------

type ``Phase3 - Silence``() =
  [<Fact>]
  member _.``T3 Silence blocks spell but allows melee``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let spell = 2<AbilityId> // Assuming a spell ability
    let silenceEffectId = 101<EffectId> // Silence

    let attacker =
      makeEntity attackerId baseStats 100 30 100 [ melee; spell ] [
        (silenceEffectId, 1, 10000L<Tick>)
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

    let spellDelta = Resolution.step state spellAction
    let spellChange = spellDelta |> AVal.force
    Resolution.apply state spellChange

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

    let meleeDelta = Resolution.step state meleeAction
    let meleeChange = meleeDelta |> AVal.force
    Resolution.apply state meleeChange

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
    let attackerId = 1<EntityId>
    let intendedTargetId = 2<EntityId>
    let taunterId = 3<EntityId>
    let melee = 1<AbilityId>
    let tauntEffectId = 103<EffectId> // Taunt

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
      RemainingTicks = 10000L<Tick>
      NextTickIn = 0L<Tick>
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

    let delta = Resolution.step state action
    let change = delta |> AVal.force
    Resolution.apply state change

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
    let casterId = 1<EntityId>
    let targetId = 2<EntityId>
    let spellId = 3<AbilityId> // A spell that applies a NoStack effect
    let noStackEffectId = 104<EffectId> // Assuming this is a NoStack effect

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        Resolution.step
          state
          (CastSpell {
            actor = casterId
            target = targetId
            abilityId = spellId
          })

      let change = delta |> AVal.force
      Resolution.apply state change

    // Act
    applySpell() // First application
    let effectsAfterFirst = state.entities.[targetId].Effects |> AList.force

    let firstEffect =
      effectsAfterFirst |> Seq.find(fun e -> e.EffectId = noStackEffectId)

    let advance = Gameplay.GameState.tick state 1000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // Advance time slightly

    applySpell() // Second application
    let effectsAfterSecond = state.entities.[targetId].Effects |> AList.force

    // Assert
    Assert.Equal(1, effectsAfterFirst.Count)
    Assert.Equal(1, effectsAfterSecond.Count)

    let secondEffect =
      effectsAfterSecond |> Seq.find(fun e -> e.EffectId = noStackEffectId)

    let remaining = firstEffect.RemainingTicks - 1000L<Tick>
    let secondRemaining = secondEffect.RemainingTicks

    Assert.Equal(remaining, secondRemaining)

    Assert.Equal(1, secondEffect.Stacks)

  // T6 Effect stacking: RefreshDuration --------------------------------------
  [<Fact>]
  member _.``T6 RefreshDuration resets timer, stack count unchanged``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = 1<EntityId>
    let targetId = 2<EntityId>
    let spellId = 4<AbilityId> // A spell that applies a RefreshDuration effect
    let refreshEffectId = 1<EffectId> // Minor Strength Buff

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        Resolution.step
          state
          (CastSpell {
            actor = casterId
            target = targetId
            abilityId = spellId
          })

      let change = delta |> AVal.force
      Resolution.apply state change

    // Act
    applySpell() // First application
    let effectsAfterFirst = state.entities.[targetId].Effects |> AList.force

    let firstEffect =
      effectsAfterFirst |> Seq.find(fun e -> e.EffectId = refreshEffectId)

    let advance = Gameplay.GameState.tick state 1000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // Advance time

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
      secondEffect.RemainingTicks > firstEffect.RemainingTicks - 1000L<Tick>,
      "Duration should be refreshed"
    )

    Assert.Equal(expectedDuration, secondEffect.RemainingTicks)

  // T7 Effect stacking: AddStack ---------------------------------------------
  [<Fact>]
  member _.``T7 AddStack increments up to cap then stops``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = 1<EntityId>
    let targetId = 2<EntityId>
    let spellId = 5<AbilityId> // Shield Spell
    let addStackEffectId = 102<EffectId> // Shield effect

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        Resolution.step
          state
          (CastSpell {
            actor = casterId
            target = targetId
            abilityId = spellId
          })

      let change = delta |> AVal.force
      Resolution.apply state change

    let getEffectStacks() =
      state.entities.[targetId].Effects
      |> AList.force
      |> Seq.tryFind(fun e -> e.EffectId = addStackEffectId)
      |> Option.map(fun e -> e.Stacks)
      |> Option.defaultValue 0

    // Act & Assert
    applySpell() // 1
    Assert.Equal(1, getEffectStacks())

    let advance = Gameplay.GameState.tick state 1000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // Wait for cooldown
    applySpell() // 2
    Assert.Equal(2, getEffectStacks())

    let advance = Gameplay.GameState.tick state 1000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // Wait for cooldown
    applySpell() // 3
    Assert.Equal(3, getEffectStacks())

    let advance = Gameplay.GameState.tick state 1000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // Wait for cooldown
    applySpell() // 4
    Assert.Equal(4, getEffectStacks())

    let advance = Gameplay.GameState.tick state 1000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // Wait for cooldown
    applySpell() // 5
    Assert.Equal(5, getEffectStacks())

    let advance = Gameplay.GameState.tick state 1000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // Wait for cooldown
    applySpell() // 6 - Should not exceed cap
    Assert.Equal(5, getEffectStacks())

  // T8 DoT ticking applies periodic damage -----------------------------------
  [<Fact>]
  member _.``T8 DoT ticking applies periodic damage and expires``() =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let casterId = 1<EntityId>
    let targetId = 2<EntityId>
    let spellId = 6<AbilityId> // Poison Spell
    let dotEffectId = 105<EffectId> // Poison

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 100 100 100 [] []
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        Resolution.step
          state
          (CastSpell {
            actor = casterId
            target = targetId
            abilityId = spellId
          })

      let change = delta |> AVal.force
      Resolution.apply state change

    let hp() = state.entities.[targetId].Resources.HP
    let initialHp = hp()

    // Act
    applySpell()
    let hpAfterApply = hp()
    Assert.Equal(initialHp, hpAfterApply) // No initial damage

    // Tick forward to trigger DoT
    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 1st tick
    let hpAfterTick1 = hp()
    Assert.Equal(initialHp - 5, hpAfterTick1)

    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 2nd tick
    let hpAfterTick2 = hp()
    Assert.Equal(initialHp - 10, hpAfterTick2)

    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 3rd tick
    let hpAfterTick3 = hp()
    Assert.Equal(initialHp - 15, hpAfterTick3)

    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 4th tick
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
    let casterId = 1<EntityId>
    let targetId = 2<EntityId>
    let spellId = 7<AbilityId> // Regen Spell
    let hotEffectId = 106<EffectId> // Regeneration

    let caster = makeEntity casterId baseStats 100 100 100 [ spellId ] []
    let target = makeEntity targetId baseStats 50 100 100 [] [] // Start with 50 HP
    addEntity state casterId caster
    addEntity state targetId target

    let applySpell() =
      let delta =
        Resolution.step
          state
          (CastSpell {
            actor = casterId
            target = targetId
            abilityId = spellId
          })

      let change = delta |> AVal.force
      Resolution.apply state change

    let hp() = state.entities.[targetId].Resources.HP
    let initialHp = hp()

    // Act
    applySpell()
    let hpAfterApply = hp()
    Assert.Equal(initialHp, hpAfterApply) // No initial healing

    // Tick forward to trigger HoT
    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 1st tick
    let hpAfterTick1 = hp()
    Assert.Equal(initialHp + 5, hpAfterTick1)

    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 2nd tick
    let hpAfterTick2 = hp()
    Assert.Equal(initialHp + 10, hpAfterTick2)

    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 3rd tick
    let hpAfterTick3 = hp()
    Assert.Equal(initialHp + 15, hpAfterTick3)

    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // 4th tick
    let hpAfterTick4 = hp()
    Assert.Equal(initialHp + 20, hpAfterTick4)

    // Effect should have expired now (8000L<ticks> total duration)
    let effects = state.entities.[targetId].Effects |> AList.force
    Assert.Empty(effects)

// T10 Shield partial depletion across multiple hits (spillover to HP) -----------
type ``Phase3 - Shield Extended``() =
  [<Fact>]
  member _.``T10 Shield partial depletion across multiple hits with spillover``
    ()
    =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5) // deterministic RNG
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>

    let attacker = makeEntity attackerId baseStats 100 30 100 [ melee ] []

    // Give target 2 stacks of Shield (102<EffectId>) -> 20 shield points total
    let shieldEffectId = 102<EffectId>

    let target =
      makeEntity targetId baseStats 100 30 100 [] [
        (shieldEffectId, 2, 10000L<Tick>)
      ]

    addEntity state attackerId attacker
    addEntity state targetId target

    let perform() =
      let delta =
        Resolution.step
          state
          (MeleeAttack {
            actor = attackerId
            target = targetId
            abilityId = melee
          })

      let change = delta |> AVal.force
      Resolution.apply state change

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

    // Initial state: 2 shield stacks (20 points), 100 HP
    let initialStacks = getShieldStacks()
    let initialHp = hp()
    Assert.Equal(2, initialStacks)
    Assert.Equal(100, initialHp)

    // First hit: 19 damage vs 20 shield points
    // Shield absorbs 19, reduces by 2 stacks (ceil(19/10)) to 0 stacks
    // Remaining 1 shield point after damage should be consumed
    perform()
    let stacksAfter1 = getShieldStacks()
    let hpAfter1 = hp()
    Assert.Equal(0, stacksAfter1) // Shield should be depleted
    Assert.Equal(100, hpAfter1) // HP should be unchanged (shield absorbed all damage)

    // Second hit: No shield, full 19 damage to HP
    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance // advance past cooldown
    perform()
    let stacksAfter2 = getShieldStacks()
    let hpAfter2 = hp()
    Assert.Equal(0, stacksAfter2)
    Assert.Equal(81, hpAfter2) // 100 - 19 = 81

    // Third hit: No shield, another 19 damage to HP
    let advance = Gameplay.GameState.tick state 2000L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance
    perform()
    let stacksAfter3 = getShieldStacks()
    let hpAfter3 = hp()
    Assert.Equal(0, stacksAfter3)
    Assert.Equal(62, hpAfter3) // 81 - 19 = 62

// T11 Deterministic RNG yields identical damage sequence ------------------
type ``Phase3 - Determinism``() =
  [<Fact>]
  member _.``T11 Deterministic RNG yields identical damage sequence with fixed seed``
    ()
    =
    // Arrange: Create two identical game states with the same RNG seed
    let rng1 = fun () -> 0.3 // Fixed value
    let rng2 = fun () -> 0.3 // Same fixed value

    let state1 = Gameplay.GameState.create' rng1
    let state2 = Gameplay.GameState.create' rng2

    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>

    let attacker1 = makeEntity attackerId baseStats 100 30 100 [ melee ] []
    let target1 = makeEntity targetId baseStats 100 30 100 [] []

    let attacker2 = makeEntity attackerId baseStats 100 30 100 [ melee ] []
    let target2 = makeEntity targetId baseStats 100 30 100 [] []

    addEntity state1 attackerId attacker1
    addEntity state1 targetId target1
    addEntity state2 attackerId attacker2
    addEntity state2 targetId target2

    let performAttack state =
      let delta =
        Resolution.step
          state
          (MeleeAttack {
            actor = attackerId
            target = targetId
            abilityId = melee
          })

      let change = delta |> AVal.force
      Resolution.apply state change

    // Act: Perform the same sequence of actions on both states
    performAttack state1
    performAttack state2

    // Assert: Both states should have identical results
    let target1Hp = state1.entities.[targetId].Resources.HP
    let target2Hp = state2.entities.[targetId].Resources.HP
    Assert.Equal(target1Hp, target2Hp)

    // Get damage events from both states
    let getDamageEvents(state: Gameplay.GameState) =
      state.gameEvents
      |> AList.choose (function
        | GameEvent.DamageApplied e when e.target = targetId -> Some e.amount
        | _ -> None)
      |> AList.force
      |> Seq.toList

    let damage1 = getDamageEvents state1
    let damage2 = getDamageEvents state2

    Assert.Equal<int list>(damage1, damage2)

    // Perform second round after cooldown
    let advance1 = Gameplay.GameState.tick state1 2500L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state1 advance1
    let advance2 = Gameplay.GameState.tick state2 2500L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state2 advance2

    performAttack state1
    performAttack state2

    let target1HpAfter2 = state1.entities.[targetId].Resources.HP
    let target2HpAfter2 = state2.entities.[targetId].Resources.HP
    Assert.Equal(target1HpAfter2, target2HpAfter2)

// T12 Cooldown-ready abilities set includes ability after cooldown elapses -----
type ``Phase3 - Cooldown Management``() =
  [<Fact>]
  member _.``T12 Cooldown-ready abilities set includes ability after cooldown elapses``
    ()
    =
    // Arrange
    let state = Gameplay.GameState.create'(fun () -> 0.5)
    let attackerId = 1<EntityId>
    let targetId = 2<EntityId>
    let melee = 1<AbilityId>
    let spell = 2<AbilityId>

    let attacker =
      makeEntity attackerId baseStats 100 100 100 [ melee; spell ] []

    let target = makeEntity targetId baseStats 100 100 100 [] []

    addEntity state attackerId attacker
    addEntity state targetId target

    // Act & Assert: Check initial readiness (all abilities should be ready)
    let readyAbilitiesInitial =
      Gameplay.GameState.aReadyAbilities state |> ASet.force

    let attackerAbilitiesInitial =
      readyAbilitiesInitial
      |> Seq.filter(fun (id, _) -> id = attackerId)
      |> Seq.map snd
      |> Set.ofSeq

    Assert.Contains(melee, attackerAbilitiesInitial)
    Assert.Contains(spell, attackerAbilitiesInitial)

    // Use melee ability (puts it on cooldown)
    let meleeDelta =
      Resolution.step
        state
        (MeleeAttack {
          actor = attackerId
          target = targetId
          abilityId = melee
        })

    let meleeChange = meleeDelta |> AVal.force
    Resolution.apply state meleeChange

    // Check that melee is no longer ready, but spell still is
    let readyAfterMelee = Gameplay.GameState.aReadyAbilities state |> ASet.force

    let attackerAbilitiesAfterMelee =
      readyAfterMelee
      |> Seq.filter(fun (id, _) -> id = attackerId)
      |> Seq.map snd
      |> Set.ofSeq

    Assert.DoesNotContain(melee, attackerAbilitiesAfterMelee)
    Assert.Contains(spell, attackerAbilitiesAfterMelee)

    // Advance time past melee cooldown (melee has 2000L<ticks> cooldown)
    let advance = Gameplay.GameState.tick state 2100L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance

    // Check that melee is ready again
    let readyAfterCooldown =
      Gameplay.GameState.aReadyAbilities state |> ASet.force

    let attackerAbilitiesAfterCooldown =
      readyAfterCooldown
      |> Seq.filter(fun (id, _) -> id = attackerId)
      |> Seq.map snd
      |> Set.ofSeq

    Assert.Contains(melee, attackerAbilitiesAfterCooldown)
    Assert.Contains(spell, attackerAbilitiesAfterCooldown)

    // Use spell ability (puts it on cooldown - spell has 5000L<ticks> cooldown)
    let spellDelta =
      Resolution.step
        state
        (CastSpell {
          actor = attackerId
          target = targetId
          abilityId = spell
        })

    let spellChange = spellDelta |> AVal.force
    Resolution.apply state spellChange

    // Check that spell is no longer ready, but melee still is
    let readyAfterSpell = Gameplay.GameState.aReadyAbilities state |> ASet.force

    let attackerAbilitiesAfterSpell =
      readyAfterSpell
      |> Seq.filter(fun (id, _) -> id = attackerId)
      |> Seq.map snd
      |> Set.ofSeq

    Assert.Contains(melee, attackerAbilitiesAfterSpell)
    Assert.DoesNotContain(spell, attackerAbilitiesAfterSpell)

    // Advance time past spell cooldown
    let advance = Gameplay.GameState.tick state 5100L<Tick> |> AVal.force
    Gameplay.GameState.applyTick state advance

    // Check that both abilities are ready again
    let readyAfterBothCooldowns =
      Gameplay.GameState.aReadyAbilities state |> ASet.force

    let attackerAbilitiesAfterBoth =
      readyAfterBothCooldowns
      |> Seq.filter(fun (id, _) -> id = attackerId)
      |> Seq.map snd
      |> Set.ofSeq

    Assert.Contains(melee, attackerAbilitiesAfterBoth)
    Assert.Contains(spell, attackerAbilitiesAfterBoth)
