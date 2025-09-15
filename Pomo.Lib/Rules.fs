namespace Pomo.Lib.Rules

open Pomo.Lib.Domain

/// Represents the actions that can be initiated by entities in the game.
type MeleeAttackAction = {
  actor: int<EntityId>
  target: int<EntityId>
  abilityId: int<AbilityId>
}

type CastSpellAction = {
  actor: int<EntityId>
  target: int<EntityId>
  abilityId: int<AbilityId>
}

type Command =
  | MeleeAttack of MeleeAttackAction
  | CastSpell of CastSpellAction
