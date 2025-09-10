namespace Pomo.Core.Rules

open Pomo.Core.Domain.Primitives

/// Represents the actions that can be initiated by entities in the game.
type MeleeAttackAction = { actor: EntityId; target: EntityId }

type CastSpellAction = {
  actor: EntityId
  target: EntityId
  spellId: int
}

type Command =
  | MeleeAttack of MeleeAttackAction
  | CastSpell of CastSpellAction // simple spell placeholder for Phase 2 vertical slice
// | UseItem of { actor: EntityId; itemId: int; target: EntityId }
// | Defend of { actor: EntityId }
// | Wait of { actor: EntityId }

/// Represents the outcomes of actions, which are recorded to track game state changes.
type DamageAppliedEvent = { target: EntityId; amount: int }
type HealedEvent = { target: EntityId; amount: int }

type ResourceChangedEvent = {
  target: EntityId (* TODO: Stronger type *)
  resource: string
  newValue: int
}

type EffectAppliedEvent = {
  target: EntityId (* TODO: Stronger type *)
  effectId: int
}

type EffectExpiredEvent = {
  target: EntityId (* TODO: Stronger type *)
  effectId: int
}

type EntityDiedEvent = { entityId: EntityId }

type GameEvent =
  | DamageApplied of DamageAppliedEvent
  | Healed of HealedEvent
  | ResourceChanged of ResourceChangedEvent
  | EffectApplied of EffectAppliedEvent
  | EffectExpired of EffectExpiredEvent
  | EntityDied of EntityDiedEvent
