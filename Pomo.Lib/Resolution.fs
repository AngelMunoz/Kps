namespace Pomo.Lib.Rules

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives
open Pomo.Lib.Domain.Components
open Pomo.Lib.Content
open Pomo.Lib.Gameplay

module Resolution =

  type ResolverParams = {
    entities: amap<EntityId, All>
    derivedStats: amap<EntityId, Attributes.DerivedStats>
    gameTime: cval<int64<ticks>>
  }

  type ResolverActors = { actor: EntityId; target: EntityId }


  type ResolverFn =
    ResolverParams * ResolverActors -> aval<GameEvent[] * Map<EntityId, All>>

  /// A validation function that checks for the presence of actor and target, and the actor's status.
  let private validateAction
    (rparams: ResolverParams)
    (ractors: ResolverActors)
    (abilityId: Abilities.AbilityId)
    =
    adaptive {
      let! actor = rparams.entities |> AMap.tryFind ractors.actor
      let! target = rparams.entities |> AMap.tryFind ractors.target
      let! gameTime = rparams.gameTime

      match actor, target with
      | Some actor, Some target when
        actor.Resources.Status = Attributes.Status.Alive
        ->
        // Cooldown Check
        let! cooldowns = actor.AbilityCooldowns |> AMap.tryFind abilityId

        let isOnCooldown =
          match cooldowns with
          | Some readyTime -> gameTime < readyTime
          | None -> false

        if isOnCooldown then
          return None
        else
          // Cost Check
          let abilityDef = AbilityStore.definitions[abilityId]

          let hasEnoughResource, cost =
            match abilityDef.Cost with
            | Some c ->
              let hasEnough =
                match c.Type with
                | Abilities.ResourceType.HP -> actor.Resources.HP >= c.Amount
                | Abilities.ResourceType.MP -> actor.Resources.MP >= c.Amount
                | Abilities.ResourceType.Stamina ->
                  actor.Resources.Stamina >= c.Amount

              hasEnough, Some c
            | None -> true, None

          if hasEnoughResource then
            return Some(actor, target, cost, abilityDef)
          else
            return None
      | _ -> return None
    }

  /// Resolves a MeleeAttack command, calculating damage and generating events.
  let private resolveMeleeAttack(abilityId: Abilities.AbilityId) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let {
            entities = entities
            derivedStats = derivedStats
            gameTime = gameTime
          } =
        rparams

      let { actor = actorId; target = targetId } = ractors

      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None -> return Array.empty, Map.empty
      | Some(actorComponents, targetComponents, costOpt, abilityDef) ->
        let! actorStats = derivedStats |> AMap.find actorId
        let! targetStats = derivedStats |> AMap.find targetId
        let! gameTime = gameTime

        // 1. Resolve: Calculate damage based on attacker's power and target's armor.
        let damage = max 0 (actorStats.AttackPower - targetStats.Armor)

        let damageEvent = DamageApplied { target = targetId; amount = damage }

        // 2. Apply Changes: Update the target's health.
        let newHp = max 0 (targetComponents.Resources.HP - damage)

        let newResources = {
          targetComponents.Resources with
              HP = newHp
        }

        // 3. Check for Death
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

        let costEvents, actorComponents =
          match costOpt with
          | Some cost ->
            let amount, all =
              match cost.Type with
              | Abilities.ResourceType.HP ->
                let amount = actorComponents.Resources.HP - cost.Amount

                amount,
                {
                  actorComponents with
                      Resources.HP = amount
                }
              | Abilities.ResourceType.MP ->
                let amount = actorComponents.Resources.MP - cost.Amount

                amount,
                {
                  actorComponents with
                      Resources = {
                        actorComponents.Resources with
                            MP = amount
                      }
                }
              | Abilities.ResourceType.Stamina ->
                let amount = actorComponents.Resources.Stamina - cost.Amount

                amount,
                {
                  actorComponents with
                      Resources = {
                        actorComponents.Resources with
                            Stamina = amount
                      }
                }



            let ev =
              ResourceChanged {
                target = actorId
                resource = sprintf "%A" cost.Type
                newValue = amount
              }

            [| ev |], all
          | None -> Array.empty, actorComponents

        let updatedActor = {
          actorComponents with
              AbilityCooldowns =
                actorComponents.AbilityCooldowns
                |> AMap.map(fun k v ->
                  if k = abilityId then gameTime + abilityDef.Cooldown else v)
        }

        let changes =
          Map.ofList [ actorId, updatedActor; targetId, updatedTarget ]

        let events = [|
          damageEvent
          for ev in costEvents do
            ev
          match deathEvent with
          | Some e -> e
          | _ -> ()
        |]

        return events, changes
    }

  let private resolveCastSpell(abilityId: Abilities.AbilityId) : ResolverFn =
    fun (rparams, ractors) -> adaptive {
      let {
            entities = entities
            derivedStats = derivedStats
            gameTime = gameTime
          } =
        rparams

      let { actor = actorId; target = targetId } = ractors
      // This implementation now includes a flexible resource cost.
      // It does not yet account for cooldowns or complex spell effects.
      let! validationResult = validateAction rparams ractors abilityId

      match validationResult with
      | None -> return Array.empty, Map.empty
      | Some(actorComponents, targetComponents, costOpt, abilityDef) ->
        // 1. Validate Cost: Check if the actor has enough of the required resource.
        let! actorStats = derivedStats |> AMap.find actorId
        let! gameTime = gameTime
        let spellDamage = actorStats.SpellPower // placeholder, ignore spellId for now

        // 2. Apply Cost to Actor
        let (costEvents, actorComponents) =
          match costOpt with
          | Some cost ->
            let amount, all =
              match cost.Type with
              | Abilities.ResourceType.HP ->
                let amount = actorComponents.Resources.HP - cost.Amount

                amount,
                {
                  actorComponents with
                      Resources.HP = amount
                }
              | Abilities.ResourceType.MP ->
                let amount = actorComponents.Resources.MP - cost.Amount

                amount,
                {
                  actorComponents with
                      Resources.MP = amount
                }
              | Abilities.ResourceType.Stamina ->
                let amount = actorComponents.Resources.Stamina - cost.Amount

                amount,
                {
                  actorComponents with
                      Resources.Stamina = amount
                }

            let ev =
              ResourceChanged {
                target = actorId
                resource = sprintf "%A" cost.Type
                newValue = amount
              }

            [| ev |], all
          | None -> Array.empty, actorComponents


        let updatedActor = {
          actorComponents with
              AbilityCooldowns =
                actorComponents.AbilityCooldowns
                |> AMap.map(fun k v ->
                  if k = abilityId then gameTime + abilityDef.Cooldown else v)
        }

        // 3. Resolve Damage on Target
        let targetHpAfter = max 0 (targetComponents.Resources.HP - spellDamage)

        let damageEvent =
          DamageApplied {
            target = targetId
            amount = spellDamage
          }

        let targetNewResources = {
          targetComponents.Resources with
              HP = targetHpAfter
        }

        // 4. Check for Target Death
        let deathEvent, finalTargetResources =
          if
            targetHpAfter <= 0
            && targetComponents.Resources.Status = Attributes.Status.Alive
          then
            let event = EntityDied { entityId = targetId }

            Some event,
            {
              targetNewResources with
                  Status = Attributes.Status.Dead
            }
          else
            None, targetNewResources

        let updatedTarget = {
          targetComponents with
              Resources = finalTargetResources
        }

        // 5. Collate all changes and events
        let changes =
          Map.ofList [ actorId, updatedActor; targetId, updatedTarget ]

        let events =
          [| damageEvent |]
          |> Array.append costEvents
          |> Array.append(
            match deathEvent with
            | Some e -> [| e |]
            | _ -> Array.empty
          )

        return events, changes
    }

  let private step
    (currentEntities: amap<EntityId, All>)
    (derivedStats: amap<EntityId, Attributes.DerivedStats>)
    (gameTime: cval<int64<ticks>>)
    (command: Command)
    : aval<GameEvent[] * Map<EntityId, All>> =
    let resolverParams = {
      entities = currentEntities
      derivedStats = derivedStats
      gameTime = gameTime
    }

    match command with
    | MeleeAttack action ->
      resolveMeleeAttack
        action.abilityId
        (resolverParams,
         {
           actor = action.actor
           target = action.target
         })
    | CastSpell action ->
      resolveCastSpell
        action.abilityId
        (resolverParams,
         {
           actor = action.actor
           target = action.target
         })

  let apply (state: GameState) (cmd: Command) =
    let derivedStats = GameState.getDerivedStats state

    let events, changes =
      step state.entities derivedStats state.gameTime cmd |> AVal.force

    transact(fun _ ->
      state.gameEvents.AddRange events

      for change in changes do
        state.entities[change.Key] <- change.Value)
