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
    Lines: VisualEffects.ActiveLine[]
    ActiveZones: VisualEffects.ActiveZone[]
    GameTime: TimeSpan
    DerivedStats: HashMap<Guid<EntityId>, DerivedStats>
    WearableItems:
      HashMap<Guid<EntityId>, HashMap<Inventory.Slot, Inventory.InventoryItem>>
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
    (equipment: HashMap<Inventory.Slot, Inventory.EquipmentProperties>)
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

      for e, v in item.ElementalAttributes do
        elemAttr <- addElem elemAttr e v

      for e, v in item.ElementalResistances do
        elemRes <- addElem elemRes e v

    struct (statBonuses, elemAttr, elemRes)

  let applyModifiers
    (effectStore: Services.IEffectStore)
    (formulaStore: Services.IFormulaStore)
    (baseStats: BaseAttributes)
    (effects: HashMap<int<EffectId>, ActiveEffect>)
    (equipment: HashMap<Inventory.Slot, Inventory.EquipmentProperties>)
    : DerivedStats =
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
        | StaticMod sm ->
          match sm with
          | Additive(stat, v) -> addMap <- addInt stat (v * stacks) addMap
          | Subtractive(stat, v) -> addMap <- addInt stat (-v * stacks) addMap
          | Multiplicative(stat, v) ->
            let stacked = if stacks > 1 then Math.Pow(v, float stacks) else v

            factorMap <-
              HashMap.alterV
                stat
                (fun existing ->
                  match existing with
                  | ValueSome e -> ValueSome(e * stacked)
                  | ValueNone -> ValueSome stacked)
                factorMap
          | Divisive(stat, v) ->
            let stacked = if stacks > 1 then Math.Pow(v, float stacks) else v

            let inv = 1.0 / stacked

            factorMap <-
              HashMap.alterV
                stat
                (fun existing ->
                  match existing with
                  | ValueSome e -> ValueSome(e * inv)
                  | ValueNone -> ValueSome inv)
                factorMap

        | DynamicMod(formulaId, stat) ->
          dynamics.Add struct (formulaId, stat, stacks)
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
      MovementSpeed = 100
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
          MovementSpeed = applyAll addMap MovementSpeed initial.MovementSpeed
    }

    final

  let getWearableItems
    (itemStore: Services.IItemStore)
    (entity: EntityComponents)
    =
    entity.EquippedItems
    |> HashMap.chooseV(fun _ item ->
      let item =
        entity.Inventory
        |> HashMap.tryFindV item
        |> ValueOption.bind(fun item -> itemStore.tryFind item.ItemId)

      match item with
      | ValueSome definition ->
        match definition.Kind with
        | Inventory.Wearable equipment -> ValueSome equipment
        | Inventory.Usable _
        | Inventory.NonUsable -> ValueNone
      | ValueNone -> ValueNone)

  let inline byGameState(state: GameState) = adaptive {
    let! scenario = Scenario.ActiveScenario state

    return
      scenario.entities
      |> AMap.map(fun _ c ->
        let equipments = getWearableItems state.services.itemStore c

        applyModifiers
          state.services.effectStore
          state.services.formulaStore
          c.BaseStats
          c.Effects
          equipments)
  }

  let inline byScenario
    (services: Services.EngineServices)
    (state: Scenario.ScenarioState)
    =
    state.entities
    |> AMap.map(fun _ c ->
      let equipments = getWearableItems services.itemStore c

      applyModifiers
        services.effectStore
        services.formulaStore
        c.BaseStats
        c.Effects
        equipments)

  let inline byEntity
    (effectStore: Services.IEffectStore)
    (formulaStore: Services.IFormulaStore)
    (itemStore: Services.IItemStore)
    (entity: EntityComponents)
    =
    let equipments = getWearableItems itemStore entity

    applyModifiers
      effectStore
      formulaStore
      entity.BaseStats
      entity.Effects
      equipments

module Projectile =
  let resolve
    (state: GameState)
    (scenario: Scenario.ScenarioState)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    (time: TimeSpan)
    (newTime: TimeSpan)
    =
    scenario.activeObjects
    |> AMap.choose'(fun obj ->
      match obj with
      | ActiveObject.Projectile proj -> Some proj
      | _ -> None)
    |> AMap.mapA(fun objId proj -> adaptive {
      if newTime - proj.CreationTick > TimeSpan.FromSeconds 5.0 then
        // Timeout
        return {
          StateChange.empty with
              visualEffects = [|
                RemoveObject objId
                match proj.PendingResolutionId with
                | ValueSome resId -> RemoveObject(UMX.untag resId)
                | ValueNone -> ()
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

              let ftId = Guid.NewGuid()

              let ft = {
                Id = ftId |> UMX.tag
                Text = "Miss"
                Position = targetPos
                Color = Evade
                CreationTick = newTime
              }

              return {
                StateChange.empty with
                    visualEffects = [|
                      RemoveObject objId
                      match proj.PendingResolutionId with
                      | ValueSome resId -> RemoveObject(UMX.untag resId)
                      | ValueNone -> ()
                      AddObject(ftId, ActiveObject.FloatingText ft)
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
              match proj.PendingResolutionId with
              | ValueSome resolutionId ->
                let! resolutionOpt =
                  scenario.activeObjects
                  |> AMap.tryFind(UMX.untag resolutionId)

                let! resolution =
                  match resolutionOpt with
                  | Some(PendingResolution res) -> AVal.constant res
                  | _ -> AVal.constant Unchecked.defaultof<PendingResolution>

                let! actor = entities |> AMap.find resolution.ActorId

                let actorStats =
                  actor
                  |> DerivedStats.byEntity
                    state.services.effectStore
                    state.services.formulaStore
                    state.services.itemStore

                let ability =
                  state.services.abilityStore.find resolution.AbilityId

                match ability with
                | Abilities.Active abilityDef ->
                  let visualEffects = ResizeArray()
                  let mutable updates = HashMap.empty

                  visualEffects.Add(RemoveObject objId)

                  visualEffects.Add(RemoveObject(UMX.untag resolutionId))

                  let targetResults =
                    entitiesInZone
                    |> AMap.mapA(fun targetId target -> adaptive {
                      let targetStats =
                        target
                        |> DerivedStats.byEntity
                          state.services.effectStore
                          state.services.formulaStore
                          state.services.itemStore

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
                              Resolution.applyDamage
                                damageResult.Amount
                                target
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
                          let ftId = UMX.untag ft.Id

                          visualEffects.Add(
                            AddObject(ftId, ActiveObject.FloatingText ft)
                          ))

                        HashMap.add targetId updatedTarget acc)
                      HashMap.empty

                  updates <- finalUpdates

                  return {
                    StateChange.empty with
                        updates = updates
                        visualEffects = visualEffects.ToArray()
                  }
                | _ -> return StateChange.empty
              | ValueNone -> return StateChange.empty
          else
            let dirX = dx / dist
            let dirY = dy / dist
            let moveDist = def.Speed * float32 time.TotalSeconds

            let newPos = {
              X = proj.CurrentPosition.X + dirX * moveDist
              Y = proj.CurrentPosition.Y + dirY * moveDist
            }

            let projRadius = def.Size * 0.5f

            let! actorIdOpt =
              match proj.PendingResolutionId with
              | ValueSome resolutionId ->
                scenario.activeObjects
                |> AMap.tryFind(UMX.untag resolutionId)
                |> AVal.map(fun resOpt ->
                  match resOpt with
                  | Some(PendingResolution res) -> ValueSome res.ActorId
                  | _ -> ValueNone)
              | ValueNone -> AVal.constant ValueNone

            let! collidedEntity =
              entities
              |> AMap.filter(fun entityId _ ->
                match actorIdOpt with
                | ValueSome actorId -> entityId <> actorId
                | ValueNone -> true)
              |> AMap.fold
                (fun (acc: _ voption) entityId entity ->
                  if acc.IsValueSome then
                    acc
                  else
                    let entityRadius =
                      Movement.Utils.radiusOfStage entity.Identity.Stage

                    let dx = entity.Position.X - newPos.X
                    let dy = entity.Position.Y - newPos.Y
                    let distSq = dx * dx + dy * dy
                    let collisionDist = projRadius + entityRadius

                    if distSq <= collisionDist * collisionDist then
                      ValueSome(entityId, entity)
                    else
                      ValueNone)
                ValueNone

            match collidedEntity with
            | ValueSome(entityId, _) ->
              match proj.PendingResolutionId with
              | ValueSome resolutionId ->
                let! resolutionOpt =
                  scenario.activeObjects
                  |> AMap.tryFind(UMX.untag resolutionId)

                match resolutionOpt with
                | Some(PendingResolution res) ->
                  let! resolution = AVal.constant res
                  let! actor = entities |> AMap.find resolution.ActorId
                  let! entity = entities |> AMap.find entityId

                  let actorStats =
                    actor
                    |> DerivedStats.byEntity
                      state.services.effectStore
                      state.services.formulaStore
                      state.services.itemStore

                  let entityStats =
                    entity
                    |> DerivedStats.byEntity
                      state.services.effectStore
                      state.services.formulaStore
                      state.services.itemStore

                  let ability =
                    state.services.abilityStore.find resolution.AbilityId

                  match ability with
                  | Abilities.Active abilityDef ->
                    let damageParams = {
                      services = state.services
                      attackerStats = actorStats
                      defenderStats = entityStats
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

                    let entityAfterDamage = {
                      entity with
                          Resources =
                            Resolution.applyDamage damageResult.Amount entity
                    }

                    let! entityAfterEffects =
                      Resolution.applyAbilityEffects
                        state.services.effectStore
                        resolution.ActorId
                        abilityDef
                        entityAfterDamage

                    let floatingText =
                      if damageResult.IsEvaded then
                        let ftId = Guid.NewGuid()

                        Some(
                          AddObject(
                            ftId,
                            ActiveObject.FloatingText {
                              Id = ftId |> UMX.tag
                              Text = "Miss"
                              Position = entity.Position
                              Color = FloatingTextColor.Evade
                              CreationTick = newTime
                            }
                          )
                        )
                      elif damageResult.Amount > 0 then
                        let ftId = Guid.NewGuid()

                        Some(
                          AddObject(
                            ftId,
                            ActiveObject.FloatingText {
                              Id = ftId |> UMX.tag
                              Text = string damageResult.Amount
                              Position = entity.Position
                              Color =
                                if damageResult.IsCritical then
                                  FloatingTextColor.Critical
                                else
                                  FloatingTextColor.Damage
                              CreationTick = newTime
                            }
                          )
                        )
                      else
                        None

                    return {
                      StateChange.empty with
                          updates = HashMap.single entityId entityAfterEffects
                          visualEffects = [|
                            RemoveObject objId
                            RemoveObject(UMX.untag resolutionId)
                            yield! floatingText |> Option.toArray
                          |]
                    }
                  | _ ->
                    return {
                      StateChange.empty with
                          visualEffects = [|
                            RemoveObject objId
                            RemoveObject(UMX.untag resolutionId)
                          |]
                    }
                | _ ->
                  return {
                    StateChange.empty with
                        visualEffects = [| RemoveObject objId |]
                  }
              | ValueNone ->
                return {
                  StateChange.empty with
                      visualEffects = [| RemoveObject objId |]
                }
            | ValueNone ->

            match def.CollisionMode with
            | Visuals.CollisionMode.BlockedByTerrain ->
              let canMove =
                Collision.Query.canMoveTo newPos 5f scenario.scenario

              if not canMove then
                return {
                  StateChange.empty with
                      visualEffects = [|
                        RemoveObject objId
                        match proj.PendingResolutionId with
                        | ValueSome resId -> RemoveObject(UMX.untag resId)
                        | ValueNone -> ()
                      |]
                }
              else
                let updatedProj = { proj with CurrentPosition = newPos }

                return {
                  StateChange.empty with
                      visualEffects = [|
                        UpdateObject(
                          objId,
                          ActiveObject.Projectile updatedProj
                        )
                      |]
                }
            | Visuals.CollisionMode.IgnoreTerrain ->
              let updatedProj = { proj with CurrentPosition = newPos }

              return {
                StateChange.empty with
                    visualEffects = [|
                      UpdateObject(objId, ActiveObject.Projectile updatedProj)
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
            match proj.PendingResolutionId with
            | ValueSome resolutionId ->
              let! resolutionOpt =
                scenario.activeObjects |> AMap.tryFind(UMX.untag resolutionId)

              let! resolution =
                match resolutionOpt with
                | Some(PendingResolution res) -> AVal.constant res
                | _ -> AVal.constant Unchecked.defaultof<PendingResolution>

              let! actor = entities |> AMap.find resolution.ActorId

              let actorStats =
                actor
                |> DerivedStats.byEntity
                  state.services.effectStore
                  state.services.formulaStore
                  state.services.itemStore

              let targetStats =
                target
                |> DerivedStats.byEntity
                  state.services.effectStore
                  state.services.formulaStore
                  state.services.itemStore

              let ability =
                state.services.abilityStore.find resolution.AbilityId

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

                visualEffects.Add(RemoveObject objId)

                visualEffects.Add(RemoveObject(UMX.untag resolutionId))

                let impactCues =
                  Audio.Cues.createAbilityImpactCue
                    state.services.audioStore
                    resolution.AbilityId
                    targetId
                    target.Position
                    newTime

                audioChanges.AddRange impactCues

                if damageResult.IsEvaded then
                  let ftId = Guid.NewGuid()

                  let ft = {
                    Id = ftId |> UMX.tag
                    Text = "Miss"
                    Position = target.Position
                    Color = Evade
                    CreationTick = newTime
                  }

                  visualEffects.Add(
                    AddObject(ftId, ActiveObject.FloatingText ft)
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
                  let ftId = Guid.NewGuid()

                  let ft = {
                    Id = ftId |> UMX.tag
                    Text = string damageResult.Amount
                    Position = target.Position
                    Color =
                      if damageResult.IsCritical then Critical else Damage
                    CreationTick = newTime
                  }

                  visualEffects.Add(
                    AddObject(ftId, ActiveObject.FloatingText ft)
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
            | ValueNone -> return StateChange.empty
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
                  visualEffects = [|
                    UpdateObject(
                      objId,
                      ActiveObject.Projectile updatedProjectile
                    )
                  |]
            }

        | None -> // Target disappeared
          return {
            StateChange.empty with
                visualEffects = [|
                  RemoveObject objId
                  match proj.PendingResolutionId with
                  | ValueSome resId -> RemoveObject(UMX.untag resId)
                  | ValueNone -> ()
                |]
          }
    })

  let resolveNonProjectile
    (state: GameState)
    (scenario: Scenario.ScenarioState)
    (entities: amap<Guid<EntityId>, EntityComponents>)
    newTime
    =
    scenario.activeObjects
    |> AMap.chooseA(fun objId obj -> adaptive {
      match obj with
      | PendingResolution res ->
        let! isAttachedToProjectile =
          scenario.activeObjects
          |> AMap.exists(fun _ o ->
            match o with
            | ActiveObject.Projectile p ->
              match p.PendingResolutionId with
              | ValueSome resId -> UMX.untag resId = objId
              | ValueNone -> false
            | _ -> false)

        if not isAttachedToProjectile && newTime >= res.TriggerTick then
          return Some(objId, res)
        else
          return None
      | _ -> return None
    })
    |> AMap.mapA(fun objId (_, res) -> adaptive {
      // Handle AoE/Impact resolutions here as before
      let! actor = entities |> AMap.find res.ActorId

      match res.Target with
      | PositionResolution _ -> return StateChange.empty
      | EntityResolution targetId ->

      let! target = entities |> AMap.find targetId

      let actorStats =
        actor
        |> DerivedStats.byEntity
          state.services.effectStore
          state.services.formulaStore
          state.services.itemStore

      let targetStats =
        target
        |> DerivedStats.byEntity
          state.services.effectStore
          state.services.formulaStore
          state.services.itemStore

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
        visualEffects.Add(RemoveObject objId)

        if damageResult.IsEvaded then
          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text = "Miss"
            Position = target.Position
            Color = Evade
            CreationTick = newTime
          }

          visualEffects.Add(AddObject(ftId, ActiveObject.FloatingText ft))
        else
          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text = string damageResult.Amount
            Position = target.Position
            Color = if damageResult.IsCritical then Critical else Damage
            CreationTick = newTime
          }

          visualEffects.Add(AddObject(ftId, ActiveObject.FloatingText ft))


        return {
          StateChange.empty with
              updates = HashMap.single targetId targetAfterEffects
              visualEffects = visualEffects.ToArray()
        }
      | _ -> return StateChange.empty
    })

module ZoneEffects =

  let isEntityInZone (position: Position) (zone: VisualEffects.ActiveZone) =
    let dx = position.X - zone.Position.X
    let dy = position.Y - zone.Position.Y
    let dist2 = dx * dx + dy * dy
    dist2 <= zone.Radius * zone.Radius

  let applyEffectStacking
    (effectStore: Services.IEffectStore)
    (zoneId: Guid<ActiveZoneId>)
    (effectId: int<EffectId>)
    (existing: HashMap<int<EffectId>, ActiveEffect>)
    : HashMap<int<EffectId>, ActiveEffect> =

    let effDef = effectStore.find effectId

    match existing.TryFindV effectId with
    | ValueSome existingEffect ->
      match effDef.Stacking with
      | NoStack -> existing
      | RefreshDuration ->
        HashMap.add
          effectId
          {
            existingEffect with
                RemainingTicks =
                  effDef.Duration.Ticks
                  |> ValueOption.defaultValue TimeSpan.Zero
                NextTickIn =
                  effDef.Duration.Interval
                  |> ValueOption.defaultValue TimeSpan.Zero
          }
          existing
      | AddStack maxStacks ->
        HashMap.add
          effectId
          {
            existingEffect with
                Stacks = min maxStacks (existingEffect.Stacks + 1)
                RemainingTicks =
                  effDef.Duration.Ticks
                  |> ValueOption.defaultValue TimeSpan.Zero
                NextTickIn =
                  effDef.Duration.Interval
                  |> ValueOption.defaultValue TimeSpan.Zero
          }
          existing
    | ValueNone ->
      HashMap.add
        effectId
        {
          EffectId = effectId
          SourceId = UMX.cast zoneId
          RemainingTicks =
            effDef.Duration.Ticks |> ValueOption.defaultValue TimeSpan.Zero
          NextTickIn =
            effDef.Duration.Interval |> ValueOption.defaultValue TimeSpan.Zero
          Stacks = 1
          Definition = effDef
        }
        existing

  let applyZoneEffectsToEntity
    (effectStore: Services.IEffectStore)
    (zoneId: Guid<ActiveZoneId>)
    (effectsToApply: int<EffectId> array)
    (components: Components.EntityComponents)
    : Components.EntityComponents =

    let newEffects =
      effectsToApply
      |> Array.fold
        (fun effects effectId ->
          applyEffectStacking effectStore zoneId effectId effects)
        components.Effects

    { components with Effects = newEffects }

  let computeZoneEntrants
    (newTime: TimeSpan)
    (zoneId: Guid<ActiveZoneId>)
    (zone: VisualEffects.ActiveZone)
    (effectStore: Services.IEffectStore)
    (entities: amap<Guid<EntityId>, Components.EntityComponents>)
    : amap<Guid<EntityId>, Components.EntityComponents> =

    if newTime >= zone.EndTime then
      AMap.empty
    else
      entities
      |> AMap.choose(fun entId comps ->
        let isNewEntrant =
          isEntityInZone comps.Position zone
          && not(zone.EntitiesInside.Contains entId)

        if isNewEntrant then
          Some(
            applyZoneEffectsToEntity
              effectStore
              zoneId
              zone.EffectsToApply
              comps
          )
        else
          None)

  let computeZoneUpdate
    (newTime: TimeSpan)
    (zone: VisualEffects.ActiveZone)
    (entrantsMap: amap<Guid<EntityId>, Components.EntityComponents>)
    =

    adaptive {
      let! entrantIds = entrantsMap |> AMap.keys |> ASet.toAVal

      if HashSet.isEmpty entrantIds then
        return None
      else
        let updatedZone = {
          zone with
              EntitiesInside = HashSet.union zone.EntitiesInside entrantIds
        }

        return Some(UpdateActiveZone updatedZone)
    }

  let processAllZones
    (state: GameState)
    (newTime: TimeSpan)
    (entities: amap<Guid<EntityId>, Components.EntityComponents>)
    (activeZones: amap<Guid<ActiveZoneId>, VisualEffects.ActiveZone>)
    =
    adaptive {
      let allEntrantsPerZone =
        activeZones
        |> AMap.map(fun zoneId zone ->
          computeZoneEntrants
            newTime
            zoneId
            zone
            state.services.effectStore
            entities)

      let! mergedEntrants =
        allEntrantsPerZone
        |> AMap.reduce(
          AdaptiveReduction.fold AMap.empty (fun acc entMap ->
            AMap.union acc entMap)
        )

      let scenarioChanges =
        activeZones
        |> AMap.chooseA(fun zoneId zone -> adaptive {
          let! entrantsForZone = allEntrantsPerZone |> AMap.find zoneId
          return! computeZoneUpdate newTime zone entrantsForZone
        })

      return struct (mergedEntrants, scenarioChanges)
    }


module GameState =

  [<Struct>]
  type TickEffectsMappingArgs = {
    time: TimeSpan
    scenario: Scenario.Scenario
    effectStore: Services.IEffectStore
    formulaStore: Services.IFormulaStore
    itemStore: Services.IItemStore
  }

  let runTickEffects
    (args: TickEffectsMappingArgs)
    (gameTime: TimeSpan)
    currentEntityId
    currentEntity
    : aval<struct (EntityComponents * State.VisualEffectChange[])> =
    adaptive {
      let {
            time = time
            scenario = scenario
            effectStore = effectStore
            formulaStore = formulaStore
            itemStore = itemStore
          } =
        args

      let struct (updatedEffects, tickResult) =
        StatusEffects.tickEffects effectStore currentEntity.Effects time

      let derivedStatsForEntity =
        currentEntity
        |> DerivedStats.byEntity effectStore formulaStore itemStore

      let movementSpeed = float32 derivedStatsForEntity.MovementSpeed

      let movedComponents =
        Movement.Update.withPath
          time
          scenario
          currentEntityId
          currentEntity
          {
            Width = scenario.BoundsWidth
            Height = scenario.BoundsHeight
            CenterX = scenario.BoundsWidth * 0.5f
            CenterY = scenario.BoundsHeight * 0.5f
          }
          movementSpeed

      let maxHp = derivedStatsForEntity.HP
      let currentHp = movedComponents.Resources.HP
      let newHp = min maxHp (currentHp + tickResult.Resources.HP)

      let newMp =
        min
          derivedStatsForEntity.MP
          (movedComponents.Resources.MP + tickResult.Resources.MP)

      let updatedResources = {
        movedComponents.Resources with
            HP = max 0 (newHp - tickResult.Damage)
            MP = newMp
      }

      let visualEffects = ResizeArray()

      do
        if tickResult.Damage > 0 then
          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text = string tickResult.Damage
            Position = movedComponents.Position
            Color = VisualEffects.FloatingTextColor.Damage
            CreationTick = gameTime
          }

          visualEffects.Add(
            State.AddObject(ftId, VisualEffects.ActiveObject.FloatingText ft)
          )

      do
        if tickResult.Resources.HP > 0 then
          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text = $"+{tickResult.Resources.HP}"
            Position = movedComponents.Position
            Color = VisualEffects.FloatingTextColor.Heal
            CreationTick = gameTime
          }

          visualEffects.Add(
            State.AddObject(ftId, VisualEffects.ActiveObject.FloatingText ft)
          )

      do
        if tickResult.Resources.MP <> 0 then
          let ftId = Guid.NewGuid()

          let ft = {
            Id = ftId |> UMX.tag
            Text =
              if tickResult.Resources.MP > 0 then
                $"+{tickResult.Resources.MP} MP"
              else
                $"{tickResult.Resources.MP} MP"
            Position = movedComponents.Position
            Color =
              if tickResult.Resources.MP > 0 then
                VisualEffects.FloatingTextColor.Heal
              else
                VisualEffects.FloatingTextColor.Damage
            CreationTick = gameTime
          }

          visualEffects.Add(
            State.AddObject(ftId, VisualEffects.ActiveObject.FloatingText ft)
          )

      let updatedEntity = {
        movedComponents with
            Effects = updatedEffects
            Resources = updatedResources
      }

      return struct (updatedEntity, visualEffects.ToArray())
    }

  let tick (state: GameState) (time: TimeSpan) : aval<StateChange> = adaptive {
    let! activeId = state.activeScenarioId
    let scenario = state.scenarios[activeId]
    let! currentTime = scenario.gameTime

    let newTime = currentTime + time

    let entitiesWithVisuals =
      scenario.entities
      |> AMap.mapA(
        runTickEffects
          {
            time = time
            scenario = scenario.scenario
            effectStore = state.services.effectStore
            formulaStore = state.services.formulaStore
            itemStore = state.services.itemStore
          }
          newTime
      )

    let! struct (entities, effectTickVisuals) =
      entitiesWithVisuals
      |> AMap.fold
        (fun
             struct (entities, visualEffects)
             (entityId: Guid<EntityId>)
             struct (entity, visuals) ->
          struct (HashMap.add entityId entity entities,
                  ResizeArray.addRange visuals visualEffects))
        (HashMap.empty, ResizeArray.empty())

    let entities = AMap.ofHashMap entities

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
    let! expiredObjectRemovals =
      scenario.activeObjects
      |> AMap.choose(fun id obj ->
        match obj with
        | ActiveObject.FloatingText ft ->
          if newTime - ft.CreationTick > TimeSpan.FromSeconds 2.5 then
            Some(RemoveObject id)
          else
            None
        | ActiveObject.Impact impact ->
          let def = state.services.impactStore.find impact.DefinitionId
          let age = newTime - impact.CreationTick
          if age > def.Duration then Some(RemoveObject id) else None
        | ActiveObject.Aoe aoe ->
          let age = newTime - aoe.CreationTick

          if age > TimeSpan.FromSeconds 1.0 then
            Some(RemoveObject id)
          else
            None
        | ActiveObject.Line line ->
          let age = newTime - line.CreationTick
          if age > line.Duration then Some(RemoveObject id) else None
        | _ -> None)
      |> AMap.reduce(
        AdaptiveReduction.fold IndexList.empty (fun acc change ->
          IndexList.add change acc)
      )

    let visualEffectChanges =
      Array.concat [|
        effectTickVisuals |> ResizeArray.toArray
        projectileStateChanges.visualEffects
        nonProjectileResolutionChanges.visualEffects
        expiredObjectRemovals.AsArray
      |]

    let updates =
      projectileStateChanges.updates
      |> HashMap.union nonProjectileResolutionChanges.updates

    let! zoneRemovals =
      scenario.activeZones
      |> AMap.fold
        (fun acc id (zone: VisualEffects.ActiveZone) ->
          if newTime >= zone.EndTime then
            IndexList.add (RemoveActiveZone id) acc
          else
            acc)
        (IndexList.empty<State.ScenarioChange>)

    let! struct (zoneEntityUpdates, zoneScenarioChanges) =
      ZoneEffects.processAllZones state newTime entities scenario.activeZones

    let! zoneScenarioChanges =
      zoneScenarioChanges |> AMap.toAVal |> AVal.map HashMap.toValueArray

    let zoneScenarioChangesArray =
      Array.append zoneRemovals.AsArray zoneScenarioChanges

    let keyedEntities = entities |> AMap.map(fun id comp -> struct (id, comp))

    let! finalUpdatesBase =
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

    let! zoneEntityUpdates = zoneEntityUpdates |> AMap.toAVal
    let finalUpdates = HashMap.union finalUpdatesBase zoneEntityUpdates

    let audioChanges =
      Array.concat [|
        projectileStateChanges.audioChanges
        nonProjectileResolutionChanges.audioChanges
      |]

    return {
      StateChange.empty with
          updates = finalUpdates
          visualEffects = visualEffectChanges
          audioChanges = audioChanges
          scenarioChanges = zoneScenarioChangesArray
          gameTime = ValueSome newTime
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
        | AddObject(id, obj) -> scenario.activeObjects.Add(id, obj) |> ignore
        | UpdateObject(id, obj) -> scenario.activeObjects[id] <- obj
        | RemoveObject id -> scenario.activeObjects.Remove id |> ignore

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
        | AddActiveZone zone ->
          scenario.activeZones.Add(zone.Id, zone) |> ignore
        | UpdateActiveZone zone -> scenario.activeZones[zone.Id] <- zone
        | RemoveActiveZone zoneId ->
          scenario.activeZones.Remove zoneId |> ignore

      for entityId, updatedComponents in change.updates do
        scenario.entities[entityId] <- updatedComponents

      for entityId, newComponents in change.additions do
        scenario.entities.Add(entityId, newComponents) |> ignore

      for entityId in change.removals do
        scenario.entities.Remove entityId |> ignore
        scenario.aiControllers.Remove entityId |> ignore

      for entityId, controller in change.aiControllers do
        scenario.aiControllers[entityId] <- controller

      AILifecycle.cleanupDeadControllers
        scenario.entities
        scenario.aiControllers

      for tp in change.teleports do
        if state.scenarios.ContainsKey tp.ToScenarioId then
          let mutable moved = ValueNone

          for _, scState in state.scenarios do
            if scState.entities.ContainsKey tp.EntityId then
              let comps = scState.entities[tp.EntityId]
              moved <- ValueSome comps
              scState.entities.Remove tp.EntityId |> ignore
              scState.aiControllers.Remove tp.EntityId |> ignore

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
      and! activeObjects = scenarioState.activeObjects |> AMap.toAVal
      and! activeZones = scenarioState.activeZones |> AMap.toAVal

      let floatingTexts =
        activeObjects
        |> HashMap.chooseV(fun _ obj ->
          match obj with
          | ActiveObject.FloatingText ft -> ValueSome ft
          | _ -> ValueNone)

      let projectiles =
        activeObjects
        |> HashMap.chooseV(fun _ obj ->
          match obj with
          | ActiveObject.Projectile p -> ValueSome p
          | _ -> ValueNone)

      let aoes =
        activeObjects
        |> HashMap.chooseV(fun _ obj ->
          match obj with
          | ActiveObject.Aoe a -> ValueSome a
          | _ -> ValueNone)

      let impacts =
        activeObjects
        |> HashMap.chooseV(fun _ obj ->
          match obj with
          | ActiveObject.Impact i -> ValueSome i
          | _ -> ValueNone)

      let lines =
        activeObjects
        |> HashMap.chooseV(fun _ obj ->
          match obj with
          | ActiveObject.Line l -> ValueSome l
          | _ -> ValueNone)

      let! derivedStats =
        DerivedStats.byScenario services scenarioState |> AMap.toAVal

      let! wearableItems =
        scenarioState.entities
        |> AMap.map(fun _ e ->
          e.EquippedItems
          |> HashMap.chooseV(fun _ item ->
            e.Inventory |> HashMap.tryFindV item))
        |> AMap.toAVal

      return {
        Scenario = scenarioState.scenario
        Entities = entities
        FloatingTexts = floatingTexts |> HashMap.toValueArray
        Projectiles = projectiles |> HashMap.toValueArray
        Aoes = aoes |> HashMap.toValueArray
        Impacts = impacts |> HashMap.toValueArray
        Lines = lines |> HashMap.toValueArray
        ActiveZones = activeZones |> HashMap.toValueArray
        GameTime = gameTime
        DerivedStats = derivedStats
        WearableItems = wearableItems
      }
    }
