namespace Pomo.Core.Rules

open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Primitives
open Pomo.Core.Domain.Components
open Pomo.Core.Gameplay

module Resolution =

  type ResolverParams = {
    entities: amap<EntityId, All>
    derivedStats: amap<EntityId, Attributes.DerivedStats>
  }

  type ResolverActors = { actor: EntityId; target: EntityId }

  type ResolverFn =
    ResolverParams * ResolverActors -> aval<GameEvent[] * Map<EntityId, All>>

  /// Resolves a MeleeAttack command, calculating damage and generating events.
  let private resolveMeleeAttack: ResolverFn =
    fun
        ({
           entities = entities
           derivedStats = derivedStats
         },
         { actor = actorId; target = targetId }) -> adaptive {
      // Note: This is a simplified implementation for Phase 2.
      // It does not yet account for costs, cooldowns, or complex validation.
      let! isActorAndTargetPresent =
        entities |> AMap.exists(fun id _ -> id = actorId || id = targetId)

      if not isActorAndTargetPresent then
        return Array.empty, Map.empty
      else
        let! actorComponents = entities |> AMap.tryFind actorId
        let! targetComponents = entities |> AMap.tryFind targetId

        match actorComponents, targetComponents with
        | (None, Some _)
        | (Some _, None)
        | (None, None) -> return Array.empty, Map.empty
        | Some actorComponents, Some targetComponents ->
          let! actorStats = derivedStats |> AMap.find actorId
          let! targetStats = derivedStats |> AMap.find targetId

          // 1. Validate: Check if the attacker is alive.
          if actorComponents.Resources.Status <> Attributes.Status.Alive then
            return Array.empty, Map.empty // Attacker is not alive, so no action occurs.
          else
            // 2. Resolve: Calculate damage based on attacker's power and target's armor.
            let damage = max 0 (actorStats.AttackPower - targetStats.Armor)

            let damageEvent =
              DamageApplied { target = targetId; amount = damage }

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

            return events, changes
    }

  let private resolveCastSpell(spellId) : ResolverFn =
    fun
        ({
           entities = entities
           derivedStats = derivedStats
         },
         { actor = actorId; target = targetId }) ->

      adaptive {
        // Very simple placeholder spell: does spellPower as pure damage ignoring armor.
        // Note: This is a highly simplified implementation for Phase 2.
        // It does not yet account for spell costs, cooldowns, spell effects, or complex validation.
        let! isActorAndTargetPresent =
          entities |> AMap.exists(fun id _ -> id = actorId || id = targetId)

        if not isActorAndTargetPresent then
          return Array.empty, Map.empty
        else

          let! actorComponents = entities |> AMap.tryFind actorId
          let! targetComponents = entities |> AMap.tryFind targetId

          match actorComponents, targetComponents with
          | (None, Some _)
          | (Some _, None)
          | (None, None) -> return Array.empty, Map.empty
          | Some actorComponents, Some targetComponents ->

            if actorComponents.Resources.Status <> Attributes.Status.Alive then
              return Array.empty, Map.empty
            else
              let! actorStats = derivedStats |> AMap.find actorId
              let spellDamage = actorStats.SpellPower // placeholder, ignore spellId for now

              let targetHpAfter =
                max 0 (targetComponents.Resources.HP - spellDamage)

              let damageEvent =
                DamageApplied {
                  target = targetId
                  amount = spellDamage
                }

              let newResources = {
                targetComponents.Resources with
                    HP = targetHpAfter
              }

              let deathEvent, finalResources =
                if
                  targetHpAfter <= 0
                  && targetComponents.Resources.Status = Attributes.Status.Alive
                then
                  let event = EntityDied { entityId = targetId }

                  Some event,
                  {
                    newResources with
                        Status = Attributes.Status.Dead
                  }
                else
                  None, newResources

              let updatedTarget = {
                targetComponents with
                    Resources = finalResources
              }

              let changes = Map.ofList [ targetId, updatedTarget ]

              let events = [|
                damageEvent
                match deathEvent with
                | Some e -> e
                | None -> ()
              |]

              return events, changes
      }

  let private step
    (currentEntities: amap<EntityId, All>)
    (derivedStats: amap<EntityId, Attributes.DerivedStats>)
    (command: Command)
    : aval<GameEvent[] * Map<EntityId, All>> =
    let resolverParams = {
      entities = currentEntities
      derivedStats = derivedStats
    }

    match command with
    | MeleeAttack { actor = actor; target = target } ->
      resolveMeleeAttack(resolverParams, { actor = actor; target = target })
    | CastSpell action ->
      resolveCastSpell
        action.spellId
        (resolverParams,
         {
           actor = action.actor
           target = action.target
         })

  let apply (state: GameState) (cmd: Command) =
    let derivedStats = GameState.getDerivedStats state
    let events, changes = step state.entities derivedStats cmd |> AVal.force

    transact(fun _ ->
      state.gameEvents.AddRange events

      for change in changes do
        state.entities[change.Key] <- change.Value)
