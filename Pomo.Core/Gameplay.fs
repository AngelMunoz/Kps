namespace Pomo.Core.Gameplay

open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Primitives
open Pomo.Core.Domain.Components
open Pomo.Core.Rules

type GameState = {
  entities: cmap<EntityId, All>
  gameEvents: clist<GameEvent>
  gameTime: cval<int64>
  rng: cval<System.Random>
}

module GameState =
  let create() = {
    entities = cmap()
    gameEvents = clist []
    gameTime = cval 0L
    rng = cval(System.Random 42)
  }

  let getDerivedStats
    (state: GameState)
    : amap<EntityId, Attributes.DerivedStats> =
    state.entities
    |> AMap.map(fun id c ->
      // This is the base calculation for derived stats.
      // TODO: Expand this to compose stats from other sources:
      // 1. Profession/class modifiers (from c.Identity).
      // 2. Active status effects (buffs/debuffs from c.Effects).
      // 3. Equipped items (from a future Equipment component).
      let attack = c.BaseStats.Strength * 2
      let spell = c.BaseStats.Intellect * 2
      let maxHp = c.BaseStats.Vitality * 10
      let maxMp = c.BaseStats.Willpower * 10

      {
        MaxHP = maxHp
        MaxMP = maxMp
        AttackPower = attack
        SpellPower = spell
        Armor = c.BaseStats.Agility // placeholder
        Evasion = float c.BaseStats.Agility / 100.0 // placeholder
        CritChance = float c.BaseStats.Luck / 100.0 // placeholder
        Resistances = Map.empty // placeholder
      })

  let aAlive(state: GameState) : aset<EntityId> =
    state.entities
    |> AMap.filter(fun _ c -> c.Resources.Status = Attributes.Status.Alive)
    |> AMap.toASet
    |> ASet.map(fun (id, _) -> id)
