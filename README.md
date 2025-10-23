# F# and MonoGame Project Structure

This is a cross-platform game built with F# and MonoGame. The project is structured to share core logic across multiple platforms (Windows, DesktopGL, Android, iOS).

**For AI Agents**: See [AGENTS.md](./AGENTS.md) for coding guidelines and paradigm requirements.

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

This project uses a reactive architecture with FSharp.Data.Adaptive (FDA) for its core logic, implementing Data Oriented Programming principles. See [AGENTS.md](./AGENTS.md) for detailed architectural guidelines.

## Developer Workflow

### Building the Project

To build the entire solution, you can use the `dotnet build` command at the root of the repository:

```shell
dotnet build
```

### Running the Game

You can run the game on a specific platform by running the corresponding project. For example, to run the desktop version:

```shell
dotnet run --project Pomo.DesktopGL
```

### Running Tests

The tests are located in the `Pomo.Lib.Tests` project. You can run them using:

```shell
dotnet run --project Pomo.Lib.Tests
```

### Integration Tests

Not available yet.
