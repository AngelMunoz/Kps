namespace Pomo.Lib.Gameplay

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Effects
open Pomo.Lib.Domain.State
open Pomo.Lib.Domain.AggregatedEffects
open Pomo.Lib.Scenario
open Pomo.Lib.Domain.VisualEffects
open Pomo.Lib.EffectApplication
open Pomo.Lib.EnemyAI

module Scenario =
  let ActiveScenario(state: GameState) = adaptive {
    let! scenarioId = state.activeScenarioId
    return state.scenarios[scenarioId]
  }

  [<Struct>]
  type DrawingContext = {
    Scenario: Scenario.Scenario
    Entities: HashMap<Guid<EntityId>, EntityComponents>
    FloatingTexts: VisualEffects.FloatingText[]
    Projectiles: VisualEffects.ActiveProjectile[]
    Aoes: VisualEffects.ActiveAoe[]
    Impacts: VisualEffects.ActiveImpact[]
    GameTime: TimeSpan
    DerivedStats: HashMap<Guid<EntityId>, DerivedStats>
  }


module ScenarioState =
  let inline getEntityById entityId (scenario: Scenario.ScenarioState) =
    scenario.entities |> AMap.tryFind entityId

module DerivedStats =

  let inline private addInt stat value =
    HashMap.alterV stat (fun existing ->
      match existing with
      | ValueSome e -> ValueSome(e + value)
      | ValueNone -> ValueSome value)

  let private aggregateEquipment
    (equipment: HashMap<Inventory.Slot, Inventory.Equipment>)
    =
    let mutable statBonuses = HashMap.empty<Stat, int>
    let mutable elemAttr = HashMap.empty<Element, float>
    let mutable elemRes = HashMap.empty<Element, float>

    let inline addElem map k v =
      map
      |> HashMap.alterV k (fun existing ->
        match existing with
        | ValueSome e -> ValueSome(e + v)
        | ValueNone -> ValueSome v)

    for _, item in equipment do
      for b in item.StatBonuses do
        statBonuses <- addInt b.Stat b.Value statBonuses

      for (e, v) in item.ElementalAttributes do
        elemAttr <- addElem elemAttr e v

      for (e, v) in item.ElementalResistances do
        elemRes <- addElem elemRes e v

    struct (statBonuses, elemAttr, elemRes)

  let applyModifiers
    (effectStore: Services.IEffectStore)
    (formulaStore: Services.IFormulaStore)
    (baseStats: BaseAttributes)
    (effects: HashMap<int<EffectId>, ActiveEffect>)
    (equipment: HashMap<Inventory.Slot, Inventory.Equipment>)
    : aval<DerivedStats> =
    adaptive {
      let struct (equipStatBonuses, equipElemAttr, equipElemRes) =
        aggregateEquipment equipment

      let mutable addMap = HashMap.empty<Stat, int>
      let mutable factorMap = HashMap.empty<Stat, float>
      let mutable dynamics = ResizeArray<struct (int<FormulaId> * Stat * int)>()

      effects
      |> HashMap.iter(fun _ eff ->
        let stacks = eff.Stacks

        let mods =
          effectStore.tryFind eff.EffectId
          |> ValueOption.map _.Modifiers
          |> ValueOption.defaultValue Array.empty

        mods
        |> Array.iter(fun m ->
          match m with
          | EffectModifier.StaticMod sm ->
            match sm with
            | StatModifier.Additive(stat, v) ->
              addMap <- addInt stat (v * stacks) addMap
            | StatModifier.Subtractive(stat, v) ->
              addMap <- addInt stat (-v * stacks) addMap
            | StatModifier.Multiplicative(stat, v) ->
              let stacked =
                if stacks > 1 then Math.Pow(v, float stacks) else v

              factorMap <-
                HashMap.alterV
                  stat
                  (fun existing ->
                    match existing with
                    | ValueSome e -> ValueSome(e * stacked)
                    | ValueNone -> ValueSome stacked)
                  factorMap
            | StatModifier.Divisive(stat, v) ->
              let stacked =
                if stacks > 1 then Math.Pow(v, float stacks) else v

              let inv = 1.0 / stacked

              factorMap <-
                HashMap.alterV
                  stat
                  (fun existing ->
                    match existing with
                    | ValueSome e -> ValueSome(e * inv)
                    | ValueNone -> ValueSome inv)
                  factorMap

          | EffectModifier.DynamicMod(formulaId, stat) ->
            dynamics.Add(struct (formulaId, stat, stacks))
          | _ -> ()))

      let inline applyAll (addMapRef: HashMap<Stat, int>) stat current =
        let addV = HashMap.tryFindV stat addMapRef |> ValueOption.defaultValue 0

        let factor =
          HashMap.tryFindV stat factorMap |> ValueOption.defaultValue 1.0

        let equipV =
          HashMap.tryFindV stat equipStatBonuses |> ValueOption.defaultValue 0

        int(float(current + addV + equipV) * factor)

      let initial = {
        AP = baseStats.Power * 2
        AC = baseStats.Power + int(float baseStats.Power * 1.25)
        DX = baseStats.Power
        MP = baseStats.Magic * 5
        MA = baseStats.Magic * 2
        MD = baseStats.Magic + int(float baseStats.Magic * 1.25)
        WT = baseStats.Sense * 5
        DA = baseStats.Sense * 2
        LK = baseStats.Sense + int(float baseStats.Sense * 0.5)
        HP = baseStats.Charm * 10
        DP = baseStats.Charm + int(float baseStats.Charm * 1.25)
        HV = baseStats.Charm * 2
        ElementAttributes = equipElemAttr
        ElementResistances = equipElemRes
      }

      dynamics
      |> Seq.iter(fun struct (formulaId, stat, stacks) ->
        match formulaStore.tryFind formulaId with
        | ValueSome f ->
          let ctx: Abilities.CalculationContext = {
            InvokerStats = initial
            InvokerElementalAttributes = initial.ElementAttributes
            TargetElementalResistances = HashMap.empty
          }

          let value = f.Calculate ctx |> _.BaseDamage

          if stacks > 1 then
            addMap <- addInt stat (value * stacks) addMap
          else
            addMap <- addInt stat value addMap
        | ValueNone -> ())

      let final = {
        initial with
            HP = applyAll addMap HP initial.HP
            MP = applyAll addMap MP initial.MP
            AP = applyAll addMap AP initial.AP
            MA = applyAll addMap MA initial.MA
            MD = applyAll addMap MD initial.MD
            DA = applyAll addMap DA initial.DA
            DX = applyAll addMap DX initial.DX
            WT = applyAll addMap WT initial.WT
            LK = applyAll addMap LK initial.LK
            DP = applyAll addMap DP initial.DP
            AC = applyAll addMap AC initial.AC
            HV = applyAll addMap HV initial.HV
      }

      return final
    }

  let inline byGameState(state: GameState) = adaptive {
    let! scenario = Scenario.ActiveScenario state

    return
      scenario.entities
      |> AMap.mapA(fun _ c ->
        applyModifiers
          state.services.effectStore
          state.services.formulaStore
          c.BaseStats
          c.Effects
          c.Equipment)
  }

  let inline byScenario
    (services: Services.EngineServices)
    (state: Scenario.ScenarioState)
    =
    state.entities
    |> AMap.mapA(fun _ c ->
      applyModifiers
        services.effectStore
        services.formulaStore
        c.BaseStats
        c.Effects
        c.Equipment)

  let inline byEntity
    (effectStore: Services.IEffectStore)
    (formulaStore: Services.IFormulaStore)
    (entity: EntityComponents)
    =
    applyModifiers
      effectStore
      formulaStore
      entity.BaseStats
      entity.Effects
      entity.Equipment

module Projectile =
  let resolve
    (state: GameState)
    (scenario: Scenario.ScenarioState)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (time: TimeSpan)
    (newTime: TimeSpan)
    =
    scenario.projectiles
    |> AMap.mapA(fun projId proj -> adaptive {
      if newTime - proj.CreationTick > TimeSpan.FromSeconds(5.0) then
        // Timeout
        return {
          StateChange.empty with
              visualEffects = [|
                RemoveProjectile projId
                RemovePendingResolution proj.PendingResolutionId
              |]
        }
      else
        match proj.Target with
        | PositionTarget targetPos ->
          let def = state.services.projectileStore.find proj.DefinitionId
          let dx = targetPos.X - proj.CurrentPosition.X
          let dy = targetPos.Y - proj.CurrentPosition.Y
          let dist = sqrt(dx * dx + dy * dy)



          if dist < 5f then
            let impactRadius = def.ImpactRadius |> ValueOption.defaultValue 0f

            let entitiesInZone =
              entities
              |> AMap.filterA(fun _ e -> adaptive {
                let dx = e.Position.X - targetPos.X
                let dy = e.Position.Y - targetPos.Y
                let dist = sqrt(dx * dx + dy * dy)
                return dist <= impactRadius
              })

            let! isEmpty = AMap.isEmpty entitiesInZone

            if isEmpty then
              let cues =
                state.services.audioStore.findByTrigger
                  Audio.AudioTrigger.MissedHit

              return {
                StateChange.empty with
                    visualEffects = [|
                      RemoveProjectile projId
                      RemovePendingResolution proj.PendingResolutionId
                      AddFloatingText {
                        Id = Guid.NewGuid() |> UMX.tag
                        Text = "Miss"
                        Position = targetPos
                        Color = FloatingTextColor.Evade
                        CreationTick = newTime
                      }
                    |]
                    audioChanges =
                      cues
                      |> Array.map(fun clipId ->
                        Audio.PlayAudio {
                          Id = %Guid.NewGuid()
                          ClipId = clipId
                          Trigger = Audio.AudioTrigger.MissedHit
                          SpatialInfo =
                            ValueSome {
                              Position = targetPos
                              MaxDistance = 500f
                              Rolloff = 1f
                            }
                          CreationTick = newTime
                          EntityId = ValueNone
                        })
              }
            else
              let! resolution =
                scenario.pendingResolutions
                |> AMap.find proj.PendingResolutionId

              let! actor = entities |> AMap.find resolution.ActorId

              let! actorStats =
                actor
                |> DerivedStats.byEntity
                  state.services.effectStore
                  state.services.formulaStore

              let ability =
                state.services.abilityStore.find resolution.AbilityId

              match ability with
              | Abilities.Active abilityDef ->
                let visualEffects = ResizeArray()
                let mutable updates = HashMap.empty

                visualEffects.Add(RemoveProjectile projId)

                visualEffects.Add(
                  RemovePendingResolution proj.PendingResolutionId
                )

                let targetResults =
                  entitiesInZone
                  |> AMap.mapA(fun targetId target -> adaptive {
                    let! targetStats =
                      target
                      |> DerivedStats.byEntity
                        state.services.effectStore
                        state.services.formulaStore

                    let damageParams = {
                      services = state.services
                      attackerStats = actorStats
                      defenderStats = targetStats
                      attackerEffects = actor.Effects
                    }

                    let! damageResult =
                      match abilityDef.FormulaId with
                      | ValueSome formulaId ->
                        Resolution.calculateDamage damageParams formulaId
                      | ValueNone ->
                        AVal.constant {
                          Amount = 0
                          IsCritical = false
                          IsEvaded = false
                        }

                    let targetAfterDamage = {
                      target with
                          Resources =
                            Resolution.applyDamage damageResult.Amount target
                    }

                    let! targetAfterEffects =
                      Resolution.applyAbilityEffects
                        state.services.effectStore
                        resolution.ActorId
                        abilityDef
                        targetAfterDamage

                    let floatingText =
                      if damageResult.IsEvaded then
                        ValueSome {
                          Id = Guid.NewGuid() |> UMX.tag
                          Text = "Miss"
                          Position = target.Position
                          Color = FloatingTextColor.Evade
                          CreationTick = newTime
                        }
                      elif damageResult.Amount > 0 then
                        ValueSome {
                          Id = Guid.NewGuid() |> UMX.tag
                          Text = string damageResult.Amount
                          Position = target.Position
                          Color =
                            if damageResult.IsCritical then
                              FloatingTextColor.Critical
                            else
                              FloatingTextColor.Damage
                          CreationTick = newTime
                        }
                      else
                        ValueNone

                    return struct (targetAfterEffects, floatingText)
                  })


                let! finalUpdates =
                  targetResults
                  |> AMap.fold
                    (fun acc targetId struct (updatedTarget, floatingText) ->
                      floatingText
                      |> ValueOption.iter(fun ft ->
                        visualEffects.Add(AddFloatingText ft))

                      HashMap.add targetId updatedTarget acc)
                    HashMap.empty

                updates <- finalUpdates

                return {
                  StateChange.empty with
                      updates = updates
                      visualEffects = visualEffects.ToArray()
                }
              | _ -> return StateChange.empty
          else
            let dirX = dx / dist
            let dirY = dy / dist
            let moveDist = def.Speed * float32 time.TotalSeconds

            let newPos = {
              X = proj.CurrentPosition.X + dirX * moveDist
              Y = proj.CurrentPosition.Y + dirY * moveDist
            }

            match def.CollisionMode with
            | Visuals.CollisionMode.BlockedByTerrain ->
              let canMove =
                Collision.Query.canMoveTo newPos 5f scenario.scenario

              if not canMove then
                return {
                  StateChange.empty with
                      visualEffects = [|
                        RemoveProjectile projId
                        RemovePendingResolution proj.PendingResolutionId
                      |]
                }
              else
                return {
                  StateChange.empty with
                      visualEffects = [|
                        UpdateProjectile { proj with CurrentPosition = newPos }
                      |]
                }
            | Visuals.CollisionMode.IgnoreTerrain ->
              return {
                StateChange.empty with
                    visualEffects = [|
                      UpdateProjectile { proj with CurrentPosition = newPos }
                    |]
              }
        | EntityTarget targetId ->

        let! targetOpt = entities |> AMap.tryFind targetId

        match targetOpt with
        | Some target ->
          let def = state.services.projectileStore.find proj.DefinitionId

          let targetRadius = Movement.Utils.radiusOfStage target.Identity.Stage

          let dx = target.Position.X - proj.CurrentPosition.X
          let dy = target.Position.Y - proj.CurrentPosition.Y
          let dist = sqrt(dx * dx + dy * dy)

          if dist < targetRadius then
            // Collision
            let! resolution =
              scenario.pendingResolutions |> AMap.find proj.PendingResolutionId

            let! actor = entities |> AMap.find resolution.ActorId

            let! actorStats =
              actor
              |> DerivedStats.byEntity
                state.services.effectStore
                state.services.formulaStore

            let! targetStats =
              target
              |> DerivedStats.byEntity
                state.services.effectStore
                state.services.formulaStore

            let ability = state.services.abilityStore.find resolution.AbilityId

            match ability with
            | Abilities.Active abilityDef ->
              let damageParams = {
                services = state.services
                attackerStats = actorStats
                defenderStats = targetStats
                attackerEffects = actor.Effects
              }

              let! damageResult =
                match abilityDef.FormulaId with
                | ValueSome formulaId ->
                  Resolution.calculateDamage damageParams formulaId
                | ValueNone ->
                  AVal.constant {
                    Amount = 0
                    IsCritical = false
                    IsEvaded = false
                  }

              let targetAfterDamage = {
                target with
                    Resources =
                      Resolution.applyDamage damageResult.Amount target
              }

              let! targetAfterEffects =
                Resolution.applyAbilityEffects
                  state.services.effectStore
                  resolution.ActorId
                  abilityDef
                  targetAfterDamage

              let visualEffects = ResizeArray()
              let audioChanges = ResizeArray()

              visualEffects.Add(RemoveProjectile projId)

              visualEffects.Add(
                RemovePendingResolution proj.PendingResolutionId
              )

              let impactCues =
                Audio.Cues.createAbilityImpactCue
                  state.services.audioStore
                  resolution.AbilityId
                  targetId
                  target.Position
                  newTime

              audioChanges.AddRange(impactCues)

              if damageResult.IsEvaded then
                visualEffects.Add(
                  AddFloatingText {
                    Id = Guid.NewGuid() |> UMX.tag
                    Text = "Miss"
                    Position = target.Position
                    Color = FloatingTextColor.Evade
                    CreationTick = newTime
                  }
                )

                let cues =
                  state.services.audioStore.findByTrigger
                    Audio.AudioTrigger.MissedHit

                cues
                |> Array.map(fun clipId ->
                  Audio.PlayAudio {
                    Id = %Guid.NewGuid()
                    ClipId = clipId
                    Trigger = Audio.AudioTrigger.MissedHit
                    SpatialInfo =
                      ValueSome {
                        Position = target.Position
                        MaxDistance = 500f
                        Rolloff = 1f
                      }
                    CreationTick = newTime
                    EntityId = ValueSome targetId
                  })
                |> audioChanges.AddRange

              elif damageResult.Amount > 0 then
                visualEffects.Add(
                  AddFloatingText {
                    Id = Guid.NewGuid() |> UMX.tag
                    Text = string damageResult.Amount
                    Position = target.Position
                    Color =
                      if damageResult.IsCritical then
                        FloatingTextColor.Critical
                      else
                        FloatingTextColor.Damage
                    CreationTick = newTime
                  }
                )

                let damageCue =
                  Audio.AudioTrigger.DamageTaken damageResult.IsCritical

                let cues = state.services.audioStore.findByTrigger damageCue

                cues
                |> Array.map(fun clipId ->
                  Audio.PlayAudio {
                    Id = %Guid.NewGuid()
                    ClipId = clipId
                    Trigger = damageCue
                    SpatialInfo =
                      ValueSome {
                        Position = target.Position
                        MaxDistance = 500f
                        Rolloff = 1f
                      }
                    CreationTick = newTime
                    EntityId = ValueSome targetId
                  })
                |> audioChanges.AddRange

              return {
                StateChange.empty with
                    updates = HashMap.single targetId targetAfterEffects
                    visualEffects = visualEffects.ToArray()
                    audioChanges = audioChanges.ToArray()
              }
            | _ -> return StateChange.empty
          else
            // No collision, update position
            let dirX = dx / dist
            let dirY = dy / dist
            let moveDist = def.Speed * float32 time.TotalSeconds

            let newPos = {
              X = proj.CurrentPosition.X + dirX * moveDist
              Y = proj.CurrentPosition.Y + dirY * moveDist
            }

            let updatedProjectile = { proj with CurrentPosition = newPos }

            return {
              StateChange.empty with
                  visualEffects = [| UpdateProjectile updatedProjectile |]
            }

        | None -> // Target disappeared
          return {
            StateChange.empty with
                visualEffects = [|
                  RemoveProjectile projId
                  RemovePendingResolution proj.PendingResolutionId
                |]
          }
    })

  let resolveNonProjectile
    (state: GameState)
    (scenario: Scenario.ScenarioState)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    newTime
    =
    scenario.pendingResolutions
    |> AMap.filterA(fun _ res -> adaptive {
      let! isProjectile =
        scenario.projectiles
        |> AMap.exists(fun _ p -> p.PendingResolutionId = res.Id)

      return not isProjectile && newTime >= res.TriggerTick
    })
    |> AMap.mapA(fun _ res -> adaptive {
      // Handle AoE/Impact resolutions here as before
      let! actor = entities |> AMap.find res.ActorId

      match res.Target with
      | PositionResolution _ -> return StateChange.empty
      | EntityResolution targetId ->

      let! target = entities |> AMap.find targetId

      let! actorStats =
        actor
        |> DerivedStats.byEntity
          state.services.effectStore
          state.services.formulaStore

      let! targetStats =
        target
        |> DerivedStats.byEntity
          state.services.effectStore
          state.services.formulaStore

      let ability = state.services.abilityStore.find res.AbilityId

      match ability with
      | Abilities.Active abilityDef ->
        let damageParams = {
          services = state.services
          attackerStats = actorStats
          defenderStats = targetStats
          attackerEffects = actor.Effects
        }

        let! damageResult =
          match abilityDef.FormulaId with
          | ValueSome formulaId ->
            Resolution.calculateDamage damageParams formulaId
          | ValueNone ->
            AVal.constant {
              Amount = 0
              IsCritical = false
              IsEvaded = false
            }

        let targetAfterDamage = {
          target with
              Resources = Resolution.applyDamage damageResult.Amount target
        }

        let! targetAfterEffects =
          Resolution.applyAbilityEffects
            state.services.effectStore
            res.ActorId
            abilityDef
            targetAfterDamage

        let visualEffects = ResizeArray()
        visualEffects.Add(RemovePendingResolution res.Id)

        if damageResult.IsEvaded then
          visualEffects.Add(
            AddFloatingText {
              Id = Guid.NewGuid() |> UMX.tag
              Text = "Miss"
              Position = target.Position
              Color = FloatingTextColor.Evade
              CreationTick = newTime
            }
          )
        else
          visualEffects.Add(
            AddFloatingText {
              Id = Guid.NewGuid() |> UMX.tag
              Text = string damageResult.Amount
              Position = target.Position
              Color =
                if damageResult.IsCritical then
                  FloatingTextColor.Critical
                else
                  FloatingTextColor.Damage
              CreationTick = newTime
            }
          )

        return {
          StateChange.empty with
              updates = HashMap.single targetId targetAfterEffects
              visualEffects = visualEffects.ToArray()
        }
      | _ -> return StateChange.empty
    })

module GameState =

  [<Struct>]
  type TickEffectsMappingArgs = {
    time: TimeSpan
    scenario: Scenario.Scenario
    effectStore: Services.IEffectStore
    formulaStore: Services.IFormulaStore
  }

  let runTickEffects
    (args: TickEffectsMappingArgs)
    currentEntityId
    currentEntity
    : aval<EntityComponents> =
    adaptive {
      let {
            time = time
            scenario = scenario
            effectStore = effectStore
            formulaStore = formulaStore
          } =
        args

      let struct (updatedEffects, tickResult) =
        StatusEffects.tickEffects effectStore currentEntity.Effects time

      let movedComponents =
        {
          Width = scenario.BoundsWidth
          Height = scenario.BoundsHeight
          CenterX = scenario.BoundsWidth * 0.5f
          CenterY = scenario.BoundsHeight * 0.5f
        }
        |> Movement.Update.withPath time scenario currentEntityId currentEntity

      let! derivedStatsForEntity =
        movedComponents |> DerivedStats.byEntity effectStore formulaStore


      let maxHp = derivedStatsForEntity.HP
      let currentHp = movedComponents.Resources.HP
      let newHp = min maxHp (currentHp + tickResult.Healing)

      let updatedResources = {
        movedComponents.Resources with
            HP = max 0 (newHp - tickResult.Damage)
      }

      return {
        movedComponents with
            Effects = updatedEffects
            Resources = updatedResources
      }
    }

  let tick (state: GameState) (time: TimeSpan) : aval<StateChange> = adaptive {
    let! activeId = state.activeScenarioId
    let scenario = state.scenarios[activeId]
    let! currentTime = scenario.gameTime

    let newTime = currentTime + time

    let entities =
      scenario.entities
      |> AMap.mapA(
        runTickEffects {
          time = time
          scenario = scenario.scenario
          effectStore = state.services.effectStore
          formulaStore = state.services.formulaStore
        }
      )

    let! projectileStateChanges =
      Projectile.resolve state scenario entities time newTime
      |> AMap.reduce(
        AdaptiveReduction.fold StateChange.empty (fun acc change -> {
          acc with
              updates = HashMap.union acc.updates change.updates
              visualEffects =
                Array.append acc.visualEffects change.visualEffects
              audioChanges = Array.append acc.audioChanges change.audioChanges
        })
      )

    let! nonProjectileResolutionChanges =
      Projectile.resolveNonProjectile state scenario entities newTime
      |> AMap.reduce(
        AdaptiveReduction.fold StateChange.empty (fun acc change -> {
          acc with
              updates = HashMap.union acc.updates change.updates
              visualEffects =
                Array.append acc.visualEffects change.visualEffects
              audioChanges = Array.append acc.audioChanges change.audioChanges
        })
      )

    // Generate removal changes for expired visual effects
    let! floatingTextRemovals =
      scenario.floatingTexts
      |> AMap.choose(fun id ft ->
        if newTime - ft.CreationTick > TimeSpan.FromSeconds(2.5) then
          Some(RemoveFloatingText id)
        else
          None)
      |> AMap.reduce(
        AdaptiveReduction.fold IndexList.empty (fun acc change ->
          IndexList.add change acc)
      )

    let! impactRemovals =
      scenario.impacts
      |> AMap.choose(fun id (impact: ActiveImpact) ->
        let def = state.services.impactStore.find impact.DefinitionId

        let age = newTime - impact.CreationTick

        if age > def.Duration then Some(RemoveImpact id) else None)
      |> AMap.reduce(
        AdaptiveReduction.fold IndexList.empty (fun acc change ->
          IndexList.add change acc)
      )

    let! aoeRemovals =
      scenario.aoes
      |> AMap.choose(fun id (aoe: ActiveAoe) ->
        let age = newTime - aoe.CreationTick

        if age > TimeSpan.FromSeconds(1.0) then
          Some(RemoveAoe id)
        else
          None)
      |> AMap.reduce(
        AdaptiveReduction.fold IndexList.empty (fun acc change ->
          IndexList.add change acc)
      )

    let visualEffectChanges =
      Array.concat [|
        projectileStateChanges.visualEffects
        nonProjectileResolutionChanges.visualEffects
        floatingTextRemovals.AsArray
        impactRemovals.AsArray
        aoeRemovals.AsArray
      |]

    let updates =
      projectileStateChanges.updates
      |> HashMap.union nonProjectileResolutionChanges.updates

    let keyedEntities = entities |> AMap.map(fun id comp -> struct (id, comp))

    let! finalUpdates =
      keyedEntities
      |> AMap.reduce(
        AdaptiveReduction.fold updates (fun acc struct (id, comp) ->
          HashMap.alterV
            id
            (fun existing ->
              match existing with
              | ValueSome existing ->
                ValueSome {
                  existing with
                      Position = comp.Position
                      Movement = comp.Movement
                }
              | ValueNone -> ValueSome comp)
            acc)
      )


    let audioChanges =
      Array.concat [|
        projectileStateChanges.audioChanges
        nonProjectileResolutionChanges.audioChanges
      |]

    let! updatedAiControllers =
      AISystem.processAllControllers
        scenario.entities
        state.services.aiArchetypeStore
        newTime
        scenario.aiControllers
      |> AMap.toAVal

    return {
      StateChange.empty with
          updates = finalUpdates
          visualEffects = visualEffectChanges
          audioChanges = audioChanges
          gameTime = ValueSome newTime
          aiControllers = updatedAiControllers
    }
  }

  let apply (state: GameState) (change: StateChange) =
    transact(fun _ ->
      let activeId = state.activeScenarioId.Value
      let scenario = state.scenarios[activeId]

      match change.gameTime with
      | ValueSome newTime -> scenario.gameTime.Value <- newTime
      | ValueNone -> ()

      for effect in change.visualEffects do
        match effect with
        | AddFloatingText ft -> scenario.floatingTexts.Add(ft.Id, ft) |> ignore
        | RemoveFloatingText ftId ->
          scenario.floatingTexts.Remove ftId |> ignore
        | AddProjectile p -> scenario.projectiles.Add(p.Id, p) |> ignore
        | UpdateProjectile p -> scenario.projectiles.[p.Id] <- p
        | RemoveProjectile pId -> scenario.projectiles.Remove pId |> ignore
        | AddAoe a -> scenario.aoes.Add(a.Id, a) |> ignore
        | RemoveAoe aId -> scenario.aoes.Remove aId |> ignore
        | AddImpact i -> scenario.impacts.Add(i.Id, i) |> ignore
        | RemoveImpact iId -> scenario.impacts.Remove iId |> ignore
        | AddPendingResolution res ->
          scenario.pendingResolutions.Add(res.Id, res) |> ignore
        | RemovePendingResolution resId ->
          scenario.pendingResolutions.Remove resId |> ignore

      for sc in change.scenarioChanges do
        match sc with
        | AddBattleInstance bi ->
          scenario.battleInstances.Add(bi.Id, bi) |> ignore
        | UpdateBattleInstance bi -> scenario.battleInstances[bi.Id] <- bi
        | RemoveBattleInstance biId ->
          scenario.battleInstances.Remove biId |> ignore
        | AddPendingDuel(requester, target) ->
          scenario.pendingDuels.Add(requester, target) |> ignore
        | RemovePendingDuel requester ->
          scenario.pendingDuels.Remove requester |> ignore
        | AddPendingPartyDuel(requester, target) ->
          scenario.pendingPartyDuels.Add(requester, target) |> ignore
        | RemovePendingPartyDuel requester ->
          scenario.pendingPartyDuels.Remove requester |> ignore

      for entityId, updatedComponents in change.updates do
        scenario.entities[entityId] <- updatedComponents

      for entityId, newComponents in change.additions do
        scenario.entities.Add(entityId, newComponents) |> ignore

      for entityId in change.removals do
        scenario.entities.Remove entityId |> ignore

      for entityId, controller in change.aiControllers do
        scenario.aiControllers[entityId] <- controller

      for tp in change.teleports do
        if state.scenarios.ContainsKey tp.ToScenarioId then
          let mutable moved = ValueNone

          for _, scState in state.scenarios do
            if scState.entities.ContainsKey tp.EntityId then
              let comps = scState.entities[tp.EntityId]
              moved <- ValueSome comps
              scState.entities.Remove tp.EntityId |> ignore

          match moved with
          | ValueSome comps ->
            let targetScenarioState = state.scenarios[tp.ToScenarioId]

            targetScenarioState.entities[tp.EntityId] <-
              { comps with Position = tp.ToPosition }
          | ValueNone -> ()

      ())

  let GetDrawingContext
    services
    (scenarioState: Scenario.ScenarioState aval)
    : Scenario.DrawingContext aval =
    adaptive {
      let! scenarioState = scenarioState
      let! gameTime = scenarioState.gameTime
      and! entities = scenarioState.entities |> AMap.toAVal
      and! floatingTexts = scenarioState.floatingTexts |> AMap.toAVal
      and! projectiles = scenarioState.projectiles |> AMap.toAVal
      and! aoes = scenarioState.aoes |> AMap.toAVal
      and! impacts = scenarioState.impacts |> AMap.toAVal

      let! derivedStats =
        DerivedStats.byScenario services scenarioState |> AMap.toAVal

      return {
        Scenario = scenarioState.scenario
        Entities = entities
        FloatingTexts = floatingTexts |> HashMap.toValueArray
        Projectiles = projectiles |> HashMap.toValueArray
        Aoes = aoes |> HashMap.toValueArray
        Impacts = impacts |> HashMap.toValueArray
        GameTime = gameTime
        DerivedStats = derivedStats
      }
    }
