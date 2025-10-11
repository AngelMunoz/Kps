# Phase 5.6 - Database Migration & Content System

**Status**: 📋 **PLANNING** - Optional future enhancement

**Created**: 2025-10-10

## Overview

This phase transitions the hardcoded content definitions in `Content.fs` to a SQLite database, enabling:
- Dynamic content loading without recompilation
- Easier content authoring and modding support
- Potential for runtime content updates
- Better content versioning and migration

**Note**: This phase is marked as **optional** and can be deferred based on complexity vs. immediate need.

---

## 1. Goals & Motivation

### 1.1 Current State
All game content is defined in `Content.fs`:
- **EffectStore**: ~7 effect definitions (Poison, Regen, Silence, etc.)
- **AbilityStore**: ~7 ability definitions (Fireball, Melee Attack, etc.)
- **FormulaStore**: ~4 formula definitions (Physical+Neutral, Fire+Magical, etc.)
- **EquipmentStore**: ~18 equipment items
- **CharacterKitStore**: ~12 character kits

**Pros of Current Approach**:
- ✅ Type-safe at compile time
- ✅ Fast (no I/O overhead)
- ✅ Simple to reason about

**Cons of Current Approach**:
- ❌ Requires recompilation for content changes
- ❌ No modding support
- ❌ Hard to balance (designers need to edit F# code)
- ❌ No content versioning

### 1.2 Benefits of Database Approach
- ✅ **Dynamic Content**: Load content at runtime from SQLite
- ✅ **Designer-Friendly**: Non-programmers can edit content
- ✅ **Modding Support**: Players can create custom content
- ✅ **Versioning**: Track content changes over time
- ✅ **Tooling**: Build content editors and validators
- ✅ **Hot Reload**: Change content without restarting game (dev mode)

### 1.3 Trade-offs
- ⚠️ **Complexity**: Need schema, migrations, loading logic
- ⚠️ **Type Safety**: Runtime validation instead of compile-time
- ⚠️ **Performance**: Database I/O (mitigated by caching)
- ⚠️ **Debugging**: Harder to trace content errors

---

## 2. SQLite Schema Design

### 2.1 Core Tables

#### **Effects Table**
```sql
CREATE TABLE Effects (
    EffectId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Kind TEXT NOT NULL CHECK(Kind IN ('Buff', 'Debuff', 'DamageOverTime', 'HealOverTime', 'Stun', 'Silence', 'Taunt')),
    StackingRule TEXT NOT NULL CHECK(StackingRule IN ('NoStack', 'RefreshDuration', 'AddStack')),
    MaxStacks INTEGER CHECK(MaxStacks IS NULL OR MaxStacks > 0),
    DurationType TEXT NOT NULL CHECK(DurationType IN ('Instant', 'Timed', 'Loop', 'Permanent')),
    Duration REAL CHECK(Duration IS NULL OR Duration > 0),
    LoopInterval REAL CHECK(LoopInterval IS NULL OR LoopInterval > 0),
    LoopCount INTEGER CHECK(LoopCount IS NULL OR LoopCount > 0),
    FormulaId INTEGER REFERENCES Formulas(FormulaId),
    Created TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    Modified TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_effects_kind ON Effects(Kind);
```

#### **Effect Modifiers Table**
```sql
CREATE TABLE EffectModifiers (
    ModifierId INTEGER PRIMARY KEY AUTOINCREMENT,
    EffectId INTEGER NOT NULL REFERENCES Effects(EffectId) ON DELETE CASCADE,
    ModifierType TEXT NOT NULL CHECK(ModifierType IN ('StaticMod', 'DynamicMod', 'AbilityDamageMod', 'ResourceConversion')),

    -- For StaticMod
    StatModType TEXT CHECK(StatModType IN ('Additive', 'Subtractive', 'Multiplicative', 'Divisive')),
    Stat TEXT CHECK(Stat IN ('AP', 'AC', 'DX', 'MP', 'MA', 'MD', 'WT', 'DA', 'LK', 'HP', 'DP', 'HV')),
    StaticValue INTEGER,

    -- For DynamicMod
    FormulaId INTEGER REFERENCES Formulas(FormulaId),

    -- For AbilityDamageMod
    DamageMultiplier REAL CHECK(DamageMultiplier IS NULL OR DamageMultiplier > 0),

    -- For ResourceConversion
    ConversionType TEXT CHECK(ConversionType IN ('HPCostAmplifier', 'MPToHP', 'HPToMP')),
    ConversionRate REAL,

    FOREIGN KEY (EffectId) REFERENCES Effects(EffectId)
);

CREATE INDEX idx_effect_modifiers_effect ON EffectModifiers(EffectId);
```

#### **Abilities Table**
```sql
CREATE TABLE Abilities (
    AbilityId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Profession TEXT CHECK(Profession IN ('Power', 'Magic', 'Sense', 'Charm') OR Profession IS NULL),
    SkillType TEXT NOT NULL CHECK(SkillType IN ('Active', 'Passive')),
    TargetingType TEXT NOT NULL CHECK(TargetingType IN ('NoTarget', 'Self', 'SingleAlly', 'SingleEnemy', 'MultiTarget', 'AllAllies', 'AllEnemies')),
    MaxTargets INTEGER CHECK(MaxTargets IS NULL OR MaxTargets > 0),
    CostHP INTEGER DEFAULT 0 CHECK(CostHP >= 0),
    CostMP INTEGER DEFAULT 0 CHECK(CostMP >= 0),
    Cooldown INTEGER DEFAULT 0 CHECK(Cooldown >= 0),
    FormulaId INTEGER REFERENCES Formulas(FormulaId),
    Created TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    Modified TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_abilities_type ON Abilities(SkillType);
CREATE INDEX idx_abilities_profession ON Abilities(Profession);
```

#### **Ability Effects Table** (Junction)
```sql
CREATE TABLE AbilityEffects (
    AbilityId INTEGER NOT NULL REFERENCES Abilities(AbilityId) ON DELETE CASCADE,
    EffectId INTEGER NOT NULL REFERENCES Effects(EffectId) ON DELETE CASCADE,
    EffectOrder INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (AbilityId, EffectId)
);

CREATE INDEX idx_ability_effects_ability ON AbilityEffects(AbilityId);
```

#### **Ability Requirements Table**
```sql
CREATE TABLE AbilityRequirements (
    RequirementId INTEGER PRIMARY KEY AUTOINCREMENT,
    AbilityId INTEGER NOT NULL REFERENCES Abilities(AbilityId) ON DELETE CASCADE,
    RequiredAbilityId INTEGER NOT NULL REFERENCES Abilities(AbilityId),
    FOREIGN KEY (AbilityId) REFERENCES Abilities(AbilityId),
    FOREIGN KEY (RequiredAbilityId) REFERENCES Abilities(AbilityId)
);

CREATE INDEX idx_ability_requirements_ability ON AbilityRequirements(AbilityId);
```

#### **Formulas Table**
```sql
CREATE TABLE Formulas (
    FormulaId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Context TEXT NOT NULL CHECK(Context IN ('Attacker', 'Defender', 'Both')),
    FormulaText TEXT NOT NULL,
    Element TEXT CHECK(Element IN ('Fire', 'Water', 'Earth', 'Air', 'Lightning', 'Light', 'Dark', 'Neutral')),
    DamageType TEXT CHECK(DamageType IN ('Physical', 'Magical', 'True')),
    Created TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    Modified TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_formulas_element ON Formulas(Element);
CREATE INDEX idx_formulas_damage_type ON Formulas(DamageType);
```

#### **Equipment Table**
```sql
CREATE TABLE Equipment (
    ItemId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Slot TEXT NOT NULL CHECK(Slot IN ('Head', 'Chest', 'Legs', 'Hands', 'Weapon1', 'Weapon2', 'Accessory')),
    Rarity TEXT NOT NULL CHECK(Rarity IN ('Common', 'Uncommon', 'Rare', 'Epic', 'Legendary')),
    Created TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    Modified TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_equipment_slot ON Equipment(Slot);
CREATE INDEX idx_equipment_rarity ON Equipment(Rarity);
```

#### **Equipment Stat Bonuses Table**
```sql
CREATE TABLE EquipmentStatBonuses (
    BonusId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES Equipment(ItemId) ON DELETE CASCADE,
    Stat TEXT NOT NULL CHECK(Stat IN ('AP', 'AC', 'DX', 'MP', 'MA', 'MD', 'WT', 'DA', 'LK', 'HP', 'DP', 'HV')),
    Value INTEGER NOT NULL,
    FOREIGN KEY (ItemId) REFERENCES Equipment(ItemId)
);

CREATE INDEX idx_equipment_stat_bonuses_item ON EquipmentStatBonuses(ItemId);
```

#### **Equipment Elemental Attributes Table**
```sql
CREATE TABLE EquipmentElementalAttributes (
    AttributeId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES Equipment(ItemId) ON DELETE CASCADE,
    Element TEXT NOT NULL CHECK(Element IN ('Fire', 'Water', 'Earth', 'Air', 'Lightning', 'Light', 'Dark')),
    Value REAL NOT NULL,
    FOREIGN KEY (ItemId) REFERENCES Equipment(ItemId)
);

CREATE INDEX idx_equipment_elemental_attributes_item ON EquipmentElementalAttributes(ItemId);
```

#### **Equipment Elemental Resistances Table**
```sql
CREATE TABLE EquipmentElementalResistances (
    ResistanceId INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL REFERENCES Equipment(ItemId) ON DELETE CASCADE,
    Element TEXT NOT NULL CHECK(Element IN ('Fire', 'Water', 'Earth', 'Air', 'Lightning', 'Light', 'Dark')),
    Value REAL NOT NULL,
    FOREIGN KEY (ItemId) REFERENCES Equipment(ItemId)
);

CREATE INDEX idx_equipment_elemental_resistances_item ON EquipmentElementalResistances(ItemId);
```

#### **Character Kits Table**
```sql
CREATE TABLE CharacterKits (
    KitId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Family TEXT NOT NULL CHECK(Family IN ('Power', 'Magic', 'Sense', 'Charm')),
    Stage TEXT NOT NULL CHECK(Stage IN ('First', 'Second', 'Third')),
    BasePower INTEGER NOT NULL,
    BaseMagic INTEGER NOT NULL,
    BaseSense INTEGER NOT NULL,
    BaseCharm INTEGER NOT NULL,
    Created TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    Modified TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(Family, Stage)
);

CREATE INDEX idx_character_kits_family_stage ON CharacterKits(Family, Stage);
```

#### **Character Kit Abilities Table** (Junction)
```sql
CREATE TABLE CharacterKitAbilities (
    KitId INTEGER NOT NULL REFERENCES CharacterKits(KitId) ON DELETE CASCADE,
    AbilityId INTEGER NOT NULL REFERENCES Abilities(AbilityId) ON DELETE CASCADE,
    PRIMARY KEY (KitId, AbilityId)
);

CREATE INDEX idx_character_kit_abilities_kit ON CharacterKitAbilities(KitId);
```

### 2.2 Metadata & Versioning

#### **Schema Version Table**
```sql
CREATE TABLE SchemaVersion (
    Version INTEGER PRIMARY KEY,
    AppliedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    Description TEXT
);

INSERT INTO SchemaVersion (Version, Description) VALUES (1, 'Initial schema');
```

#### **Content Version Table**
```sql
CREATE TABLE ContentVersion (
    ContentType TEXT PRIMARY KEY CHECK(ContentType IN ('Effects', 'Abilities', 'Formulas', 'Equipment', 'CharacterKits')),
    Version INTEGER NOT NULL,
    LastModified TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO ContentVersion (ContentType, Version) VALUES
    ('Effects', 1),
    ('Abilities', 1),
    ('Formulas', 1),
    ('Equipment', 1),
    ('CharacterKits', 1);
```

---

## 3. Data Migration Strategy

### 3.1 Migration Scripts

#### **Step 1: Create Empty Database**
```powershell
# create-database.ps1
sqlite3 Content.db < schema.sql
```

#### **Step 2: Generate Seed Data from Content.fs**
Create an F# script that reads `Content.fs` definitions and generates SQL INSERT statements:

```fsharp
// GenerateSeedData.fsx
#r "nuget: FSharp.Data.Adaptive"
#load "Pomo.Lib/Domain.fs"
#load "Pomo.Lib/Content.fs"

open System.IO
open Pomo.Lib.Content

let escapeString (s: string) =
    s.Replace("'", "''")

let generateEffectInserts() =
    EffectStore.definitions
    |> Map.toSeq
    |> Seq.map (fun (id, effect) ->
        sprintf "INSERT INTO Effects (EffectId, Name, Kind, StackingRule, ...) VALUES (%d, '%s', '%s', ...);"
            (int id) (escapeString effect.Name) (string effect.Kind))
    |> String.concat "\n"

let generateAbilityInserts() =
    // Similar logic for abilities
    ""

let generateAll() =
    let sql = [
        "-- Effects"
        generateEffectInserts()
        ""
        "-- Abilities"
        generateAbilityInserts()
        // ... etc
    ]
    File.WriteAllText("seed-data.sql", String.concat "\n" sql)

generateAll()
```

#### **Step 3: Seed Database**
```powershell
# seed-database.ps1
dotnet fsi GenerateSeedData.fsx
sqlite3 Content.db < seed-data.sql
```

### 3.2 Validation Script
After migration, validate that all data is correctly inserted:

```sql
-- validate-content.sql
SELECT 'Effects' AS ContentType, COUNT(*) AS Count FROM Effects
UNION ALL
SELECT 'Abilities', COUNT(*) FROM Abilities
UNION ALL
SELECT 'Formulas', COUNT(*) FROM Formulas
UNION ALL
SELECT 'Equipment', COUNT(*) FROM Equipment
UNION ALL
SELECT 'CharacterKits', COUNT(*) FROM CharacterKits;

-- Check for orphaned references
SELECT 'Orphaned Effect References' AS Issue, COUNT(*) AS Count
FROM EffectModifiers em
LEFT JOIN Effects e ON em.EffectId = e.EffectId
WHERE e.EffectId IS NULL;

-- Similar checks for other foreign keys
```

---

## 4. Content Loading System

### 4.1 Database Access Layer

```fsharp
// ContentDatabase.fs
namespace Pomo.Lib.ContentDatabase

open System.Data.SQLite
open FSharp.Data.Adaptive
open Pomo.Lib.Domain

type ContentDatabase(connectionString: string) =
    let connection = new SQLiteConnection(connectionString)

    member _.Open() = connection.Open()
    member _.Close() = connection.Close()

    member _.LoadEffects() : Map<int<EffectId>, EffectDefinition> =
        use cmd = new SQLiteCommand("SELECT * FROM Effects", connection)
        use reader = cmd.ExecuteReader()

        let mutable effects = Map.empty
        while reader.Read() do
            let effectId = reader.GetInt32(0) * 1<EffectId>
            let name = reader.GetString(1)
            let kind = parseEffectKind (reader.GetString(3))
            // ... parse remaining fields

            // Load modifiers
            let modifiers = this.LoadEffectModifiers(effectId)

            let effect = {
                Name = name
                Kind = kind
                Modifiers = modifiers
                // ... etc
            }
            effects <- Map.add effectId effect effects

        effects

    member private _.LoadEffectModifiers(effectId: int<EffectId>) : EffectModifier[] =
        use cmd = new SQLiteCommand("SELECT * FROM EffectModifiers WHERE EffectId = @id", connection)
        cmd.Parameters.AddWithValue("@id", int effectId) |> ignore
        use reader = cmd.ExecuteReader()

        let modifiers = ResizeArray<EffectModifier>()
        while reader.Read() do
            let modType = reader.GetString(2)
            let modifier =
                match modType with
                | "StaticMod" ->
                    let statModType = reader.GetString(3)
                    let stat = parseStat (reader.GetString(4))
                    let value = reader.GetInt32(5)
                    EffectModifier.StaticMod (parseStatModifier statModType stat value)
                | "DynamicMod" ->
                    let formulaId = reader.GetInt32(6) * 1<FormulaId>
                    let stat = parseStat (reader.GetString(4))
                    EffectModifier.DynamicMod (stat, formulaId)
                | "AbilityDamageMod" ->
                    let multiplier = reader.GetDouble(7)
                    EffectModifier.AbilityDamageMod multiplier
                | "ResourceConversion" ->
                    let convType = reader.GetString(8)
                    let rate = reader.GetDouble(9)
                    EffectModifier.ResourceConversion (parseConversionType convType rate)
                | _ -> failwithf "Unknown modifier type: %s" modType
            modifiers.Add(modifier)

        modifiers.ToArray()

    member _.LoadAbilities() : Map<int<AbilityId>, AbilityKind> =
        // Similar to LoadEffects
        Map.empty

    member _.LoadFormulas() : Map<int<FormulaId>, FormulaDefinition> =
        // Similar to LoadEffects
        Map.empty

    member _.LoadEquipment() : Map<int<ItemId>, Equipment> =
        // Similar to LoadEffects
        Map.empty

    member _.LoadCharacterKits() : Map<int<KitId>, CharacterKit> =
        // Similar to LoadEffects
        Map.empty

// Helper parsers
let parseEffectKind (s: string) : EffectKind =
    match s with
    | "Buff" -> EffectKind.Buff
    | "Debuff" -> EffectKind.Debuff
    | "DamageOverTime" -> EffectKind.DamageOverTime
    | "HealOverTime" -> EffectKind.HealOverTime
    | "Stun" -> EffectKind.Stun
    | "Silence" -> EffectKind.Silence
    | "Taunt" -> EffectKind.Taunt
    | _ -> failwithf "Unknown effect kind: %s" s

// ... similar parsers for other enums
```

### 4.2 Database-Backed Store Services

```fsharp
// DatabaseStores.fs
namespace Pomo.Lib.ContentDatabase

open Pomo.Lib.Domain.Services
open Pomo.Lib.ContentDatabase

type DatabaseEffectStore(db: ContentDatabase) =
    let effects = lazy (db.LoadEffects())

    interface IEffectStore with
        member _.tryFind effectId =
            effects.Value |> Map.tryFind effectId |> ValueOption.ofOption

        member _.find effectId =
            effects.Value |> Map.find effectId

type DatabaseAbilityStore(db: ContentDatabase) =
    let abilities = lazy (db.LoadAbilities())

    interface IAbilityStore with
        member _.tryFind abilityId =
            abilities.Value |> Map.tryFind abilityId |> ValueOption.ofOption

        member _.find abilityId =
            abilities.Value |> Map.find abilityId

type DatabaseFormulaStore(db: ContentDatabase) =
    let formulas = lazy (db.LoadFormulas())

    interface IFormulaStore with
        member _.tryFind formulaId =
            formulas.Value |> Map.tryFind formulaId |> ValueOption.ofOption

        member _.find formulaId =
            formulas.Value |> Map.find formulaId
```

### 4.3 Integration with GameState

```fsharp
// In Pomo.Core or test setup
let createGameStateWithDatabase (dbPath: string) =
    let db = new ContentDatabase($"Data Source={dbPath};Version=3;")
    db.Open()

    let services = {
        effectStore = DatabaseEffectStore(db) :> IEffectStore
        abilityStore = DatabaseAbilityStore(db) :> IAbilityStore
        formulaStore = DatabaseFormulaStore(db) :> IFormulaStore
        rng = fun () -> System.Random().NextDouble()
    }

    GameState.create' services
```

---

## 5. Formula Integration

### 5.1 Storing Formulas

Formulas are stored as text in the `Formulas` table:

```sql
INSERT INTO Formulas (FormulaId, Name, FormulaText, Element, DamageType) VALUES
(1, 'Physical + Neutral', 'AP * 1.5 + DX * 0.5', 'Neutral', 'Physical'),
(2, 'Fire + Magical', 'MA * 2.0 + Fire - FireRes * 0.5', 'Fire', 'Magical'),
(3, 'Magic + Neutral', 'MA * 1.8 + MP * 0.2', 'Neutral', 'Magical'),
(4, 'Fire + Physical', 'AP * 1.2 + Fire - FireRes * 0.3', 'Fire', 'Physical');
```

### 5.2 Loading and Parsing

The `FormulaParser.fs` is already capable of parsing formula text. The database loader uses it:

```fsharp
member _.LoadFormulas() : Map<int<FormulaId>, FormulaDefinition> =
    use cmd = new SQLiteCommand("SELECT * FROM Formulas", connection)
    use reader = cmd.ExecuteReader()

    let cache = FormulaCache()
    let mutable formulas = Map.empty

    while reader.Read() do
        let formulaId = reader.GetInt32(0) * 1<FormulaId>
        let name = reader.GetString(1)
        let formulaText = reader.GetString(3)
        let element = parseElement (reader.GetString(4))
        let damageType = parseDamageType (reader.GetString(5))

        // Parse and cache the formula expression
        let expr = cache.GetOrParse(int formulaId, formulaText)

        let formula = {
            Name = name
            Formula = formulaText
            Element = element
            DamageType = damageType
            CompiledExpr = expr // Optional: cache parsed expression
        }
        formulas <- Map.add formulaId formula formulas

    formulas
```

### 5.3 Formula Validation

Add a validation step during database seeding:

```fsharp
// ValidateFormulas.fsx
#load "Pomo.Lib/FormulaParser.fs"

open Pomo.Lib.FormulaParser
open System.Data.SQLite

let validateFormulas (dbPath: string) =
    use conn = new SQLiteConnection($"Data Source={dbPath};Version=3;")
    conn.Open()

    use cmd = new SQLiteCommand("SELECT FormulaId, Name, FormulaText FROM Formulas", conn)
    use reader = cmd.ExecuteReader()

    let mutable errors = []
    while reader.Read() do
        let id = reader.GetInt32(0)
        let name = reader.GetString(1)
        let text = reader.GetString(2)

        try
            FormulaParser.parse text |> ignore
            printfn "[OK] Formula %d (%s): %s" id name text
        with ex ->
            errors <- (id, name, text, ex.Message) :: errors
            printfn "[ERROR] Formula %d (%s): %s - %s" id name text ex.Message

    if List.isEmpty errors then
        printfn "\nAll formulas are valid!"
        0
    else
        printfn "\n%d formula(s) failed validation" (List.length errors)
        1

// Run validation
let exitCode = validateFormulas "Content.db"
exit exitCode
```

---

## 6. Development Workflow

### 6.1 Dual-Mode Support (Transition Period)

During migration, support both hardcoded and database content:

```fsharp
// ContentFactory.fs
type ContentSource =
    | Hardcoded
    | Database of dbPath: string

let createServices (source: ContentSource) : EngineServices =
    match source with
    | Hardcoded ->
        // Existing Content.fs approach
        {
            effectStore = HardcodedEffectStore() :> IEffectStore
            abilityStore = HardcodedAbilityStore() :> IAbilityStore
            formulaStore = HardcodedFormulaStore() :> IFormulaStore
            rng = fun () -> System.Random().NextDouble()
        }

    | Database dbPath ->
        let db = new ContentDatabase($"Data Source={dbPath};Version=3;")
        db.Open()
        {
            effectStore = DatabaseEffectStore(db) :> IEffectStore
            abilityStore = DatabaseAbilityStore(db) :> IAbilityStore
            formulaStore = DatabaseFormulaStore(db) :> IFormulaStore
            rng = fun () -> System.Random().NextDouble()
        }
```

### 6.2 Content Editing Tools

Build simple CLI tools for content authors:

```fsharp
// ContentEditor.fsx
// Usage: dotnet fsi ContentEditor.fsx add-effect "Burn" "DamageOverTime" ...

open System
open System.Data.SQLite

let addEffect (db: SQLiteConnection) name kind stackingRule duration =
    use cmd = new SQLiteCommand("INSERT INTO Effects (Name, Kind, StackingRule, ...) VALUES (@name, @kind, ...)", db)
    cmd.Parameters.AddWithValue("@name", name) |> ignore
    cmd.Parameters.AddWithValue("@kind", kind) |> ignore
    // ... etc
    cmd.ExecuteNonQuery() |> ignore
    printfn "Added effect: %s" name

let listEffects (db: SQLiteConnection) =
    use cmd = new SQLiteCommand("SELECT EffectId, Name, Kind FROM Effects", db)
    use reader = cmd.ExecuteReader()
    printfn "Effects:"
    while reader.Read() do
        printfn "  [%d] %s (%s)" (reader.GetInt32(0)) (reader.GetString(1)) (reader.GetString(2))

// Command dispatcher
match fsi.CommandLineArgs with
| [| _; "add-effect"; name; kind; stackRule; duration |] ->
    use db = new SQLiteConnection("Data Source=Content.db;Version=3;")
    db.Open()
    addEffect db name kind stackRule duration
| [| _; "list-effects" |] ->
    use db = new SQLiteConnection("Data Source=Content.db;Version=3;")
    db.Open()
    listEffects db
| _ ->
    printfn "Usage: dotnet fsi ContentEditor.fsx <command> [args]"
    printfn "Commands: add-effect, list-effects, ..."
```

### 6.3 Hot Reload (Dev Mode)

For rapid iteration during development:

```fsharp
// In Pomo.Core
type HotReloadContentManager(dbPath: string) =
    let mutable lastModified = DateTime.MinValue
    let mutable services: EngineServices option = None

    member _.GetServices() =
        let fileInfo = System.IO.FileInfo(dbPath)
        if fileInfo.LastWriteTime > lastModified then
            printfn "[Hot Reload] Reloading content from database..."
            let db = new ContentDatabase($"Data Source={dbPath};Version=3;")
            db.Open()
            services <- Some {
                effectStore = DatabaseEffectStore(db) :> IEffectStore
                abilityStore = DatabaseAbilityStore(db) :> IAbilityStore
                formulaStore = DatabaseFormulaStore(db) :> IFormulaStore
                rng = fun () -> System.Random().NextDouble()
            }
            lastModified <- fileInfo.LastWriteTime
        services.Value

    member this.Update(gameTime) =
        // Check for database changes every second
        if gameTime.TotalGameTime.TotalSeconds % 1.0 < 0.016 then
            this.GetServices() |> ignore
```

---

## 7. Testing Strategy

### 7.1 Database Schema Tests
```fsharp
// Test that schema is correctly applied
[<Fact>]
let ``Schema creates all tables`` () =
    use db = new ContentDatabase(":memory:")
    db.Open()
    // Run schema.sql

    let tables = ["Effects"; "Abilities"; "Formulas"; "Equipment"; "CharacterKits"]
    for table in tables do
        use cmd = new SQLiteCommand($"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}'", db.Connection)
        let result = cmd.ExecuteScalar()
        Assert.NotNull(result)
```

### 7.2 Data Migration Tests
```fsharp
// Test that Content.fs data matches database data
[<Fact>]
let ``Database contains same effects as Content.fs`` () =
    let hardcodedEffects = Content.EffectStore.definitions

    use db = new ContentDatabase("Content.db")
    db.Open()
    let dbEffects = db.LoadEffects()

    Assert.Equal(Map.count hardcodedEffects, Map.count dbEffects)
    for KeyValue(id, effect) in hardcodedEffects do
        Assert.True(Map.containsKey id dbEffects)
        let dbEffect = Map.find id dbEffects
        Assert.Equal(effect.Name, dbEffect.Name)
        // ... compare other fields
```

### 7.3 Performance Tests
```fsharp
[<Fact>]
let ``Loading all content takes less than 100ms`` () =
    use db = new ContentDatabase("Content.db")
    db.Open()

    let sw = System.Diagnostics.Stopwatch.StartNew()
    let effects = db.LoadEffects()
    let abilities = db.LoadAbilities()
    let formulas = db.LoadFormulas()
    let equipment = db.LoadEquipment()
    let kits = db.LoadCharacterKits()
    sw.Stop()

    Assert.True(sw.ElapsedMilliseconds < 100L, $"Loading took {sw.ElapsedMilliseconds}ms")
```

---

## 8. Migration Path

### 8.1 Phase 1: Schema & Migration Scripts
- [ ] Design and create SQLite schema
- [ ] Write seed data generation script
- [ ] Create validation scripts
- [ ] Document schema and relationships

### 8.2 Phase 2: Database Access Layer
- [ ] Implement `ContentDatabase` class
- [ ] Implement parsers for all domain types
- [ ] Write unit tests for loading logic
- [ ] Performance benchmarking

### 8.3 Phase 3: Store Services
- [ ] Implement `DatabaseEffectStore`
- [ ] Implement `DatabaseAbilityStore`
- [ ] Implement `DatabaseFormulaStore`
- [ ] Add caching layer for performance

### 8.4 Phase 4: Integration & Testing
- [ ] Integrate with GameState initialization
- [ ] Run full test suite with database content
- [ ] Compare behavior with hardcoded content
- [ ] Fix any discrepancies

### 8.5 Phase 5: Tooling & Polish
- [ ] Create content editor CLI tools
- [ ] Implement hot reload for dev mode
- [ ] Document content authoring workflow
- [ ] Create content validation pipeline

---

## 9. Complexity Analysis

### 9.1 Estimated Effort
- **Schema Design**: 4-6 hours
- **Migration Scripts**: 8-12 hours
- **Database Access Layer**: 16-24 hours
- **Store Services**: 8-12 hours
- **Integration & Testing**: 12-16 hours
- **Tooling**: 8-12 hours

**Total**: ~56-82 hours (7-10 days of focused work)

### 9.2 Risk Assessment
- ⚠️ **Medium Risk**: Schema changes require migration logic
- ⚠️ **Medium Risk**: Performance degradation if not cached properly
- ⚠️ **Low Risk**: Type safety issues (mitigated by validation)

### 9.3 Recommendation

**Defer to Post-Phase 6**:
- The current hardcoded approach is sufficient for Phase 6 (MonoGame integration)
- Database migration is a **nice-to-have** for content authoring and modding
- Can be implemented incrementally after core gameplay is stable
- Consider implementing only if content volume grows significantly (>50 abilities, >100 items)

**Alternative**: Start with a simpler JSON/YAML approach before committing to SQLite.

---

## 10. Deliverables

### 10.1 Code Deliverables
- [ ] `schema.sql` - Complete database schema
- [ ] `GenerateSeedData.fsx` - F# script to generate seed data from Content.fs
- [ ] `ContentDatabase.fs` - Database access layer
- [ ] `DatabaseStores.fs` - IEffectStore/IAbilityStore/IFormulaStore implementations
- [ ] `ContentEditor.fsx` - CLI tools for content editing
- [ ] Unit tests for all database operations

### 10.2 Documentation Deliverables
- [x] This design document
- [ ] Schema documentation with ER diagrams
- [ ] Content authoring guide
- [ ] Migration runbook

### 10.3 Data Deliverables
- [ ] `Content.db` - Seeded SQLite database with all current content
- [ ] `validate-content.sql` - Validation queries

---

## Summary

Phase 5.6 provides a path to transition from hardcoded content to a flexible, database-driven system. While this adds complexity, it enables content authoring, modding support, and runtime updates. Given the estimated effort and current project priorities, this phase is marked as **optional** and can be deferred until after Phase 6 (MonoGame integration) is complete.

**Recommendation**: Proceed with Phase 6 using the current `Content.fs` approach, then revisit this phase if content volume or modding requirements justify the investment.
