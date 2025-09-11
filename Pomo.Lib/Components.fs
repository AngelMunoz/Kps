namespace Pomo.Lib.Domain.Components

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Primitives
open Pomo.Lib.Domain.Abilities

open Pomo.Lib.Domain.Effects

type All = {
  Identity: Classification.Profession
  BaseStats: Attributes.BaseAttributes
  Resources: Attributes.Resources
  Effects: alist<ActiveEffect>
  Abilities: alist<AbilityId> // Abilities this entity possesses
  AbilityCooldowns: amap<AbilityId, Ticks> // Tracks when a cooldown is complete
}
