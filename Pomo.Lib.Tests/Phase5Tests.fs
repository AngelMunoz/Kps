module Pomo.Lib.Tests.Phase5Tests

open Xunit
open FSharp.Data.Adaptive
open Pomo.Lib.Domain
open Pomo.Lib.Domain.Classification
open Pomo.Lib.Domain.CharacterKits
open Pomo.Lib.Content.CharacterKitStore

[<Fact>]
let ``CharacterKitStore should contain 12 character base kits``() =
  let kitCount = definitions |> HashMap.count
  Assert.Equal(12, kitCount)

[<Fact>]
let ``All 12 profession combinations should be present``() =
  let families = [ Power; Magic; Sense; Charm ]
  let stages = [ First; Second; Third ]

  for family in families do
    for stage in stages do
      let profession = { Family = family; Stage = stage }
      let kitExists = definitions |> HashMap.containsKey profession
      Assert.True(kitExists, $"Missing kit for {family} {stage}")

[<Theory>]
[<InlineData("Power", "First")>]
[<InlineData("Power", "Second")>]
[<InlineData("Power", "Third")>]
[<InlineData("Magic", "First")>]
[<InlineData("Magic", "Second")>]
[<InlineData("Magic", "Third")>]
[<InlineData("Sense", "First")>]
[<InlineData("Sense", "Second")>]
[<InlineData("Sense", "Third")>]
[<InlineData("Charm", "First")>]
[<InlineData("Charm", "Second")>]
[<InlineData("Charm", "Third")>]
let ``Each character kit should have valid base stats``
  (familyStr: string)
  (stageStr: string)
  =
  let family =
    match familyStr with
    | "Power" -> Power
    | "Magic" -> Magic
    | "Sense" -> Sense
    | "Charm" -> Charm
    | _ -> failwith "Invalid family"

  let stage =
    match stageStr with
    | "First" -> First
    | "Second" -> Second
    | "Third" -> Third
    | _ -> failwith "Invalid stage"

  let profession = { Family = family; Stage = stage }
  let kit = definitions |> HashMap.find profession

  Assert.True(kit.BaseStats.Power > 0, "Power should be positive")
  Assert.True(kit.BaseStats.Magic > 0, "Magic should be positive")
  Assert.True(kit.BaseStats.Sense > 0, "Sense should be positive")
  Assert.True(kit.BaseStats.Charm > 0, "Charm should be positive")

[<Theory>]
[<InlineData("Power", "First", 15)>]
[<InlineData("Power", "Second", 22)>]
[<InlineData("Power", "Third", 30)>]
[<InlineData("Magic", "First", 15)>]
[<InlineData("Magic", "Second", 22)>]
[<InlineData("Magic", "Third", 30)>]
[<InlineData("Sense", "First", 15)>]
[<InlineData("Sense", "Second", 22)>]
[<InlineData("Sense", "Third", 30)>]
[<InlineData("Charm", "First", 15)>]
[<InlineData("Charm", "Second", 22)>]
[<InlineData("Charm", "Third", 30)>]
let ``Primary stat should match expected value for stage``
  (familyStr: string)
  (stageStr: string)
  (expectedPrimaryStat: int)
  =
  let family =
    match familyStr with
    | "Power" -> Power
    | "Magic" -> Magic
    | "Sense" -> Sense
    | "Charm" -> Charm
    | _ -> failwith "Invalid family"

  let stage =
    match stageStr with
    | "First" -> First
    | "Second" -> Second
    | "Third" -> Third
    | _ -> failwith "Invalid stage"

  let profession = { Family = family; Stage = stage }
  let kit = definitions |> HashMap.find profession

  let primaryStat =
    match family with
    | Power -> kit.BaseStats.Power
    | Magic -> kit.BaseStats.Magic
    | Sense -> kit.BaseStats.Sense
    | Charm -> kit.BaseStats.Charm

  Assert.Equal(expectedPrimaryStat, primaryStat)

[<Fact>]
let ``Each character kit should have at least one starter ability``() =
  for _, kit in definitions do

    Assert.True(
      kit.StarterAbilities.Count > 0,
      $"Kit {kit.Name} has no starter abilities"
    )

[<Fact>]
let ``Power family kits should have name containing warrior or fighter or battle``
  ()
  =
  let powerFirst = definitions |> HashMap.find { Family = Power; Stage = First }

  let powerSecond =
    definitions |> HashMap.find { Family = Power; Stage = Second }

  let powerThird = definitions |> HashMap.find { Family = Power; Stage = Third }

  Assert.Contains("Warrior", powerFirst.Name)
  Assert.Contains("Fighter", powerSecond.Name)
  Assert.Contains("Battle", powerThird.Name)

[<Fact>]
let ``Magic family kits should have name containing mage or sorcerer or arch``
  ()
  =
  let magicFirst = definitions |> HashMap.find { Family = Magic; Stage = First }

  let magicSecond =
    definitions |> HashMap.find { Family = Magic; Stage = Second }

  let magicThird = definitions |> HashMap.find { Family = Magic; Stage = Third }

  Assert.Contains("Mage", magicFirst.Name)
  Assert.Contains("Sorcerer", magicSecond.Name)
  Assert.Contains("Archmage", magicThird.Name)

[<Fact>]
let ``Sense family kits should have name containing scout or ranger``() =
  let senseFirst = definitions |> HashMap.find { Family = Sense; Stage = First }

  let senseSecond =
    definitions |> HashMap.find { Family = Sense; Stage = Second }

  let senseThird = definitions |> HashMap.find { Family = Sense; Stage = Third }

  Assert.Contains("Scout", senseFirst.Name)
  Assert.Contains("Ranger", senseSecond.Name)
  Assert.Contains("Scout", senseThird.Name)

[<Fact>]
let ``Charm family kits should have name containing defender or guardian or protector``
  ()
  =
  let charmFirst = definitions |> HashMap.find { Family = Charm; Stage = First }

  let charmSecond =
    definitions |> HashMap.find { Family = Charm; Stage = Second }

  let charmThird = definitions |> HashMap.find { Family = Charm; Stage = Third }

  Assert.Contains("Defender", charmFirst.Name)
  Assert.Contains("Guardian", charmSecond.Name)
  Assert.Contains("Protector", charmThird.Name)
