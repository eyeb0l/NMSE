# Cosmos 7.03 integration

This integration merges upstream through `eb1ae1f` (NMSE 1.3.16) into the
native Avalonia port. The Windows extractor changes and generated Cosmos
databases are retained. The app continues to target generic .NET 10 and
self-contained Apple Silicon publishing; WinForms and Wine launchers are
excluded.

## Native adaptations

- Database Search is a sixteenth top-level tab, available without a loaded save.
  It uses the upstream multi-term and wildcard query engine.
- Catalogue includes Wonders, Collected Knowledge, Fossils, Raw Materials and
  Discovery Stats controls using the shared completion logic. Account catalogue
  changes edit the loaded account object. Bulk changes use the shell confirmation
  dialog; numeric progress can be edited separately.
- Bases lists station, corvette and space bases and disables planetary operations
  for those entries. Systems edits station standings, Cosmos missions and packed
  POI states. Inferred POI types use the shared layout cache.
- Discoveries are stored under `ApplicationPaths.Current.UserDataDirectory`,
  outside the signed app bundle.
- Difficulty-preset changes synchronize the game mode. Unchanged presets and
  unknown multitool models are preserved.
- Switch slots start at `savedata02.hg`; `savedata00.hg` settings are never
  treated as account data. Switch account writes use the platform metadata writer.
- The consistency dialog marks data modified only after a resolution.
- Raw JSON supports isolated node editing in a modal editor, including property
  renaming, validation and undo. The existing tree/text/split views remain.
- Xbox sessions retain the container identifier, resolving current blob paths on
  every load/save rather than caching obsolete paths.

The port uses Avalonia styles and managed publishing, so upstream WinForms
theme handling and DataGridView trimming roots do not apply.

## Verification

Run the `nmse-merge-main-macos-port` skill verifier for the Release app build,
core tests, Avalonia tests, updater tests, self-contained arm64 publish, signed
bundle, ZIP and DMG validation. `CosmosPortTests` covers native preset, multitool,
base, node-editing, search, Switch discovery and mission controls. Upstream
tests cover the shared Cosmos data and save-format changes.
