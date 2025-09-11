namespace Pomo.Lib.Rules

open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives

/// Represents the actions that can be initiated by entities in the game.
type MeleeAttackAction = {
  actor: EntityId
  target: EntityId
  abilityId: Abilities.AbilityId
}

type CastSpellAction = {
  actor: EntityId
  target: EntityId
  abilityId: Abilities.AbilityId
}

type Command =
  | MeleeAttack of MeleeAttackAction
  | CastSpell of CastSpellAction
