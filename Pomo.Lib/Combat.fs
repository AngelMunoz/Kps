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
    // 1. Check for evasion
    let evasionRoll = rng()

    if evasionRoll < defenderStats.Hevasion then
      {
        Amount = 0
        IsCritical = false
        IsEvaded = true
      }
    else // 2. Calculate base damage
      let baseDamage =
        max 0 (attackerStats.AttackPower - defenderStats.DefensePotential)

      // 3. Check for critical hit
      let critRoll = rng()
      let isCritical = critRoll < attackerStats.Accuracy
      let damageMultiplier = if isCritical then 2.0 else 1.0

      // 4. Add variance
      let variance = 1.0 + (rng() * 0.2 - 0.1) // +/- 10% variance
      let finalDamage = float baseDamage * damageMultiplier * variance

      {
        Amount = int finalDamage
        IsCritical = isCritical
        IsEvaded = false
      }

  let calculateMagicalDamage
    (spellElement: Element)
    (spellPower: int)
    (defenderStats: DerivedStats)
    (rng: unit -> float)
    =
    // 1. Get resistance for the element
    let resistance =
      FSharp.Data.Adaptive.HashMap.tryFind
        spellElement
        defenderStats.Resistances
      |> Option.toValueOption
      |> ValueOption.defaultValue 0.0

    // 2. Calculate damage reduction from resistance
    let damageReduction = 1.0 - resistance

    // 3. Calculate base damage
    let baseDamage = float spellPower * damageReduction

    // 4. Add variance
    let variance = 1.0 + (rng() * 0.2 - 0.1) // +/- 10% variance
    let finalDamage = baseDamage * variance

    {
      Amount = int(max 0.0 finalDamage)
      IsCritical = false
      IsEvaded = false
    } // Magical attacks can't be evaded/crit for now
