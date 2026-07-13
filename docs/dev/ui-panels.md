# Avalonia UI architecture

The desktop UI lives under `UI.Avalonia/` and is hosted by `MainWindow`. It is a
single tabbed Avalonia frontend targeting generic `net10.0`; the same project builds
on macOS, Windows, and Linux.

## Panel contract and coordination

Save-backed panels implement `ISaveEditorPanel`:

```csharp
void LoadData(JsonObject saveData);
void SaveData(JsonObject saveData);
void ApplyUiLocalisation();
event EventHandler? DataModified;
```

Panels that support JSON navigation also implement `IGoToJsonSource`. The
`SaveEditorCoordinator` captures the Raw JSON baseline before loading the active panel,
loads other panels lazily, commits only loaded panels, and purges Catalogue data when
the user navigates away.

## Top-level areas

`MainWindow` preserves the 15 current areas: Player, Exosuit, Multi-tools, Starships,
Fleet, Exocraft, Companions, Bases & Storage, Catalogue, Milestones, Settlements,
ByteBeats, Account Rewards, Export Settings, and Raw JSON Editor. The experimental
Multiplayer editor is not exposed.

## Shared controls and services

- `InventoryGrid` renders and edits every inventory type, including drag/copy,
  adjacency, supercharged slots, import/export, pinning, sorting, and autostack.
- `AvaloniaIconProvider` caches and downsizes bitmaps without GDI+ file handles.
- `GlyphRenderer`, `InvariantNumericTextBox`, `ItemPickerDialog`, and Raw JSON controls
  replace their former platform-specific equivalents.
- `IDesktopServices` is the boundary for native pickers, dialogs, clipboard, URLs,
  system theme, and application lifetime.

Cross-panel changes use typed events. The host refreshes loaded destination panels after
inventory transfers and companion egg placement while leaving unloaded panels deferred.
