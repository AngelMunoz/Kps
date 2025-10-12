# F# and MonoGame Project Structure

This is a cross-platform game built with F# and MonoGame. The project is structured to share core logic across multiple platforms (Windows, DesktopGL, Android, iOS).

## Key Projects

- **`Pomo.Lib`**: An F# library containing the core game mechanics, domain models, and business logic. This is where you'll find modules for `Combat.fs`, `Gameplay.fs`, and `Domain.fs`, which define the rules and data structures of the game. This library is engine-agnostic and can integrate with different game engines.
- **`Pomo.Core`**: An F# project that integrates Pomo.Lib with MonoGame. It handles rendering, input processing, scene management, and the main game loop (`PomoGame.fs`). It references `Pomo.Lib` and is, in turn, referenced by the platform-specific projects.
- **`Pomo.DesktopGL`, `Pomo.WindowsDX`, `Pomo.Android`, `Pomo.iOS`**: These are the platform-specific "head" projects. They contain the entry points (`Program.fs` or `MainActivity.fs`) to launch the game on their respective targets.
- **`Pomo.Lib.Tests`**: Unit tests for the `Pomo.Lib` project, written using `xUnit` and `FsCheck`.

## Architecture

The architecture is based on a layered approach with clear separation of concerns:

- **Game Logic Layer (`Pomo.Lib`)**: Contains all game mechanics, domain models, and business logic. This library is responsible for managing the game state, character interactions, combat resolution, scenarios, and other core mechanics. It is engine-agnostic and uses FSharp.Data.Adaptive for reactive state management.
- **Presentation Layer (`Pomo.Core`)**: Integrates Pomo.Lib with MonoGame. Handles rendering entities and UI, processing player input, the main game loop, and scene management. All presentation concerns (graphics, sound, input) are contained here.
- **Platform Layer**: Platform-specific projects (`Pomo.DesktopGL`, `Pomo.WindowsDX`, `Pomo.Android`, `Pomo.iOS`) provide entry points and platform-specific bootstrapping.
- **Domain Model**: The core data structures are defined in `Pomo.Lib/Domain.fs`. These are immutable F# records and discriminated unions, which are used throughout both Pomo.Lib and Pomo.Core.

## Developer Workflow

- **Building the project**: To build the entire solution, you can use the `dotnet build` command at the root of the repository.
- **Running the game**: You can run the game on a specific platform by running the corresponding project. For example, to run the desktop version, you can execute:
  ```shell
  dotnet run --project Pomo.DesktopGL
  ```
- **Running tests**: The tests are located in the `Pomo.Lib.Tests` project. You can run them using the `dotnet run --project Pomo.Lib.Tests` command.

## Architectural and Coding Principles

### 🤖 AI AGENT NOTICE: PROGRAMMING PARADIGM HIERARCHY 🤖

- Do not add extra comments to the code.
- Follow the coding style and conventions used in the existing codebase.

**MANDATORY PARADIGM ORDER - STRICTLY ENFORCE:**

1. **PRIMARY: Data Oriented Programming (DOP)** - Default programming style

   - Data structures are first-class citizens
   - Immutable data with pure transformations
   - Separation of data and logic
   - Functions operate on data, not encapsulated within objects

2. **SECONDARY: Interface-based Abstraction** - For service boundaries only

   - Use interfaces for `EngineServices` and external dependencies
   - Abstractions must be minimal and focused

3. **TERTIARY: Imperative Programming** - Limited, controlled usage

   - Only for performance-critical sections
   - Must be clearly documented and justified

4. **QUATERNARY: Mutable Imperative** - Exceptional cases only
   - Must be self-contained within single functions
   - Requires explicit documentation of mutation scope
   - Never expose mutable state outside function boundaries

**DOMAIN TYPE MODIFICATIONS:**

- **CRITICAL**: `docs/game-definitions.md` is VITAL when modifying domain types
- Any deviation from game-definitions.md requires explicit user confirmation
- If user accepts deviation, `docs/game-definitions.md` MUST be updated with the changes

This project uses a reactive architecture with FSharp.Data.Adaptive (FDA) for its core logic, implementing Data Oriented Programming principles. Adhere to the following principles derived from the project's design documents.

### Data Oriented Programming with FSharp.Data.Adaptive (DOP + FDA)

**Core DOP Principles:**

- **Data as Primary Organizing Principle**: All game logic organized around immutable data structures
- **Pure Data Transformations**: Functions transform data without side effects
- **Immutable Data Structures**: FDA collections (`cmap`, `amap`, `cset`, `aset`, `clist`, `alist`) enforce immutability
- **Reactive Data Flow**: FDA extends DOP with automatic incremental computation

**FDA Implementation:**

- **Authoritative State**: The game's core state is maintained in a central, immutable `GameState` record containing adaptive values (`cval`, `cmap`, `clist`)
- **Derived Data**: All other game views and stats should be derived from this authoritative state using adaptive computations. For example, a set of "alive" entities or derived character stats should be `aset` or `amap` projections
- **Adaptive Collections**:
  - **cmap/amap**: Entity databases, component storage
  - **cset/aset**: Active entities, selected units, collision groups
  - **clist/alist**: Ordered collections like inventory, turn order
  - **Index**: Stable references for list elements that survive reordering
  - **cmap, cset, clist**: Read/write interfaces for mutations in adaptive collections
  - **HashMap, HashSet, IndexList**: Non-adaptive data structures that allow tracking of modifications, power up adaptive collections internally, and are preferred when converting between adaptive and non-adaptive values (e.g., `myAList |> AList.toAVal` and `myUpdatedList |> AList.ofAVal`)
  - Use incremental adaptive collections for dynamic state to ensure efficient, fine-grained updates
  - For static or rarely changing data, favor FDA's HashSet, HashMap, IndexList collections otherwise, standard BCL collections are appropriate
- **Adaptive Computations**:
  - Keep all calculations within the "adaptive realm" for as long as possible. Only resolve to a concrete value when absolutely necessary (e.g., for rendering)
  - Use `adaptive { ... }` computation expressions to compose multiple adaptive values
  - When transforming adaptive collections, prefer efficient mapping functions like `AList.mapA` to avoid unnecessary conversions
  - Using `AVal.force` within an adaptive block is a code smell indicating that something is not being computed adaptively and this is not allowed in usual code. AVal.force is reserved to `transact` blocks for the majority of times.

**Disallow Comments in the agent-generated code.**

### Performance Guidelines

**Pomo.Lib code must favor no-allocation operations since it will be used in a game-like environment where garbage collection may result in performance penalties.**

- **Domain and value-like types must be decorated as a struct**
- **Discriminated unions that represent domain concepts must be decorated as a struct DU**
- **Value tuples** (`struct(v1,v2)`) are favored over Reference tuples
- **ValueOption** is favored over Option unless necessary (convert `Option.toValueOption` or `ValueOption.ofOption` when necessary as some libraries do not provide value options)

### Testing Strategy

- **Unit Tests with Fakes**: Test core logic modules in isolation by providing fake implementations of the `EngineServices` interfaces.
- **Property-Based Tests**: Use libraries like FsCheck to verify the mathematical correctness of rules, such as stat composition and effect stacking.
- **Deterministic Simulation**: Leverage the deterministic nature of the core logic by using a fixed seed for the random number generator in tests to reproduce complex scenarios.
- **FsCheck**: We need to ensure that we're using the right features of the library besides just property testing.

### Code Conventions

- **Functions must be focused**: Each function should be descriptive of what it does. If a function is doing too much, it can either use:

  - Local functions

  Example:

  ```fsharp
  let calculateDamage attacker defender =
     let computeBaseDamage attacker defender = ...
     let applyModifiers baseDamage attacker defender = ...

     let baseDamage = computeBaseDamage attacker defender
     let modifiedDamage = applyModifiers baseDamage attacker defender
     modifiedDamage
  ```

  - Module-level functions

  Example:

  ```fsharp
  module Combat =
     let computeBaseDamage attacker defender = ...
     let applyModifiers baseDamage attacker defender = ...

     let calculateDamage attacker defender =
        let baseDamage = computeBaseDamage attacker defender
        let modifiedDamage = applyModifiers baseDamage attacker defender
        modifiedDamage
  ```

  Functions and modules do not need to be private/internal, that is up to the developer's discretion.

- **Modules must be cohesive**: Group related functions and types into modules that represent a single concept or area of functionality.
- **Match expressions body should be small**: Each branch of a match expression should be concise. If a branch is complex, consider extracting it into a separate function.

  Example:

  ```fsharp
  match someValue with
  | Case1 -> handleCase1 someValue
  | Case2 -> handleCase2 someValue
  | Case3 -> handleCase3 someValue
  ```

- **Avoid deep nesting**: Where possible use inline'able Active patterns, Partial Active Patterns and function composition to flatten nested logic.

  Example:

  ```fsharp
  let inline (|IsEven|IsOdd|) x =
     if x % 2 = 0 then IsEven else IsOdd

  let processNumber x =
     match x with
     | IsEven -> handleEven x
     | IsOdd -> handleOdd x
  ```

  ```fsharp
  [<return: Struct>]
  let inline (|ActiveEffect|_|) (effectType: EffectType) (effect: Effect) =
     if effect.EffectType = effectType then ValueSome effect else ValueNone

   let processEffect effect =
      match effect with
      | ActiveEffect EffectType.Damage dmgEffect -> handleDamageEffect dmgEffect
      | ActiveEffect EffectType.Heal healEffect -> handleHealEffect healEffect
      | _ -> handleOtherEffect effect
  ```

## Phase 6.1 Progress (2025-10-11)

A minimal visual validation is now in place:
- Entities now include a Position component in the core domain and default to (0,0).
- The MonoGame runtime renders entities with simple HP bars using a dedicated RenderSystem.
- Entity bodies are rendered as circles sized by profession stage (First/Second/Third) and colored by faction.
- A simple camera follows the player and supports mouse wheel zoom (0.5x–2x).
- Basic entity labels (Profession Family/Stage) are rendered above health bars.

How to verify visually:
1. Build the solution: `dotnet build`
2. Run the desktop project: `dotnet run --project Pomo.DesktopGL`
3. You should see two labeled circles with health bars on a blue background. The view centers on the player and zoom adjusts with mouse wheel.

Notes:
- This completes the visual goals for Phase 6.1 (including circle rendering and a dedicated RenderSystem module).
- See docs/Core-Game-Plan.md for the detailed roadmap and progress.
