This is a previous sketch of some of the core ideas I wanted to implement in a game

This is modeled more after the user's point of view but I think it is a good tell to where I'd like to go.
```fsharp
namespace FeX.Core

module Types =

    [<RequireQualifiedAccess>]
    type Family =
        | Strength
        | Magic
        | Charm
        | Sensory

    [<RequireQualifiedAccess>]
    type Stage =
        | First
        | Second
        | Third

    type Profession = Family * Stage

    [<AutoOpen>]
    type Modifier =
        | Fire
        | Earth
        | Water
        | Air
        | Light
        | Dark
        | Neutral

    type SkillType =
        | Passive
        | Active

    type EffectDuration =
        | Instant
        | Lapse of lapse: float
        | Loop of times: int * looplapse: float

    type Target =
        | NoTarget
        | SingleTarget
        | MultiTarget of targets: int
        | AoE of area: float

    type EffectType =
        | Damage of Amount: Option<float> * Target: Target
        | Heal of Amount: Option<float> * Target: Target
        | Replenish of Amount: Option<float> * Target: Target


    type Effect =
        { EffectType: EffectType
          EffectDuration: EffectDuration
          EffectCooldown: Option<float> }

    type Skill =
        { Name: string
          Profession: Profession
          SkillType: SkillType
          Effect: Effect
          Points: int
          Modifier: Modifier }

    [<RequireQualifiedAccess>]
    type CharacterProperty =
        | Name
        | Profession
        | Skills
        | SkillPoints

    [<RequireQualifiedAccess>]
    type UpdateErrorMsg =
        | AlreadyThirdStage
        | AlreadyFirstStage
        | NotEnoughSkillPoints
        | NotSameFamily
        | SkillIsHigherState
        | SkillNotPresent
        | SkillAlreadyPresent

    type CharacterUpdateError =
        { Message: UpdateErrorMsg
          Property: CharacterProperty }

    [<RequireQualifiedAccess>]
    type SceneType =
        | LoadingScreen
        | Plaza
        | WildArea
        | WaitingRoom
        | BossRoom
        | SelectionRoom

    [<RequireQualifiedAccess>]
    type GameObjectKind =
        | NPC
        | Enemy
        | Player
        | DestructibleObject
        | IndestructibleObject
```

Now, this code is very naive and doesn't actually take into account how a game would actually work.
but you have the idea.
```fsharp
namespace FeX.Core

open Types

type Character =
    { Name: string
      Profession: Profession
      Skills: list<Skill>
      SkillPoints: int }

[<RequireQualifiedAccess>]
module Character =

    let Create (name: string) (profession: Profession) (skills: Option<list<Skill>>): Character =
        { Name = name
          SkillPoints = 0
          Profession = profession
          Skills =
              match skills with
              | Some skills -> skills
              | None -> List.empty<Skill> }

    let UpdateName (character: Character) (name: string) = { character with Name = name }

    let UpdateSkillPoints (character: Character) (skillPoints: int) =
        { character with
              SkillPoints = skillPoints }

    let PromoteProfession (character: Character): Result<Character, CharacterUpdateError> =
        let (family, stage) = character.Profession
        match stage with
        | Stage.First ->
            Ok
                { character with
                      Profession = (family, Stage.Second) }
        | Stage.Second ->
            Ok
                { character with
                      Profession = (family, Stage.Third) }
        | Stage.Third ->
            Error
                { Message = UpdateErrorMsg.AlreadyThirdStage
                  Property = CharacterProperty.Profession }

    let DemoteProfession (character: Character): Result<Character, CharacterUpdateError> =
        let (family, stage) = character.Profession
        match stage with
        | Stage.Third ->
            Ok
                { character with
                      Profession = (family, Stage.Second) }
        | Stage.Second ->
            Ok
                { character with
                      Profession = (family, Stage.First) }
        | Stage.First ->
            Error
                { Message = UpdateErrorMsg.AlreadyFirstStage
                  Property = CharacterProperty.Profession }

    let AddSkill (character: Character) (skill: Skill): Result<Character, CharacterUpdateError> =
        if skill.Points > character.SkillPoints then
            Error
                { Message = UpdateErrorMsg.NotEnoughSkillPoints
                  Property = CharacterProperty.SkillPoints }
        else
            match character.Skills
                  |> List.tryFind (fun s -> s = skill) with
            | Some _ ->
                Error
                    { Message = UpdateErrorMsg.SkillAlreadyPresent
                      Property = CharacterProperty.Skills }
            | None ->
                let (family, stage) = character.Profession
                let (skillFamily, skillStage) = skill.Profession

                let skillMatchesStage =
                    match stage, skillStage with
                    | Stage.Third, _ -> true
                    | Stage.Second, Stage.Second
                    | Stage.Second, Stage.First -> true
                    | Stage.First, Stage.First -> true
                    | _ -> false

                match skillFamily = family, skillMatchesStage with
                | false, _ ->
                    Error
                        { Message = UpdateErrorMsg.NotSameFamily
                          Property = CharacterProperty.Profession }
                | _, false ->
                    Error
                        { Message = UpdateErrorMsg.SkillIsHigherState
                          Property = CharacterProperty.Profession }
                | true, true ->
                    Ok
                        { character with
                              SkillPoints = character.SkillPoints - skill.Points
                              Skills = skill :: character.Skills }

    let RemoveSkill (character: Character) (skill: Skill): Result<Character, CharacterUpdateError> =
        match character.Skills
              |> List.tryFind (fun s -> s = skill) with
        | Some skill ->
            Ok
                { character with
                      SkillPoints = character.SkillPoints + skill.Points
                      Skills =
                          character.Skills
                          |> List.filter (fun s -> s <> skill) }
        | None ->
            Error
                { Message = UpdateErrorMsg.SkillNotPresent
                  Property = CharacterProperty.Skills }
```
