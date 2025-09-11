namespace Pomo.Core.Domain.Components

open FSharp.Data.Adaptive
open Pomo.Core.Domain
open Pomo.Core.Domain.Classification
open Pomo.Core.Domain.Attributes
open Pomo.Core.Domain.Primitives
open Pomo.Core.Domain.Abilities

type ActiveEffect = { EffectId: int; Duration: Ticks } // Placeholder

type All = {
  Identity: Classification.Profession
  BaseStats: Attributes.BaseAttributes
  Resources: Attributes.Resources
  Effects: alist<ActiveEffect>
  Abilities: alist<AbilityId> // Abilities this entity possesses
  AbilityCooldowns: amap<AbilityId, Ticks> // Tracks when a cooldown is complete
}
