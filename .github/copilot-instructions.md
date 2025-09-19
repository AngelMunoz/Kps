# F# and MonoGame Project Structure

This is a cross-platform game built with F# and MonoGame. The project is structured to share core logic across multiple platforms (Windows, DesktopGL, Android, iOS).

## Key Projects

- **`Pomo.Core`**: The main F# project that contains the primary game logic and initialization (`PomoGame.fs`). It references `Pomo.Lib` and is, in turn, referenced by the platform-specific projects.
- **`Pomo.Lib`**: An F# library containing the core game mechanics, domain models, and business logic. This is where you'll find modules for `Combat.fs`, `Gameplay.fs`, and `Domain.fs`, which define the rules and data structures of the game.
- **`Pomo.DesktopGL`, `Pomo.WindowsDX`, `Pomo.Android`, `Pomo.iOS`**: These are the platform-specific "head" projects. They contain the entry points (`Program.fs` or `MainActivity.fs`) to launch the game on their respective targets.
- **`Pomo.Lib.Tests`**: Unit tests for the `Pomo.Lib` project, written using `xUnit` and `FsCheck`.

## Architecture

The architecture is based on a shared core and platform-specific heads. The core logic is written in F# and is completely separated from the platform-specific code.

- **Game Logic**: The core game logic is in `Pomo.Lib`. This library is responsible for managing the game state, character interactions, combat resolution, and other mechanics.
- **Game Loop**: The main game loop and scene management are handled in `Pomo.Core/PomoGame.fs`.
- **Domain Model**: The core data structures are defined in `Pomo.Lib/Domain.fs`. These are immutable F# records and discriminated unions, which are used throughout the game logic.

## Developer Workflow

- **Building the project**: To build the entire solution, you can use the `dotnet build` command at the root of the repository.
- **Running the game**: You can run the game on a specific platform by running the corresponding project. For example, to run the desktop version, you can execute:
  ```shell
  dotnet run --project Pomo.DesktopGL
  ```
- **Running tests**: The tests are located in the `Pomo.Lib.Tests` project. You can run them using the `dotnet test --project ./Pomo.Lib.Tests/Pomo.Lib.Tests.fsproj` command.

## Architectural and Coding Principles

This project uses a reactive architecture with FSharp.Data.Adaptive (FDA) for its core logic. Adhere to the following principles derived from the project's design documents.

### Reactive Core with FSharp.Data.Adaptive (FDA)

- **Authoritative State**: The game's core state is maintained in a central, immutable `GameState` record containing adaptive values (`cval`, `cmap`, `clist`).
- **Derived Data**: All other game views and stats should be derived from this authoritative state using adaptive computations. For example, a set of "alive" entities or derived character stats should be `aset` or `amap` projections.
- **Adaptive Collections**:
  - Use incremental adaptive collections (`amap`, `aset`, `alist`) for dynamic state to ensure efficient, fine-grained updates.
  - For static or rarely changing data, standard BCL collections are appropriate.
- **Adaptive Computations**:
  - Keep all calculations within the "adaptive realm" for as long as possible. Only resolve to a concrete value when absolutely necessary (e.g., for rendering).
  - Use `adaptive { ... }` computation expressions to compose multiple adaptive values.
  - When transforming adaptive collections, prefer efficient mapping functions like `AList.mapA` to avoid unnecessary conversions.

### Testing Strategy

- **Unit Tests with Fakes**: Test core logic modules in isolation by providing fake implementations of the `EngineServices` interfaces.
- **Property-Based Tests**: Use libraries like FsCheck to verify the mathematical correctness of rules, such as stat composition and effect stacking.
- **Deterministic Simulation**: Leverage the deterministic nature of the core logic by using a fixed seed for the random number generator in tests to reproduce complex scenarios.
- **FsCheck**: We need to ensure that we're using the right features of the library besides just property testing.
