# Native macOS parity matrix

This is the acceptance ledger for the Avalonia frontend. Check a row only after its
behavior matches current main, its write-back path is verified, and it has no placeholder
or no-op command. `MultiplayerPanel` is not a top-level area and must not be newly exposed.

Status keys: `[ ]` not verified, `[~]` implemented but not parity-verified, `[x]` verified.

## Shell, menus, shortcuts, and dialogs

| Status | Surface | Required parity |
|---|---|---|
| [~] | File menu | Open Save Directory (`Cmd/Ctrl+O`), Load Save File (`Cmd/Ctrl+L`), Save (`Cmd/Ctrl+S`), Save As (`Cmd/Ctrl+Shift+S`), Exit (`Cmd+Q` on macOS; Alt+F4 elsewhere). |
| [~] | Edit menu | Reload (`F5`), Restore Backup (All), Restore Backup (Single). |
| [~] | Tools menu | Export JSON, Import JSON, Recharge All Technology, Refill All Stacks, Repair All Slots, Repair All Technology. |
| [~] | Language menu | `en-GB`, `fr-FR`, `it-IT`, `de-DE`, `es-ES`, `ru-RU`, `pl-PL`, `nl-NL`, `pt-PT`, `es-419`, `pt-BR`, `zh-CN`, `zh-TW`, `ko-KR`, `ja-JP`, `en-US`; database and visible-control refresh; fallback and persisted selection. |
| [~] | Theme and Settings | System, Light, Dark; System first-run default; native Settings window, live resource refresh and persistence. |
| [~] | Help menu | GitHub Page, User Guide, Sponsor Development, Check for Updates, Release Notes, About. External URLs use the desktop service. |
| [~] | Toolbar | Directory/recent-directory selector and Browse; Backup selector and Browse; Save Slot selector; File selector; Load and Save buttons. |
| [~] | Status/loading | Startup splash, database/icon progress, operation progress, status text, item count, update notification, crash reporting. |
| [~] | Window state | Restore bounds safely, persist bounds, native menu integration, focused-control commit, dirty-title state, dirty close/open/reload confirmation. |
| [~] | File dialogs | Directory, backup directory, save-file open, Save As, JSON import/export, node import/export, and every panel import/export below use native `StorageProvider`. |
| [~] | Message dialogs | Information/success/error, destructive confirmation, consistency result, Go To JSON confirmation, backup selection/restore, unsaved changes, update availability/install fallback. |

Keyboard behavior to preserve: Enter commits numeric/text editors where current main does;
Escape cancels dialogs/edits; Delete acts only on the selected editable entity; Raw JSON
keeps undo/redo, find/find-next, copy/paste, and tree editing keys; inventory duplicate-drag
uses Command on macOS and Ctrl elsewhere.

## Top-level tabs and field/action checklist

| # | Status | Tab and sub-tabs | Fields and actions that must round-trip |
|---:|:---:|---|---|
| 0 | [~] | **Player**: General, Guides, Titles | Identity/save metadata; health, shield, energy; units/nanites/quicksilver and Max actions; camera/portal/warp flags; game mode, play time, save timestamp; slot copy/move/swap/delete and platform transfer; current/base coordinates, galaxy, portal glyph/code conversions, apply/roulette and trigger battle; outfit selector and outfit export/import/copy; guide category grids with Seen/Unlocked; title grid and unlock state. The dormant experimental Multiplayer editor remains unexposed. |
| 1 | [~] | **Exosuit**: Cargo, Technology | Inventory dimensions and cells; export/import; inventory sort/filter/actions; Go To JSON; cross-inventory transfer completion. |
| 2 | [~] | **Multi-tools** | Tool selector/count; name, seed/generate, type, class, damage/mining/scan stats; add/copy/delete; active tool; technology inventory; current-format export/import; archive export/import and confirmations; Go To JSON. |
| 3 | [~] | **Starships**: Details, Cargo, Technology, Customisation | Ship selector/count; name, seed/generate, type/class, active/primary state; damage/shield/hyperdrive/maneuverability stats; add/copy/delete/swap; cargo/tech/overflow grids and resizing; supercharged slots; corvette part lookup; ship and archive import/export; customisation parts/colors; Go To JSON and transfer completion. |
| 4 | [~] | **Fleet**: Freighter, Frigates, Squadron | Container deferred load/save and Go To JSON propagation. Freighter: name/seed/type/class/race/base stats, cargo/tech, freighter rooms, backup/restore base. Frigates: selector/count, name/type/class/race/seed, traits, mission/damage/fuel/stats, copy/delete/repair/level-up/finish expedition/import/export. Squadron: selector, pilot/ship seeds, ship type, pilot race/rank/stats, add/delete/import/export. |
| 5 | [~] | **Exocraft**: Exocrafts (Cargo, Technology), Summoning Stations | Vehicle selector/name, third-person camera and Minotaur AI flags, fixed-supercharge note, cargo/tech inventory, vehicle import/export and undeploy. Stations: individual/base station lists; name/timestamp/base/galactic address/region seed/position; galaxy/portal code decimal/signal booster/voxel/system/planet displays; delete station/base-station confirmations; Go To JSON. |
| 6 | [~] | **Companions**: Stats, Battle | Companion selector/count; species/biome/gender/name, seeds and generate, age/height/weight/scale, traits/personality/behavior, predator/fur/unlocked/summoned/egg flags, universal address hex mode, descriptors/parts, accessory eligibility/colors/scales/reset; delete/import/export; induce/make hatchable/place egg in Exosuit; creature-builder link; battle class overrides, health/agility/combat classes, victories, genetic progress/available edits, treat stats/affinity/strength/weakness, move list/cooldown/score; all five Go To JSON destinations. |
| 7 | [~] | **Bases & Storage**: Bases, Chests, Storage | Base selector/name/type/owner/galaxy/address/position/forward/up vectors/timestamp; summon/move/delete base and base computer; base import/export and overwrite confirmation; base part counts; ten chest inventories; named special containers and freighter refund storage; inventory actions and Go To JSON. |
| 8 | [~] | **Catalogue**: Known Technologies, Known Products, Specials, Known Words, Known Glyphs, Known Locations, Known Fish, Recipes, Known Recipes, Recipe Info | Known/unlocked grids and select/clear-all operations; word groups/races; 16 glyphs; location/fish state; filters/search; recipe type/search, ingredients/result/time/type, producer/consumer navigation and web link; localization refresh; deferred load and purge-on-leave; Go To JSON. |
| 9 | [~] | **Milestones**: Main Stats, Other Stats | Journey milestones and guild standing; all named global/stat rows, constrained numeric editing, section icons, reset/max where present, and per-section Go To JSON. |
| 10 | [~] | **Settlements**: Stats & Perks, Production, Building States, Building Editor | Settlement selector/name/seed/mission seed/race/decision type/population colors; population/happiness/productivity/maintenance/sentinel alert and remaining stats; perk IDs/seeds/beneficial state; production item/amount/time grids; building-state cards; selected building class/state/init/upgrade/tier/flags; delete/import/export and list/selected Go To JSON. |
| 11 | [~] | **ByteBeats** | Song selector/count, name/author username/online ID/platform, data fields, shuffle and autoplay-on-foot/ship/vehicle, delete/import/export and Go To JSON. |
| 12 | [~] | **Account Rewards**: Season Rewards, Twitch Rewards, Platform Rewards | Account-data status/warning and separate save; consistency dialog; each reward grid and Filter/Clear, Unlock All, Lock All, Redeem All, Remove All; Windows platform-MXML sync plus native macOS `GcUserSettingsData.mxml` account loading/safe atomic sync for rewards, guides and titles. |
| 13 | [~] | **Export Settings**: File Extensions, Naming Templates, Help | Every configured artifact extension; every naming template/token preview; Save Settings, Reset to Defaults, validation/status/help text; config stored separately from save data. |
| 14 | [~] | **Raw JSON Editor**: Tree, Text, Split | Account/save source selector; full import/export; Show Changes/diff; search/back/find/clear; breadcrumb; Tree/Text/Split; Expand/Stop/Collapse; Format/Validate; syntax highlighting; live split synchronization; undo/redo; node edit/add/delete/reorder/drag-drop; clipboard; navigation from every Go To JSON source. |

## Shared inventory control

Every inventory instance must cover the same reusable matrix:

- [~] Cell layers: square geometry, icon, marquee name, amount, class, element,
  charge/damage, active and supercharged state, adjacency colors/borders.
- [~] Empty/occupied slot selection, multi-select item picker, procedural seed, amount,
  charge, damage, index/position, enabled/supercharged editing and validation.
- [~] Resize rows/columns with destructive confirmation and inventory-specific limits.
- [~] Context/toolbar actions: add, apply changes, remove, copy, paste, duplicate, move,
  enable/disable, supercharge toggle, recharge/refill/repair, sort, autostack, pin/lookup,
  Corvette lookup, import and export.
- [~] Pointer behavior: single/double/right click, drag move, Command/Ctrl-drag duplicate,
  cross-inventory autostack/transfer, hover/tooltip, scroll and marquee clipping.
- [~] Direct JSON mutation, `DataModified`, destination refresh, localization/theme refresh,
  bitmap caching/downscaling/disposal, and no leaked image handles.

## Raw JSON context menu and shortcuts

- [~] Context menu: Edit Value; Add Property; Add Array Item; Delete; Copy Key; Copy
  Value; Copy Path; Export Node; Import Node. Visibility/enabled state follows node type.
- [~] Tree double-click edit, label edit, lazy expand, selection/breadcrumb, item drag,
  drag-over/drop reorder, array index rewrite, and confirmation for destructive changes.
- [~] Text/split copy, cut, paste, select all, undo, redo, search next/previous, validation,
  formatting, syntax colors, dirty state, and safe mode switching with parse errors.

## Cross-panel and coordinator invariants

- [~] All save-backed panels expose/load/save/localize consistently and raise
  `DataModified`; applicable panels expose Go To JSON.
- [~] Exosuit transfer refreshes loaded Starships, Fleet/Freighter, and Bases/Chests.
- [~] Starship transfer refreshes loaded Fleet/Freighter and Bases/Chests.
- [~] Companion Place Egg mutates Exosuit cargo, marks dirty, and refreshes loaded Exosuit.
- [~] Language switching refreshes all databases plus Account reward names, Frigate traits,
  Settlement perks, Recipe/Catalogue content, and every visible caption.
- [~] Load order is resources/config/localization, background parse, context transforms,
  `SaveContext`, account data, Raw JSON baseline, active panel, then deferred panels.
- [~] Catalogue saves and purges when left, then reloads on return.
- [~] Save commits focus, saves only loaded panels, creates backups, uses the correct
  platform writer/meta handling, saves account data separately, then clears dirty state.
- [~] Steam/GOG, Xbox, PS4/PS5, and Switch slot discovery/load/save remain byte-safe.

## Packaging and final acceptance

- [x] `osx-arm64` and `osx-x64` self-contained app bundles launch on macOS 14+ without
  .NET, Wine, Whisky, or CrossOver.
- [x] Bundle ID is `com.vectorcmdr.nmse`; resources live under `Contents/Resources`;
  config/backups live under Application Support and logs under Library/Logs.
- [x] ZIP/DMG names identify architecture; archive paths, architecture, plist, resources,
  code signature, Gatekeeper/notarization where configured, and DMG integrity validate.
- [x] Update selection chooses OS and process architecture; validates bundle/version/code
  signature; external helper performs atomic replace/relaunch/rollback; non-writable or
  failed validation opens the matching DMG/release page without modifying the app.
- [ ] A clean-account edit/save/backup/reload pass succeeds on both architecture artifacts.
