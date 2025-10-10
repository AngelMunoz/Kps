namespace Pomo.Lib.Tests

open Xunit
open Pomo.Lib.Domain
open Pomo.Lib.FormulaParser

module DerivedStats =
  let Zero: Attributes.DerivedStats = {
    // Power derived stats
    AP = 0
    AC = 0
    DX = 0
    // Magic derived stats
    MP = 0
    MA = 0
    MD = 0
    // Sense derived stats
    WT = 0
    DA = 0
    LK = 0
    // Charm derived stats
    HP = 0
    DP = 0
    HV = 0

    // Element % of attributes and resistances
    ElementAttributes =
      FSharp.Data.Adaptive.HashMap.ofList [
        Attributes.Element.Fire, 0.0
        Attributes.Element.Water, 0.0
        Attributes.Element.Earth, 0.0
        Attributes.Element.Air, 0.0
        Attributes.Element.Lightning, 0.0
        Attributes.Element.Light, 0.0
        Attributes.Element.Dark, 0.0
        Attributes.Element.Neutral, 0.0
      ]
    ElementResistances =
      FSharp.Data.Adaptive.HashMap.ofList [
        Attributes.Element.Fire, 0.0
        Attributes.Element.Water, 0.0
        Attributes.Element.Earth, 0.0
        Attributes.Element.Air, 0.0
        Attributes.Element.Lightning, 0.0
        Attributes.Element.Light, 0.0
        Attributes.Element.Dark, 0.0
        Attributes.Element.Neutral, 0.0
      ]
  }


[<Trait("Category", "Evaluation")>]
module EvaluationTests =

  let ctx = {
    DerivedStats.Zero with
        AP = 50
        AC = 20
        DX = 30
        MA = 10
  }

  [<Theory>]
  [<InlineData(1, "(AP * DX) / 100", 15)>]
  [<InlineData(2, "AP + AC * 2", 90)>]
  [<InlineData(3, "log(AP + 100) * 50", 250.53)>]
  [<InlineData(4, "AP ^ 2 / 100", 25)>]
  [<InlineData(5, "(AP + MA) ^ 1.5 / log(AC + 10)", 136.65)>]
  [<InlineData(6, "2 ^ 3 ^ 2", 512)>] // Right-associative power
  let ``Should evaluate valid formulas correctly``(skillId, formula, expected) =
    let cache = FormulaCache()

    let result = cache.Evaluate(skillId, formula, ctx)
    Assert.Equal(expected, result, 2)

[<Trait("Category", "ErrorHandling")>]
module ErrorHandlingTests =

  let private getErrorDetails(fe: FormulaError) =
    match fe with
    | InvalidToken(_, pos) -> "InvalidToken", pos
    | UnexpectedToken(_, _, pos) -> "UnexpectedToken", pos
    | UnexpectedEndOfInput pos -> "UnexpectedEndOfInput", pos
    | UnmatchedParentheses pos -> "UnmatchedParentheses", pos
    | UnknownVariable _ -> "UnknownVariable", -1 // No position info
    | DivisionByZero -> "DivisionByZero", -1 // No position info

  [<Theory>]
  // Evaluation errors (no position)
  [<InlineData(101, "AP + UNKNOWN", "UnknownVariable", -1)>]
  [<InlineData(104, "100 / (AP)", "DivisionByZero", -1)>]
  // Parsing errors (with position)
  [<InlineData(102, "(AP + 10", "UnexpectedToken", 8)>] // Expects ')' at the end
  [<InlineData(103, "AP + @", "InvalidToken", 5)>] // Invalid char '@'
  [<InlineData(105, "10 + (", "UnexpectedEndOfInput", 6)>] // Unexpected end inside parens
  [<InlineData(106, "1NV4L1D", "UnexpectedToken", 1)>]
  [<InlineData(107, "(AP + 5))", "UnexpectedToken", 8)>] // Extra ')' at the end
  let ``Should throw correct exception for invalid formulas``
    (skillId, formula, expectedErrorName, expectedPosition)
    =
    let cache = FormulaCache()

    let ex =
      Assert.Throws<FormulaException>(fun () ->
        cache.Evaluate(skillId, formula, DerivedStats.Zero) |> ignore)

    let actualErrorName, actualPosition = getErrorDetails ex.Data0
    Assert.Equal(expectedErrorName, actualErrorName)
    Assert.Equal(expectedPosition, actualPosition)
