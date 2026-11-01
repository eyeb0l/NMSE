# macOS port branch audit

This audit fixes the source-of-truth rules for the native Avalonia port. It was prepared
from the `macos-port` baseline at `09ac0ec19c96a6bebeb6c8982778bbb1f9bbe4ba`, which is
identical to `main` and `origin/main` at the start of the port.

## Branch relationship

- Abandoned reference: `origin/avalonia-experimental` at
  `57c4554d250e23c92ebcd26e6c033d13288f4115`.
- Merge base with main: `4f8b61648d357852855e78194ad0a99230db76e1`.
- Reference-only commits after the merge base: 21.
- The reference side of the merge-base comparison changes 153 files with 11,370
  insertions and 31,349 deletions. Comparing its tip to current main is much noisier
  because it is hundreds of main commits behind.
- No reference commit is approved for wholesale merge or cherry-pick. Current main,
  current tests, and the [parity matrix](macos-port-parity.md) are authoritative.

## Commit disposition

| Commit(s) | Disposition | Material that may be manually reapplied |
|---|---|---|
| `f000998` | Inspect, do not cherry-pick | Avalonia application bootstrap, desktop lifetime, loading overlay, `StorageProvider` dialog shape, resource/localisation binding, font-loading pattern. |
| `8f52cf9` | Reject merge commit | Mixed upstream merge cannot be separated safely from the foundational replacement. |
| `7fdf890` | Inspect, do not cherry-pick | File-picker and import/export flow ideas, UI localisation binding. Reconcile every flow against current main. |
| `aff0f66`, `3717ed8` | Inspect final forms | `MarqueeTextBlock` behavior and item-picker multi-select interaction. The later form wins when the commits disagree. |
| `c1783e2` | Inspect, do not cherry-pick | Complete Starship/Multi-tool export interaction, selected dialog patterns, initial inventory rework. |
| `337cdc9`, `66cb852`, `09f3670`, `eaf3dae`, `3d2b176` | Inspect final visual result | Square inventory cell geometry, full-cell icon, lightning/class layering, full-width marquee below icon overlays, pointer interaction. `09f3670` and `eaf3dae` are parallel duplicates; use the resolved final behavior, not both patches. |
| `b558ff8`, `44aab09`, `6013833` | Reject merge commits | Only reconcile the resolved visual result represented by the leaf commits above. |
| `98f9fe8` | Reject | Locale replacements are stale; current main's 16-language resources remain authoritative. |
| `088f741`, `77ffe95`, `1a0674a` | Reject merge commits | Large mixed merges with no safe port boundary. |
| `902ac68` | Reject | Conflict-recovery project files are tied to the divergent branch layout. |
| `25648a8` | Reject | Repository-rule removal is unrelated to the port. |
| `57c4554` | Reject | Temporary divergent-class fixes mask incomplete behavior and must not enter the new port. |

## Explicitly rejected branch content

The reference tip removes or diverges from current core/model behavior, deletes current
tests and Catalogue functionality, disables account behavior, contains TODO/no-op UI
commands, retains x64-only assumptions, and uses Wine-era packaging. Do not reuse its
changes to Core, Config, Data, IO, Models, update selection, project files, NativeAOT
properties, locales, CI, or packaging. Do not restore deleted utilities such as current
`SaveContext`, `InventoryBulkActions`, outfit logic, number guards, or color helpers from
the abandoned layout.

## Reuse procedure

For each manually harvested idea:

1. Compare the corresponding reference control and event with current main.
2. Add the current behavior to the parity matrix before implementation.
3. Reimplement it against the current portable logic and desktop-service interfaces.
4. Run the core tests and both frontend builds before starting the next lot.
5. Mark the matrix row complete only after headless coverage and a macOS interaction pass.

This makes the abandoned branch a visual/interaction research source, never an
architectural source of truth.

## Avalonia package-set correction

The requested `12.0.5` package set cannot be restored as a coherent graph because
`Avalonia.Controls.DataGrid` was not published at `12.0.5` (and there is no standalone
12.0.5 Diagnostics package). The migration therefore uses the first coherent matching
set, `12.1.0`, for Desktop, DataGrid, Fluent theme, Headless and Headless.XUnit. This is
an explicit migration correction, not a floating or silent package upgrade; all Avalonia
references remain exact-version pins and CI opts out of telemetry.
