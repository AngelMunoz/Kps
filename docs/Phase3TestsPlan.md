# Phase 3 Test Plan

This document tracks implementation & pass status for Phase 3 logic tests.
Update this file when a test is added ("Implemented") and when it is green ("Passing").

Legend:

- [ ] Not Implemented
- [~] Implemented (pending green)
- [x] Implemented & Passing

## Scenarios

| ID  | Scenario                                                                                                | Status | Notes                        |
| --- | ------------------------------------------------------------------------------------------------------- | ------ | ---------------------------- |
| T1  | Shield absorption (damage first reduces shield stacks; emits correct events incl. depletion/expiration) | [x]    | Test added in Phase3Tests.fs |
| T2  | Stun prevents all actions (no damage, no resource change, no cooldown applied)                          | [x]    |                              |
| T3  | Silence blocks spell but allows melee                                                                   | [x]    |                              |
| T4  | Taunt redirection forces target to taunter                                                              | [x]    |                              |
| T5  | Effect stacking: NoStack ignores second application                                                     | [x]    |                              |
| T6  | Effect stacking: RefreshDuration resets timer, stack count unchanged                                    | [x]    | Test added in Phase3Tests.fs |
| T7  | Effect stacking: AddStack increments up to cap then stops                                               | [x]    | Test added in Phase3Tests.fs |
| T8  | DoT ticking applies periodic damage and expires after loops                                             | [x]    | Test added in Phase3Tests.fs |
| T9  | HoT ticking applies periodic healing and expires after loops                                            | [x]    | Test added in Phase3Tests.fs |
| T10 | Shield partial depletion across multiple hits (spillover to HP)                                         |        |                              |
| T11 | Deterministic RNG yields identical damage sequence with fixed seed                                      |        |                              |
| T12 | Cooldown-ready abilities set includes ability after cooldown elapses                                    |        |                              |

## Next Steps

1. Inspect effect & ability definitions for IDs and stacking rules.
2. Draft `Phase3Tests.fs` with tests for T1-T3 first (core combat gating).
3. Iterate through remaining scenarios, updating this plan.

## Conventions

- Mirror existing patterns from `Tests.fs` (use `Fact` for scenario tests, `Property` where appropriate for determinism or stacking properties).
- Reuse `TestHelpers.makeEntity` (may copy locally if cross-file visibility requires) or create local helpers.
- Use deterministic RNG via `GameState.create' (fun () -> <fixed-value>)` where variance is involved.
