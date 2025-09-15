namespace Pomo.Lib.Domain.Components

open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Abilities
open Pomo.Lib.Domain.Effects

type All = {
  Identity: Classification.Profession
  BaseStats: Attributes.BaseAttributes
  Resources: Attributes.Resources
  Effects: alist<ActiveEffect>
  Abilities: alist<int<AbilityId>> // Abilities this entity possesses
  AbilityCooldowns: amap<int<AbilityId>, int64<Tick>> // Tracks when a cooldown is complete
}
