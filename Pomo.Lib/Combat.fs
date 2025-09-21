namespace Pomo.Lib.Rules

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Attributes

module Combat =
  [<Struct>]
  type DamageResult = {
    Amount: int
    IsCritical: bool
    IsEvaded: bool
  }

  let calculatePhysicalDamage
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (rng: unit -> float)
    =
    // 1. Calculate base damage (AP vs DP)
    let baseDamage = max 0 (attackerStats.AP - defenderStats.DP)

    // 2. Check for critical hit (uses LK)
    let critRoll = rng()
    let isCritical = critRoll < (float attackerStats.LK * 0.01) // LK as percentage
    let damageMultiplier = if isCritical then 2.0 else 1.0

    // 3. Add variance
    let variance = 1.0 + (rng() * 0.2 - 0.1) // +/- 10% variance
    let finalDamage = float baseDamage * damageMultiplier * variance

    {
      Amount = int finalDamage
      IsCritical = isCritical
      IsEvaded = false
    }

  let calculateMagicalDamage
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (rng: unit -> float)
    =
    // 1. Calculate base damage (MA vs MD)
    let baseDamage = max 0 (attackerStats.MA - defenderStats.MD)

    // 2. Check for critical hit (uses LK)
    let critRoll = rng()
    let isCritical = critRoll < (float attackerStats.LK * 0.01) // LK as percentage
    let damageMultiplier = if isCritical then 2.0 else 1.0

    // 3. Add variance
    let variance = 1.0 + (rng() * 0.2 - 0.1) // +/- 10% variance
    let finalDamage = float baseDamage * damageMultiplier * variance

    {
      Amount = int finalDamage
      IsCritical = isCritical
      IsEvaded = false
    }

  let calculateElementalModifier
    (element: Element)
    (baseDamage: int)
    (defenderStats: DerivedStats)
    =
    let resistance =
      FSharp.Data.Adaptive.HashMap.tryFind
        element
        defenderStats.Resistances
      |> Option.toValueOption
      |> ValueOption.defaultValue 0.0

    let damageReduction = 1.0 - resistance
    int (float baseDamage * damageReduction)

  // 4-step damage calculation per game definitions
  let calculateDamage
    (damageType: Abilities.DamageType)
    (attackerStats: DerivedStats)
    (defenderStats: DerivedStats)
    (rng: unit -> float)
    =
    match damageType with
    | Abilities.DamageType.Physical ->
      calculatePhysicalDamage attackerStats defenderStats rng
    | Abilities.DamageType.Magical ->
      calculateMagicalDamage attackerStats defenderStats rng
    | Abilities.DamageType.Elemental element ->
      // Elemental damage: base magical damage + elemental resistance modifier
      let baseMagical = calculateMagicalDamage attackerStats defenderStats rng
      let modifiedAmount = calculateElementalModifier element baseMagical.Amount defenderStats
      
      {
        Amount = modifiedAmount
        IsCritical = baseMagical.IsCritical
        IsEvaded = baseMagical.IsEvaded
      }
