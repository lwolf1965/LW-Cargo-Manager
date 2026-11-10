# Implementation status — 0.2.0

Legend: ✅ done and tested · 🟢 written, uses confirmed API, not run in game yet · 🟡 written on a guess, verify · ❌ TODO

The full design lives in the Claude project doc **"LW Cargo Manager — SE2 Client Plugin Design" (v0.5)**.

Implementation differs from the design doc in two small ways: hooks use **Harmony** (template default)
instead of MonoMod, and settings are **Config.xml** with the in-game settings screen instead of a JSON file.

## Rules (Policy/, unit-tested)
| Part | Status |
|---|---|
| Tag parser `[Ore P1]`, `[Unload All]`, `[NoUnload]`, `[Lock]`/`[NoSort]`, `[NoTidy]` | ✅ |
| Item traits → one category | ✅ |
| Block roles, unknown = Consumer, overrides from text | ✅ |
| Dock direction rule (8 cases from the design + extras) | ✅ |
| Destinations: priority, size, `[Misc]`, overflow to empty, untagged fallback for unload | ✅ |
| Sort sources (no loops, consumers/inputs untouched), grid opt-in, AutoCategories | ✅ |
| Unload filter (`[Unload]` = ore + ice, `[Unload All]` minus keep list) | ✅ |
| Grace tracker | ✅ |
| LCD tags `[Cargo]`, `[Cargo Ore]` ..., `[Cargo Alerts]` | ✅ |
| Capacity per category (sum of max mass, FULL by mass or slots, `[Lock]` excluded) | ✅ |
| LCD text layout: bars, mass units, counts, rates, alerts | ✅ |
| Rate tracker (+/- per minute over a 2 min window), off by default | ✅ |
| Last delivery per grid (passes merged, shown for 60 min) + `+` column + footer | ✅ |
| `[Cargo Ruler]` test pattern | ✅ |

## Game side (Game/, Patches/)
| Part | Status | Notes |
|---|---|---|
| Harmony patches applied one by one, essential failure disables plugin | 🟢 | |
| `Session.Update(bool)` postfix main loop | 🟢 | AutoSorter uses the same method (MonoMod) |
| Host capture via `InputGameComponent.Init` | 🟢 | |
| In-process server only, DCS thread, pause/save checks | 🟢 | remote servers: plugin stays idle |
| World scan: blocks, grids, static flag, names, inventories | 🟢 | |
| Access check | 🟡 | owner == local player (AutoSorter). TODO: faction/share rules (M1 #6) |
| Safe transfer (`TransferByIdx`, target slot, conservation check) | 🟢 | adapted from AutoSorter |
| Conveyor reachability | 🟢 | |
| Sort job | 🟢 | |
| Consolidate | 🟢 | |
| Unload: connector lock/partner | 🟡 | reflection probe on member names; falls back to reachability |
| Unload: reachability fallback | 🟢 | `[Unload]` tags only; ignores `[NoUnload]` on other connectors of the same grid |
| Grid mass for direction rule | 🟡 | probes `GridPhysicsComponent.Mass`; fallback = functional block count |
| Item categories ore/ingots/components/tools | 🟢 | |
| Item categories ice/ammo/bottles | 🟡 | matched by `ItemTypes` flag names; check discovery report |
| Producer outputs | ❌ | which inventory is output is unknown → all producer inventories treated as input (untouched) |
| Block kind GUIDs for connector/refinery/assembler/drill/grinder | ❌ | only cargo is known; get them from the discovery report |
| Grace delay via `TransferByIdx` postfix | 🟢 | conveyor-internal moves may also get a delay (harmless) |
| Quick-deposit tool protection | 🟢 | optional patch, skipped if the method is missing |
| HUD messages | ❌ | goes to the log for now (`Notifier.Hud`) |
| Stack merge | 🟡 | experimental, off by default; same-inventory transfer unverified |
| Order items | ❌ | not implemented |
| `FixedPoint` → double | 🟡 | `Fx.ToDouble` uses an explicit cast; fix there if it doesn't compile |
| Discovery report | 🟢 | button in settings → `discovery.txt` (now also LCD / text surface components) |
| LCD job (update every 3 s, write only on change, dock status lines, last delivery) | 🟢 | |
| LCD text writing | 🟡 | reflection probe: `WriteText`/`SetText` method or `Text` property + content mode enum. May skip replication |
| Item display names | 🟡 | probe `DisplayName`/`Name`/... on ItemDefinition, fallback `ToString()` |
| Ore/ingot amounts in kg | 🟡 | assumes `ItemStack.Amount` for ores is kg; check against the in-game inventory |

## Confirmed in game (2026-11-10, Lone's world, SE2 game assembly 4.0.0.x)
- Plugin compiles in Pulsar (dev folder), all 4 Harmony patches apply, host captured, scan runs (25 grids, 121 inventories).
- Discovery report read. Applied in 0.2.0:
  - Block kinds: cargo, connector, crate, locker = Storage; refinery, smelter, assembler, Gearforge, drill, grinder = Producer; everything else Consumer.
  - Producer output = `InventoryIndex >= 1` (single-inventory blocks: their only inventory). **Verify in game.**
  - Connector partner = `MechanicalBlockComponent.GetConnectedComponent()` (+ `IsConnected`).
  - LCD = `LcdMultiPanelComponent`: `SurfaceCount`, `SetSurfaceText(int, string)`, `SetSurfaceContent(int, LcdPanelContent)`.
  - ItemTypes: Ore, Material, Component, Item, Consumable, Datapad, Quest, BlockSchematic. Ammo / ice / cells from the item path; Power Module = battery cell.
  - Item names from the content path ("Iron Ore", "Steel Plate"); amounts × `MassPerUnit` = kg.
- Settings screen opens in game; the world is paused while it's open, so the plugin waits until it's closed.
- Sort (Dry run off): Si/Ni moved from an untagged container into `[Ore]`; Ice into `[Ice]`.
- Item tags: `[Si]` takes Silicon out of `[Ore]`.
- Unload: miner docked at `Connector [Unload]` → 28.7 t Copper Ore moved to the station `[Ore]`; delivery shown on `[Cargo Ore]` (`+28.7 t`, "Last delivery … now (Connector)").
- LCD: text, ASCII bars (`#`/`.`; the LCD font has no block characters), monospace font, `[Cargo Ruler]`.
  At font size 16: big square LCD ≈ 54 columns, wide LCD ≈ 100 → width per LCD via `[Cargo Ore 54]`.
- Units: ore amounts × MassPerUnit match the inventory screen (4.32k Si = 4.3 t).

## M1 checklist (use the discovery report)
- [x] Connector component: `IsConnected` + `GetConnectedComponent()`
- [x] Connector block kind GUID → `BlockRoles` (Storage)
- [ ] Refinery / assembler / drill / grinder GUIDs → `BlockRoles` (Producer) + find their output inventory → `WorldScan` (`producerOutputKnown`)
- [ ] Grid mass member → `GridMass`
- [x] Ammo / ice / cells: no ItemTypes flags → item path (`ItemPaths`)
- [ ] HUD notification API → `Notifier.Hud`
- [ ] Faction / share access rule → `AccessCheck.CanUse`
- [x] LCD: `LcdMultiPanelComponent.SetSurfaceText` / `SetSurfaceContent`
- [x] Item names: from the content path (DisplayName is a LocKey)
- [x] Check ore amount units (kg?) and inventory mass units on the LCD against the game's inventory screen
- [ ] Power cell items: which `ItemTypes` flag marks them (matched by name: PowerCell / Battery / Cell) → `[Cells]`
- [ ] `[Parts]` items: listed by mass now — switch to counts if SE2 counts them in pieces (`CargoSummary.IsMassCategory`)
- [ ] Client→server transfer request (remote servers) — research only

## If the first compile fails
The game-side code was type-checked against stubs, not the real DLLs. Likely spots:
`Fx.ToDouble` (FixedPoint cast), `block.DisplayName.EvaluateText()` (extension namespace),
`Session` namespace (both `Keen.VRage.Core.Game.Systems` and `Keen.Game2.Game.SessionComponents` are imported, like AutoSorter),
`InventoryComponent.GetItemDefinition`, `ItemStack.MetaData`. Fix and note it here.
