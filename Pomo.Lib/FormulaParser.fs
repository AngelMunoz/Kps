namespace Pomo.Lib.FormulaParser
#nowarn "3391"

open System
open System.Collections.Concurrent
open Pomo.Lib.Domain

[<Struct>]
type VarId =
  | AP
  | AC
  | DX
  | MP
  | MA
  | MD
  | WT
  | DA
  | LK
  | HP
  | DP
  | HV
  | Fire
  | FireRes
  | Water
  | WaterRes
  | Earth
  | EarthRes
  | Air
  | AirRes
  | Lightning
  | LightningRes
  | Light
  | LightRes
  | Dark
  | DarkRes
  | Unknown of string

/// Abstract Syntax Tree for math expressions
type MathExpr =
  | Const of _const: float
  | Var of VarId
  | Add of addLeft: MathExpr * addRight: MathExpr
  | Sub of subLeft: MathExpr * subRight: MathExpr
  | Mul of mulLeft: MathExpr * mulRight: MathExpr
  | Div of divLeft: MathExpr * divRight: MathExpr
  | Pow of powLeft: MathExpr * powRight: MathExpr
  | Log of logExpr: MathExpr
  | Log10 of log10Expr: MathExpr

// Error Handling
type FormulaError =
  | InvalidToken of string * int
  | UnexpectedToken of expected: string * found: string * int
  | UnexpectedEndOfInput of int
  | UnknownVariable of string // Evaluation error, no position info
  | DivisionByZero // Evaluation error, no position info
  | UnmatchedParentheses of int

exception FormulaException of FormulaError

module FormulaParser =

  // Helper for comparing a ReadOnlySpan<char> with a string without allocation
  let inline spanEquals (span: ReadOnlySpan<char>) (s: ReadOnlySpan<char>) =
    span.Equals(s, StringComparison.OrdinalIgnoreCase)

  let inline classifyVar(token: ReadOnlySpan<char>) : VarId =
    // Match by length first to reduce comparisons
    match token.Length with
    | 2 ->
      if spanEquals token "AP" then AP
      elif spanEquals token "AC" then AC
      elif spanEquals token "DX" then DX
      elif spanEquals token "MP" then MP
      elif spanEquals token "MA" then MA
      elif spanEquals token "MD" then MD
      elif spanEquals token "WT" then WT
      elif spanEquals token "DA" then DA
      elif spanEquals token "LK" then LK
      elif spanEquals token "HP" then HP
      elif spanEquals token "DP" then DP
      elif spanEquals token "HV" then HV
      else Unknown(token.ToString())
    | 5 ->
      if spanEquals token "FireA" then Fire
      elif spanEquals token "FireR" then FireRes
      elif spanEquals token "WaterA" then Water
      elif spanEquals token "WaterR" then WaterRes
      elif spanEquals token "EarthA" then Earth
      elif spanEquals token "EarthR" then EarthRes
      elif spanEquals token "LightA" then Light
      elif spanEquals token "LightR" then LightRes
      elif spanEquals token "DarkA" then Dark
      elif spanEquals token "DarkR" then DarkRes
      else Unknown(token.ToString())
    | 4 ->
      if spanEquals token "AirA" then Air
      elif spanEquals token "AirR" then AirRes
      else Unknown(token.ToString())
    | 10 ->
      if spanEquals token "LightningA" then Lightning
      elif spanEquals token "LightningR" then LightningRes
      else Unknown(token.ToString())
    | _ -> Unknown(token.ToString())

  // Internal module to handle token-level operations on the input span
  module internal SpanToken =
    let inline isOperator(c: char) =
      c = '(' || c = ')' || c = '+' || c = '-' || c = '*' || c = '/' || c = '^'

    let inline skipWhitespace (span: ReadOnlySpan<char>) (i: ref<int>) =
      while i.Value < span.Length && Char.IsWhiteSpace(span[i.Value]) do
        i.Value <- i.Value + 1

    let inline peek
      (i: ref<int>)
      (span: ReadOnlySpan<char>)
      : ReadOnlySpan<char> =
      skipWhitespace span i

      if i.Value >= span.Length then
        ReadOnlySpan.Empty
      else
        let c = span[i.Value]
        let startIndex = i.Value
        let mutable endIndex = i.Value + 1

        if isOperator c then
          () // Operator is a single char
        elif Char.IsLetter(c) then // Variable or function
          while endIndex < span.Length && Char.IsLetterOrDigit(span[endIndex]) do
            endIndex <- endIndex + 1
        elif Char.IsDigit(c) || c = '.' then // Number
          while endIndex < span.Length
                && (Char.IsDigit(span[endIndex]) || span[endIndex] = '.') do
            endIndex <- endIndex + 1
        else
          raise(FormulaException(InvalidToken(string c, i.Value)))

        span.Slice(startIndex, endIndex - startIndex)

    let next (i: ref<int>) (span: ReadOnlySpan<char>) : ReadOnlySpan<char> =
      let tokenSlice = peek i span

      if not tokenSlice.IsEmpty then
        i.Value <- i.Value + tokenSlice.Length

      tokenSlice

    let consume (i: ref<int>) (span: ReadOnlySpan<char>) : unit =
      let tokenSlice = peek i span

      if not tokenSlice.IsEmpty then
        i.Value <- i.Value + tokenSlice.Length

  let rec parseExpr (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
    let mutable left = parseTerm i span
    let mutable looping = true

    while looping do
      let nextToken = SpanToken.peek i span

      if spanEquals nextToken "+" then
        SpanToken.consume i span
        let right = parseTerm i span

        left <- Add(left, right)
      elif spanEquals nextToken "-" then
        SpanToken.consume i span
        let right = parseTerm i span

        left <- Sub(left, right)
      else
        looping <- false

    left

  and parseTerm (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
    let mutable left = parsePower i span
    let mutable looping = true

    while looping do
      let nextToken = SpanToken.peek i span

      if spanEquals nextToken "*" then
        SpanToken.consume i span
        let right = parsePower i span

        left <- Mul(left, right)
      elif spanEquals nextToken "/" then
        SpanToken.consume i span
        let right = parsePower i span

        left <- Div(left, right)
      else
        looping <- false

    left

  and parsePower (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
    let left = parseFactor i span
    let nextToken = SpanToken.peek i span

    if spanEquals nextToken "^" then
      SpanToken.consume i span

      let right = parsePower i span // Right-associative

      Pow(left, right)
    else
      left

  and parseFactor (i: ref<int>) (span: ReadOnlySpan<char>) : MathExpr =
    let startPos = i.Value
    let token = SpanToken.next i span

    if token.IsEmpty then
      raise(FormulaException(UnexpectedEndOfInput startPos))

    let firstChar = token[0]

    if Char.IsDigit firstChar || firstChar = '.' then
      match Double.TryParse(token) with
      | true, value -> Const value
      | false, _ ->
        raise(FormulaException(InvalidToken(token.ToString(), startPos)))
    elif Char.IsLetter firstChar then
      if spanEquals token "log" then
        parseFunction Log i span
      elif spanEquals token "log10" then
        parseFunction Log10 i span
      else
        Var(classifyVar token)
    elif spanEquals token "(" then
      let expr = parseExpr i span
      let closingParenPos = i.Value
      let closingToken = SpanToken.next i span

      if spanEquals closingToken ")" then
        expr
      else
        raise(
          FormulaException(
            UnexpectedToken(")", closingToken.ToString(), closingParenPos)
          )
        )
    else
      raise(FormulaException(InvalidToken(token.ToString(), startPos)))

  and parseFunction
    nodeConstructor
    (i: ref<int>)
    (span: ReadOnlySpan<char>)
    : MathExpr =
    let startPos = i.Value
    let openParen = SpanToken.next i span

    if not(spanEquals openParen "(") then
      raise(
        FormulaException(UnexpectedToken("(", openParen.ToString(), startPos))
      )

    let expr = parseExpr i span

    let closeParen = SpanToken.next i span

    if not(spanEquals closeParen ")") then
      raise(
        FormulaException(UnexpectedToken(")", closeParen.ToString(), startPos))
      )

    nodeConstructor expr

  let parse(formula: string) : MathExpr =
    let span = formula.AsSpan()
    let i = ref 0
    let expr = parseExpr i span
    SpanToken.skipWhitespace span i

    if i.Value < span.Length then
      raise(
        FormulaException(
          UnexpectedToken(
            "end of input",
            span.Slice(i.Value).ToString(),
            i.Value
          )
        )
      )

    expr

module internal FormulaOptimize =
  let inline same (a: obj) (b: obj) = obj.ReferenceEquals(a, b)

  let rec fold(expr: MathExpr) : MathExpr =
    match expr with
    | Const _ -> expr
    | Var _ -> expr
    | Add(Const a, Const b) -> Const(a + b)
    | Sub(Const a, Const b) -> Const(a - b)
    | Mul(Const a, Const b) -> Const(a * b)
    | Div(Const a, Const b) when b <> 0.0 -> Const(a / b)
    | Pow(Const a, Const b) -> Const(Math.Pow(a, b))
    | Log(Const v) -> Const(Math.Log v)
    | Log10(Const v) -> Const(Math.Log10 v)
    | Add(a, b) ->
      let a' = fold a
      let b' = fold b

      if same a a' && same b b' then
        expr
      else
        match a', b' with
        | Const x, Const y -> Const(x + y)
        | _ -> Add(a', b')
    | Sub(a, b) ->
      let a' = fold a
      let b' = fold b

      if same a a' && same b b' then
        expr
      else
        match a', b' with
        | Const x, Const y -> Const(x - y)
        | _ -> Sub(a', b')
    | Mul(a, b) ->
      let a' = fold a
      let b' = fold b

      if same a a' && same b b' then
        expr
      else
        match a', b' with
        | Const 0.0, _ -> Const 0.0
        | _, Const 0.0 -> Const 0.0
        | Const 1.0, _ -> b'
        | _, Const 1.0 -> a'
        | Const x, Const y -> Const(x * y)
        | _ -> Mul(a', b')
    | Div(a, b) ->
      let a' = fold a
      let b' = fold b

      if same a a' && same b b' then
        expr
      else
        match a', b' with
        | _, Const 1.0 -> a'
        | Const x, Const y when y <> 0.0 -> Const(x / y)
        | _ -> Div(a', b')
    | Pow(a, b) ->
      let a' = fold a
      let b' = fold b

      if same a a' && same b b' then
        expr
      else
        match a', b' with
        | _, Const 0.0 -> Const 1.0
        | x, Const 1.0 -> x
        | Const x, Const y -> Const(Math.Pow(x, y))
        | _ -> Pow(a', b')
    | Log e ->
      let e' = fold e

      if same e e' then
        expr
      else
        match e' with
        | Const v -> Const(Math.Log v)
        | _ -> Log e'
    | Log10 e ->
      let e' = fold e

      if same e e' then
        expr
      else
        match e' with
        | Const v -> Const(Math.Log10 v)
        | _ -> Log10 e'

module FormulaEvaluator =
  let inline findElement element attributes =
    FSharp.Data.Adaptive.HashMap.tryFindV element attributes
    |> ValueOption.defaultValue 0.0

  let rec eval (context: Attributes.DerivedStats) (expr: MathExpr) : float =
    match expr with
    | Const v -> v
    | Var AP -> context.AP
    | Var AC -> context.AC
    | Var DX -> context.DX
    | Var MP -> context.MP
    | Var MA -> context.MA
    | Var MD -> context.MD
    | Var WT -> context.WT
    | Var DA -> context.DA
    | Var LK -> context.LK
    | Var HP -> context.HP
    | Var DP -> context.DP
    | Var HV -> context.HV
    | Var Fire -> findElement Attributes.Element.Fire context.ElementAttributes
    | Var FireRes ->
      findElement Attributes.Element.Fire context.ElementResistances
    | Var Water ->
      findElement Attributes.Element.Water context.ElementAttributes
    | Var WaterRes ->
      findElement Attributes.Element.Water context.ElementResistances
    | Var Earth ->
      findElement Attributes.Element.Earth context.ElementAttributes
    | Var EarthRes ->
      findElement Attributes.Element.Earth context.ElementResistances
    | Var Air -> findElement Attributes.Element.Air context.ElementAttributes
    | Var AirRes ->
      findElement Attributes.Element.Air context.ElementResistances
    | Var Lightning ->
      findElement Attributes.Element.Lightning context.ElementAttributes
    | Var LightningRes ->
      findElement Attributes.Element.Lightning context.ElementResistances
    | Var Light ->
      findElement Attributes.Element.Light context.ElementAttributes
    | Var LightRes ->
      findElement Attributes.Element.Light context.ElementResistances
    | Var Dark -> findElement Attributes.Element.Dark context.ElementAttributes
    | Var DarkRes ->
      findElement Attributes.Element.Dark context.ElementResistances
    | Var(Unknown name) -> raise(FormulaException(UnknownVariable name))
    | Add(a, b) -> eval context a + eval context b
    | Sub(a, b) -> eval context a - eval context b
    | Mul(a, b) -> eval context a * eval context b
    | Div(a, b) ->
      let valB = eval context b

      if valB = 0.0 then
        raise(FormulaException DivisionByZero)

      eval context a / valB
    | Pow(a, b) -> Math.Pow(eval context a, eval context b)
    | Log e -> Math.Log(eval context e)
    | Log10 e -> Math.Log10(eval context e)

[<Sealed>]
type FormulaCache() =
  let cache = ConcurrentDictionary<int, MathExpr>()

  member _.GetOrParse(skillId: int, formulaText: string) : MathExpr =
    match cache.TryGetValue skillId with
    | true, expr -> expr
    | false, _ ->
      let expr = FormulaParser.parse formulaText |> FormulaOptimize.fold
      cache.TryAdd(skillId, expr) |> ignore
      expr

  member this.Evaluate
    (skillId: int, formulaText: string, context: Attributes.DerivedStats)
    : float =
    let expr = this.GetOrParse(skillId, formulaText)
    let result = FormulaEvaluator.eval context expr
    Math.Round(result, 2)
