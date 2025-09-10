namespace Pomo.Core.Rules

open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Primitives
open Pomo.Core.Domain.Components
open Pomo.Core.Gameplay

module Resolution =
  /// Resolves a MeleeAttack command, calculating damage and generating events.
  let private resolveMeleeAttack
    (entities: Map<EntityId, All>)
    (derivedStats: Map<EntityId, Attributes.DerivedStats>)
    actorId
    targetId
    =
    // Note: This is a simplified implementation for Phase 2.
    // It does not yet account for costs, cooldowns, or complex validation.

    let actorComponents = entities[actorId]
    let targetComponents = entities[targetId]
    let actorStats = derivedStats[actorId]
    let targetStats = derivedStats[targetId]

    // 1. Validate: Check if the attacker is alive.
    if actorComponents.Resources.Status <> Attributes.Status.Alive then
      [||], Map.empty // Attacker is not alive, so no action occurs.
    else
      // 2. Resolve: Calculate damage based on attacker's power and target's armor.
      let damage = max 0 (actorStats.AttackPower - targetStats.Armor)
      let damageEvent = DamageApplied { target = targetId; amount = damage }

      // 3. Apply Changes: Update the target's health.
      let newHp = max 0 (targetComponents.Resources.HP - damage)

      let newResources = {
        targetComponents.Resources with
            HP = newHp
      }

      // 4. Check for Death
      let deathEvent, finalResources =
        if
          newHp <= 0
          && targetComponents.Resources.Status = Attributes.Status.Alive
        then
          let event = EntityDied { entityId = targetId }

          let resources = {
            newResources with
                Status = Attributes.Status.Dead
          }

          Some event, resources
        else
          None, newResources

      let updatedTarget = {
        targetComponents with
            Resources = finalResources
      }

      let changes = Map.ofList [ targetId, updatedTarget ]

      let events =
        [| damageEvent |]
        |> Array.append(
          match deathEvent with
          | Some e -> [| e |]
          | _ -> [||]
        )

      events, changes

  let private step
    (currentEntities: Map<EntityId, All>)
    (derivedStats: Map<EntityId, Attributes.DerivedStats>)
    (command: Command)
    : GameEvent[] * Map<EntityId, All> =
    match command with
    | MeleeAttack attack ->
      let { actor = actor; target = target } = attack
      resolveMeleeAttack currentEntities derivedStats actor target
    | _ -> [||], Map.empty

  let apply (state: GameState) (cmd: Command) =
    transact(fun _ ->
      let currentEntities = state.entities.GetValue()
      let derivedStats = GameState.getDerivedStats state |> AMap.force
      let (events, changes) = step currentEntities derivedStats cmd

      for e in events do
        state.gameEvents.Add(e)

      state.entities.Modify(fun m ->
        Map.fold (fun map key value -> map.Add(key, value)) m changes))
