namespace Pomo.Core.Domain

[<Measure>] type ticks

module Primitives =
    type EntityId = EntityId of int
    type Ticks = int64<ticks>

module Classification =
    type Faction =
        | Player
        | Enemy
        | Neutral

    type Tag =
        | Biological
        | Artificial
        | Undead

    type Family =
        | Strength
        | Magic
        | Charm
        | Sensory

    type Stage =
        | First
        | Second
        | Third

    type Profession = {
        Family: Family
        Stage: Stage
    }

module Attributes =
    type Element =
        | Fire
        | Earth
        | Water
        | Air
        | Light
        | Dark
        | Neutral

    type BaseAttributes = {
        Strength: int
        Agility: int
        Intellect: int
        Vitality: int
        Willpower: int
        Luck: int
    }

    type Resistances = Map<Element, float>

    type DerivedStats = {
        MaxHP: int
        MaxMP: int
        AttackPower: int
        SpellPower: int
        Armor: int
        Evasion: float
        CritChance: float
        Resistances: Resistances
    }

    type Status =
        | Alive
        | Dead
        | Disabled

    type Resources = {
        HP: int
        MP: int
        Stamina: int
        Status: Status
    }

module Inventory =
    type Slot =
        | Head
        | Chest
        | Legs
        | Hands
        | Weapon1
        | Weapon2
        | Accessory
