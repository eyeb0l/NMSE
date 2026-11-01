# Forsaken 7.11 Avalonia integration

Integrated upstream `6e0fd3a3c92456ac7241e106856ca8be76b2e46b` (NMSE 1.5.12)
into the native Avalonia port. The Windows-only frontend stays excluded and deleted.

| Upstream changes | Native integration |
| --- | --- |
| Catalogue/story fixes (`1f284663`) | Extracted story indexes flow through knowledge completion and developer commentary. Product, special and technology completion uses source-table-aware sets and account seen lists. Word page counters use race ordinals. |
| 7.10/7.11 data and localisation (`3744ec45`, `6bc48982`) | Updated JSON, mappings, images and UI strings are copied into native packages. |
| Bases (`da216a82`) | Filterable multi-select list includes legacy/temporary entries; shared block moves/sorting; full raw base import/export with validated terrain sidecars; import as new and type-for-type replacement with metadata/terrain warnings. |
| Companions (`7813a8bd`) | Separate pet/egg lists, persisted locked-slot visibility, sorting/moves through shared logic, multi-delete, and explicit unlocking before editing locked pets. Accessories and battle-team references move with pets. |
| Larva Cannon (`475a21db`) | Shared multi-tool model list, localisation and type classification. |
| Milestones (`4a050e65`) | Shared catalog drives four tabs, categories/subcategories, all 241 placements of 237 stats, synchronized shared values, guild ranks and searchable reference/navigation. |
| JSON bookmarks (`c6a4ac10`) | Native add/manage/navigation controls, saved path scopes for save/account files, and isolated Split view restoration. |
| Hotkeys (`634e91a4`) | Slot labels run Ctrl+0 through Ctrl+9. |
| Tests/docs/releases (`ec57b6a4`, `fbd2654d`, `6748b041`, `2e8bad34`) | Shared tests retained and native regression coverage added; package version is 1.5.12. |
| Technology filtering (`dc540449`) | Shared source-table resolution preserved for mixed technology/product data. |
| AppImage/startup (`8601ed0c`, `e620aaae`, `6e0fd3a3`) | Native Linux launchers and shared platform matrix retained. Wine/WinForms splash changes do not apply to Avalonia's loading window. Retained metadata/icon updates and manual smoke-only release control. Assembly metadata now comes from version.json for local builds too. |

Base exports remain raw `.nmsbase` JSON; terrain is an adjacent
`<base-file>.terrain.json`. Import validates the sidecar format and galactic address
before applying either base or terrain. Importing the sidecar directly resolves its
base file. Exports remove stale sidecars, including stale sidecar symlinks, without
writing through those links.

## Verification

From the repository root:

```bash
bash /Users/irisgiertuga/.codex/skills/nmse-merge-main-macos-port/scripts/verify-macos-port.sh
./scripts/build.sh publish all --skip-tests --smoke-test
```

The second command uses tests already run by the verifier and creates Windows x64,
Linux x64/ARM64, and macOS arm64/x64 archives in `Build/artifacts/`. Native architecture,
self-contained payloads and archive integrity are checked for every target. App launch
smoke testing runs for the host macOS architecture. Other architectures need execution
on their matching hosts; producing an archive is not a cross-platform runtime test.

Local macOS signing is ad hoc unless a release signing identity is configured;
notarization is a separate configured release step. The verifier also emits Apple
Silicon ZIP/DMG packages under `Build/macos/`.
