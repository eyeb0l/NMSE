# NMSE - No Man's Save Editor

## Project Overview

NMSE (No Man's Save Editor) is an open-source .NET/Avalonia desktop application for
viewing and editing No Man's Sky save files, originally designed and developed by [**vectorcmdr**][githubOwner].

Native macOS migration references:

- [Abandoned Avalonia branch audit](macos-port-audit.md)
- [Native macOS parity matrix](macos-port-parity.md)
- [Cosmos 7.03 integration](macos-cosmos-integration.md)

It supports saves from every platform the game ships on (Steam, GOG, Xbox Game Pass,
PlayStation 4, and Nintendo Switch) and handles each platform's unique file layout,
compression, and encryption transparently.

The editor loads a save file into an in-memory JSON tree built from custom `JsonObject` and
`JsonArray` classes (not `System.Text.Json`), presents the data through categorized tab
panels, and writes changes back while preserving binary-safe round-trip fidelity.

A companion console tool, NMSE.Extractor, mines the game's PAK archives to produce the JSON
databases and icons that the editor uses at runtime.

The codebase follows a strict separation between *Logic* classes (pure, static, testable
data manipulation) and *Panel* classes (Avalonia UI binding). All game-specific knowledge
lives in the Core and Data layers so the UI layer stays thin.

## Solution Structure

| Project | Description |
|---------|-------------|
| **NMSE** (`NMSE.csproj`) | Cross-platform Avalonia application -- Core, Data, IO, Models, `UI.Avalonia`, Config, Resources |
| **NMSE.Tests** (`NMSE.Tests/`) | Core unit tests |
| **NMSE.Avalonia.Tests** | Avalonia headless UI tests |
| **NMSE.MacUpdater.Tests** | macOS updater tests |
| **NMSE.Extractor** (`NMSE.Extractor/`) | Console tool that extracts game data from NMS PAK files |
| **NMSE.Extractor.Tests** (`NMSE.Extractor.Tests/`) | Unit tests for the extractor |
| **NMSE.Site** (`NMSE.Site/`) | Static web companion site (HTML/JS/CSS) for GitHub Pages |

Build output is redirected to `Build/bin/` and `Build/obj/` via `Directory.Build.props`.
The solution file is `NMSE.slnx` (modern XML format). `NMSE.Site` is a static companion
site and is not part of the solution.

### Build System

See [Building and publishing](building.md) for the shared local/CI commands:

```bash
./scripts/build.sh build
./scripts/build.sh test
./scripts/build.sh publish all   # Windows, Linux, Apple Silicon and Intel macOS; requires a Mac
```

Windows uses `scripts/build.ps1` with the same arguments. All build logic lives in
`scripts/build.py` (Python standard library). Publishing tests once, then produces
managed, self-contained .NET 10 packages in `Build/artifacts/`. Tiered PGO is enabled;
Native AOT is deferred until after parity. Use an explicit project for raw SDK commands
because the solution also includes the Windows-only extractor.

## Documentation Index

### [Core Logic](core-logic.md)

Logic classes that encapsulate game rules, data transformation, and domain operations.
Most are `internal static` with no mutable state; `CatalogueItemCatalog` is a scoped
instance model for the unified catalogue item lists.

- AccountLogic, BaseLogic, CatalogueLogic, CatalogueCompletionLogic (partials), CatalogueItemCatalog, CompanionLogic
- DatabaseSearchLogic, ExocraftLogic, ExosuitLogic, FreighterLogic, FrigateLogic
- InventoryBulkActions, KnowledgeCatalogue, MainStatsLogic, MilestoneLogic, MultitoolLogic
- OutfitLogic, RawJsonLogic, SaveContext, SettlementLogic, SpaceStationLogic
- SpacePoiSlotInference, SpacePoiSlotTokens, SpacePoiLayoutsFile, DiscoveriesFile
- SquadronLogic, StarshipLogic, ThemeManager, ThemeColors, UpdateService, AppJsonContext
- ExportConfig; `Core/Utilities/`: CoordinateHelper, InventoryImportHelper, InventorySlotHelper
  MathHelper, NmsColourPalette, NumericParseHelper, ProceduralSeedHelper, RawNumberGuard
  SeedHelper, StatHelper, StringHelper

### [Data Layer](data-layer.md)

Databases and helper classes that load, store, and query game reference data.

- CompanionDatabase, CreaturePartDatabase, ElementDatabase, FrigateTraitDatabase
- GalaxyDatabase, GameItemDatabase, GameItem, AvaloniaIconProvider
- InventoryStackDatabase, JsonNameMapper, LeveledStatDatabase, BaseStatLimits
- ProceduralStubs, RecipeDatabase, RewardDatabase, SettlementPerkDatabase
- TechAdjacencyDatabase, TechPackDatabase, TitleDatabase, WikiGuideDatabase
- WordDatabase, CoordinateHelper, LocalisationService, UiStrings

### [IO Layer](io-layer.md)

Save file reading, writing, compression, encryption, and platform abstraction.

- SaveFileManager, SaveSlotManager, ContainersIndexManager, MemoryDatManager
- MetaCrypto, MetaFileWriter, BinaryIO
- Lz4Compressor, Lz4CompressorStream, Lz4BufferedCompressorStream
- Lz4ChunkedCompressorStream, Lz4DecompressorStream

### [Models](models.md)

Domain model classes, the custom JSON tree, and value types.

- JsonObject, JsonArray, JsonParser, JsonReader, JsonException
- BinaryData, RawDouble, IPropertyChangeListener
- Ship, ShipType, ShipClass, Multitool, MultitoolType
- Frigate, Companion, Inventory, InventoryType
- Recipe, DifficultyLevel, SaveFileMetadata

### UI Layer

Avalonia panels, controls, and visual infrastructure live under `UI.Avalonia`.
`MainWindow` coordinates the 15 top-level areas; platform operations cross the narrow
`IDesktopServices` boundary, and save-backed panels implement `ISaveEditorPanel`.

### [Localisation](ui-localisation.md)

UI string localisation drives menus, tabs, dialog messages, status bar text,
grid headers, and other user-visible labels. The system loads per-language
JSON files from `Resources/ui/lang/{bcp47}.json`, falls back to English,
and updates all panels via their `ApplyUiLocalisation()` methods.

- UiStrings handles loading, lookup, formatting, and fallback behaviour
- Language menu triggers reloads and updates every UI panel
- Supports 16 languages (en-GB source + 15 translations)

### [NMSE.Extractor](extractor.md)

The data extraction pipeline that converts NMS game archives into editor databases.

- Program (multi-stage pipeline), `Config/ExtractorConfig`
- `Data/`: CataloguePackBuilder, Categorizer, CuratedItemNames, ImageExtractor, JsonWriter
  LocalisationBuilder, MbinConverter, MxmlParser, PakExtractor, Parsers, ProductLookup, TeeTextWriter
- `Util/`: SteamLocator, ToolManager

### Cross-platform packaging

[The build guide](building.md) covers all five runtime targets, output layout and the
shared release/PR matrix. `scripts/macos/` retains the bundle, signing, notarization and
ZIP/DMG validation tooling; `scripts/linux/` contains the FreeDesktop launcher template.
The older Wine/CrossOver/Bottles guides describe legacy Windows releases and are not
part of the current native build or installation path.

## Key Architectural Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Logic/Panel separation | Logic classes are static and testable without a UI; panels delegate all game knowledge to them |
| 2 | Custom JSON model | `JsonObject`/`JsonArray` preserve field order, support binary data, round-trip `RawDouble` values, and integrate the name mapper -- things `System.Text.Json` does not do out of the box |
| 3 | Save pipeline: containers.index / memory.dat -> LZ4 -> JSON | Each platform wraps the same JSON payload differently; the IO layer normalizes everything to a single `JsonObject` |
| 4 | Name mapper (obfuscated keys) | NMS obfuscates JSON keys to 3-character codes; the mapper translates both ways so the editor can use human-readable names internally |
| 5 | Context transforms | `PlayerStateData` resolves to either `BaseContext` or `ExpeditionContext` at runtime via registered transforms on the root `JsonObject` |
| 6 | `version.json` | `version.json` is the single source of truth for application and package versions |
| 7 | Avalonia bitmap and glyph controls | Icons are downscaled and cached without GDI+; portal glyphs render through an Avalonia control |
| 8 | InventoryGrid as reusable control | One grid control handles every inventory type (suit, ship, weapon, freighter, vehicle) with owner-type configuration |
| 9 | Multi-format import/export | `InventoryImportHelper` detects and unwraps NomNom and NMSSaveEditor wrappers so users can share inventories across tools |
| 10 | Extractor pipeline (MBIN -> MXML -> JSON) | Game data is compiled into MBIN binary; the extractor decompiles to MXML, parses to dictionaries, then categorizes into JSON database files |
| 11 | Multi-language localisation | Per-language JSON files in `Resources/json/lang/` (16 languages, BCP 47 tags). Items store `_LocStr` keys for runtime localisation lookup. The language menu switches live display language; internal logic retains English defaults while UI strings come from `Resources/ui/lang/` |
| 12 | Catalogue item state | `CatalogueItemCatalog` and `ItemUnlockRules` define Known/Seen writes for Avalonia catalogue rows; bulk actions use the visible filtered rows, while Complete Catalogue includes raw materials |
| 13 | Account rewards | Account context distinguishes ambiguous unlocks from stale rewards; redeemed snapshots refresh after each save so a second edit in the same session removes matching Known state |

### Reversed game code

Some Core logic reimplements algorithms reversed from the game. These areas are deliberately not documented here: they are game-derived reference code, provided for save interoperability only, and are not covered by the project's licence. Do not add algorithm documentation for them.


[githubOwner]: https://github.com/vectorcmdr
