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
        if def.Kind = EffectKind.Shield then Some e.Stacks else None)
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
