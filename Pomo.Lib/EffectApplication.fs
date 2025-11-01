namespace Pomo.Lib.EffectApplication

open System
open FSharp.UMX
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Effects
open Pomo.Lib.Domain.Components
open Pomo.Lib.Domain.Attributes
open Pomo.Lib.Domain.Services
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Rules

[<Struct>]
type DamageParams = {
  services: EngineServices
  attackerStats: DerivedStats
  defenderStats: DerivedStats
  attackerEffects: HashMap<int<EffectId>, ActiveEffect>
}

module Resolution =
  let calculateHitChance attackerStat defenderStat =
    let attackerValue = float attackerStat
    let defenderValue = float defenderStat

    // Base chance to hit is 50%, adjusted by stats
    let baseHitChance = 0.5

    // If both stats are zero, it's a guaranteed hit.
    if attackerValue = 0.0 && defenderValue = 0.0 then
      1.0
    else
      // The effective stat difference. We use max to avoid negative results which would flip the logic.
      let effectiveAttacker = max 0.0 attackerValue
      let effectiveDefender = max 0.0 defenderValue

      let statAdvantage = effectiveAttacker - effectiveDefender

      // The divisor scales the effect of the stat advantage.
      // A larger divisor means stats have less impact on hit chance.
      let divisor = 100.0

      let chance = baseHitChance + (statAdvantage / divisor)

      // Clamp the result between a minimum and maximum hit chance
      // to ensure there's always a chance to hit or miss.
      max 0.05 (min 0.95 chance)

  let calculateDamage (damageParams: DamageParams) formulaId = adaptive {
    match damageParams.services.formulaStore.tryFind formulaId with
    | ValueSome formula ->

      let formulaResult =
        formula.Calculate {
          InvokerStats = damageParams.attackerStats
          InvokerElementalAttributes =
            damageParams.attackerStats.ElementAttributes
          TargetElementalResistances =
            damageParams.defenderStats.ElementResistances
        }

      // STEP 1: Hit/Miss calculation based on damage type
      let hitRoll = damageParams.services.rng()

      let hitChance =
        match formulaResult.DamageType with
        | DamageType.Neutral -> 1.0
        | DamageType.Physical ->
          calculateHitChance
            damageParams.attackerStats.AC
            damageParams.defenderStats.HV
        | DamageType.Magical ->
          calculateHitChance
            damageParams.attackerStats.LK
            damageParams.defenderStats.LK

      let isHit = hitRoll <= hitChance

      if not isHit then
        return {
          Amount = 0
          IsCritical = false
          IsEvaded = true
        }
      else
        // STEP 2-4: Calculate damage (includes base damage, modifiers, and final damage)
        // Apply critical hit (uses LK)
        let critRoll = damageParams.services.rng()
        let isCritical = critRoll < float damageParams.attackerStats.LK * 0.01

        let damageBonus =
          if isCritical then
            int(
              float(formulaResult.BaseDamage + formulaResult.ElementalDamage)
              * 0.10
            )
          else
            0

        let finalElementalDamage =
          if formulaResult.ElementalDamage > 0 then
            let elementRes =
              damageParams.defenderStats.ElementResistances.TryFindV
                formulaResult.Element
              |> ValueOption.defaultValue 0.0

            float formulaResult.ElementalDamage * (1.0 - elementRes) |> int
          else
            0

        let totalDamage = formulaResult.BaseDamage + finalElementalDamage

        // Apply defense reduction
        let damageAfterDefense =
          match formulaResult.DamageType with
          | DamageType.Physical -> totalDamage - damageParams.defenderStats.DP
          | DamageType.Magical -> totalDamage - damageParams.defenderStats.MD
          | DamageType.Neutral -> totalDamage

        // Apply AbilityDamageMod from active effects
        let abilityDamageMod =
          damageParams.attackerEffects
          |> HashMap.fold
            (fun acc _ effect ->
              effect.Definition.Modifiers
              |> Array.fold
                (fun modAcc modifier ->
                  match modifier with
                  | AbilityDamageMod value -> modAcc + value
                  | _ -> modAcc)
                acc)
            0.0

        let damageWithModifier =
          if abilityDamageMod > 0.0 then
            damageAfterDefense
            + int(float damageAfterDefense * abilityDamageMod)
          else
            damageAfterDefense

        let finalDamage = max 0 (damageWithModifier + damageBonus)

        return {
          Amount = int finalDamage
          IsCritical = isCritical
          IsEvaded = false
        }
    | ValueNone ->
      return {
        Amount = 0
        IsCritical = false
        IsEvaded = false
      }
  }

  let determineNewEffect
    (effectDef: EffectDefinition)
    (existingEffect: ActiveEffect)
    =
    let stacking = effectDef.Stacking

    match stacking with
    | NoStack -> ValueNone
    | RefreshDuration ->
      ValueSome {
        existingEffect with
            RemainingTicks =
              effectDef.Duration.Ticks |> ValueOption.defaultValue TimeSpan.Zero
            NextTickIn =
              effectDef.Duration.Interval
              |> ValueOption.defaultValue TimeSpan.Zero
      }
    | AddStack maxStacks ->
      let newStacks = min maxStacks (existingEffect.Stacks + 1)

      ValueSome {
        existingEffect with
            Stacks = newStacks
            RemainingTicks =
              effectDef.Duration.Ticks |> ValueOption.defaultValue TimeSpan.Zero
            NextTickIn =
              effectDef.Duration.Interval
              |> ValueOption.defaultValue TimeSpan.Zero
      }

  let processEffects
    (effectStore: Services.IEffectStore)
    (abilityDef: ActiveAbilityDefinition)
    (actorId: Guid<EntityId>)
    (currentEffects: HashMap<int<EffectId>, ActiveEffect>)
    =

    let mutable updatedMap = currentEffects

    for effId in abilityDef.Effects do
      let existing = HashMap.tryFindV effId updatedMap

      let newEffect =
        match existing with
        | ValueSome actEff ->
          // Determine stacking update
          match determineNewEffect actEff.Definition actEff with
          | ValueSome newEff -> ValueSome newEff
          | ValueNone -> ValueNone
        | ValueNone ->
          let effect = effectStore.tryFind effId

          match effect with
          | ValueNone -> ValueNone // Effect definition not found, skip
          | ValueSome effectDef ->
            // Create new effect instance
            let newEff: ActiveEffect = {
              EffectId = effId
              SourceId = actorId
              RemainingTicks =
                effectDef.Duration.Ticks
                |> ValueOption.defaultValue TimeSpan.Zero
              NextTickIn =
                effectDef.Duration.Interval
                |> ValueOption.defaultValue TimeSpan.Zero
              Stacks = 1
              Definition = effectDef
            }

            ValueSome newEff

      newEffect
      |> ValueOption.iter(fun ne ->
        updatedMap <- HashMap.add effId ne updatedMap)

    updatedMap

  let applyInstantEffects
    (target: EntityComponents)
    (effectDef: EffectDefinition)
    =
    let mutable newTarget = target

    for modifier in effectDef.Modifiers do
      match modifier with
      | StaticMod(Additive(HP, value)) ->
        let newHp = newTarget.Resources.HP + value

        newTarget <- {
          newTarget with
              Resources = { newTarget.Resources with HP = newHp }
        }
      | StaticMod(Additive(MP, value)) ->
        let newMp = newTarget.Resources.MP + value

        newTarget <- {
          newTarget with
              Resources = { newTarget.Resources with MP = newMp }
        }
      | StaticMod(Subtractive(HP, value)) ->
        let newHp = newTarget.Resources.HP - value

        newTarget <- {
          newTarget with
              Resources = { newTarget.Resources with HP = newHp }
        }
      | StaticMod(Subtractive(MP, value)) ->
        let newMp = newTarget.Resources.MP - value

        newTarget <- {
          newTarget with
              Resources = { newTarget.Resources with MP = newMp }
        }
      | DynamicMod _
      | StaticMod _
      | ResourceConversion _
      | AbilityDamageMod _ -> ()


    newTarget

  let applyAbilityEffects
    (effectStore: IEffectStore)
    (actorId: Guid<EntityId>)
    (abilityDef: ActiveAbilityDefinition)
    (targetComponents: EntityComponents)
    =
    adaptive {
      let currentEffects = targetComponents.Effects

      let effects = processEffects effectStore abilityDef actorId currentEffects

      let mutable targetWithInstantEffects = targetComponents

      abilityDef.Effects
      |> Array.iter(fun effectId ->
        let effectDef = effectStore.find effectId

        if effectDef.Duration = Instant then
          targetWithInstantEffects <-
            applyInstantEffects targetWithInstantEffects effectDef)

      return {
        targetWithInstantEffects with
            Effects = effects
      }
    }

  let checkForDeath (newHp: int) (targetComponents: EntityComponents) =
    if newHp <= 0 && targetComponents.Resources.Status = Status.Alive then
      {
        targetComponents.Resources with
            Status = Dead
            HP = newHp
      }
    else
      {
        targetComponents.Resources with
            HP = newHp
      }

  let applyResourceCost
    (costOpt: ResourceCost voption)
    (actorComponents: EntityComponents)
    (damageAmount: int)
    =
    adaptive {
      // Extract ResourceConversion modifiers from active effects
      let resourceConversions =
        actorComponents.Effects
        |> HashMap.fold
          (fun acc _ effect ->
            effect.Definition.Modifiers
            |> Array.fold
              (fun convAcc modifier ->
                match modifier with
                | ResourceConversion(fromType, toType, ratio) ->
                  (fromType, toType, ratio) :: convAcc
                | _ -> convAcc)
              acc)
          []

      // Apply base cost if present
      let resourcesAfterBaseCost =
        match costOpt with
        | ValueSome cost ->
          match cost.Type with
          | ResourceType.HP -> {
              actorComponents.Resources with
                  HP = actorComponents.Resources.HP - cost.Amount
            }
          | ResourceType.MP ->
              {
                actorComponents.Resources with
                    MP = actorComponents.Resources.MP - cost.Amount
              }
        | ValueNone -> actorComponents.Resources

      // Apply ResourceConversion modifiers
      let finalResources: Attributes.Resources =
        resourceConversions
        |> List.fold
          (fun (resources: Attributes.Resources) (fromType, toType, ratio) ->
            match fromType, toType with
            | ResourceType.HP, ResourceType.HP when ratio < 0.0 ->
              // HP-cost amplification: consume HP based on damage dealt
              let hpCost = int(float damageAmount * abs ratio)

              {
                resources with
                    HP = resources.HP - hpCost
              }
            | ResourceType.MP, ResourceType.HP when ratio > 0.0 ->
              // MP to HP conversion: convert MP to HP
              let mpToConvert = resources.MP
              let hpGained = int(float mpToConvert * ratio)

              {
                resources with
                    MP = 0
                    HP = resources.HP + hpGained
              }
            | ResourceType.HP, ResourceType.MP when ratio > 0.0 ->
              // HP to MP conversion
              let hpToConvert = resources.HP
              let mpGained = int(float hpToConvert * ratio)

              {
                resources with
                    HP = 0
                    MP = resources.MP + mpGained
              }
            | _ -> resources)
          resourcesAfterBaseCost

      return {
        actorComponents with
            Resources = finalResources
      }
    }

  let updateCooldowns
    (actorComponents: EntityComponents)
    abilityId
    gameTime
    cooldown
    =
    {
      actorComponents with
          AbilityCooldowns =
            actorComponents.AbilityCooldowns
            |> HashMap.alterV abilityId (fun _ ->
              ValueSome(gameTime + cooldown))
    }

  let applyDamage (damage: int) (targetComponents: EntityComponents) =
    let newHp = max 0 (targetComponents.Resources.HP - damage)

    let updatedTargetWithDamage = {
      targetComponents with
          EntityComponents.Resources.HP = newHp
    }

    checkForDeath newHp updatedTargetWithDamage
