# LW Cargo Manager — Space Engineers 2 client plugin

Automatic cargo handling for Space Engineers 2, loaded with [Pulsar](https://github.com/SpaceGT/Pulsar):

1. **Unload docks** — dock a mining ship at a connector named `[Unload]` and its ore moves to the mother grid.
2. **Name-tag sorting** — name containers `[Ore]`, `[Parts P1]`, `[Ammo]`, `[Player]`... and items go to them.
   Single items too: `[Si]`, `[Iron Ore]`, `[Rifle]`.
3. **Tidy** — the same item is packed into as few same-tag containers as possible (stack merging is experimental).
4. **LCD screens** — name an LCD `[Cargo]` for fill bars, `[Cargo Ore]` for an ore list with the last delivery.

> Status: **0.3.0, early test version.** Unload docks, sorting (categories and item tags), production output
> collection and LCD screens are **confirmed working in game** (single player). Ship-to-ship docking and
> multiplayer are not tested yet. See [Docs/Status.md](Docs/Status.md).

---

## Tags (type them into block names)

Case-insensitive, one tag per bracket, anywhere in the name. Priority `P1`..`P9` inside the bracket (P1 fills first, default P5).

| Tag | On | Meaning |
|---|---|---|
| `[Unload]` | Connector | A ship locked here gets its **ore and ice** moved to this grid |
| `[Unload All]` | Connector | Moves everything except Tools, Consumables, Ammo, Cells (configurable) |
| `[NoUnload]` | Connector | Never unload here (e.g. a refuel dock) |
| `[Ore]` `[Ice]` `[Parts]` `[Ammo]` `[Tools]` `[Consumables]` `[Cells]` `[Misc]` | Cargo container | Destination for that category, e.g. `[Ore P1]`. `[Parts]` = ingots **and** components (`[Components]`, `[Ingots]` are aliases). `[Cells]` = Power Modules (battery cells, also `[Batteries]`). `[Consumables]` = suit batteries, jetpack fuel, O2 recharges, medkits (old name `[Bottles]` works) |
| `[Player]` | Cargo container / locker | Group: Tools (hand weapons and tools) + Consumables. Not ammo |
| `[Si]` `[Silicon]` `[Iron Ore]` `[Rifle]` `[Gatling]` ... | Cargo container | **Item tag**: only that item, wins over category tags. Element symbols work (Fe, Ni, Si, Co, Mg, Ag, Au, Pt, U, Cu). A category as last word limits it: `[Si Ore]`, `[Iron Parts]` |
| `[Lock]` (alias `[NoSort]`) | Any inventory | The plugin never touches it |
| `[NoTidy]` | Container | Sorted, but not consolidated or merged |
| `[Cargo]` | LCD / cockpit | Fill bars for every category of this grid + dock status |
| `[Cargo Ore]` `[Cargo Parts]` `[Cargo Ice]` ... | LCD / cockpit | Item list for one category, biggest first, with the last delivery |
| `[Cargo 54]` `[Cargo Ore 54]` | LCD | Same, with this LCD's width in characters (overrides the setting) |
| `[Cargo Alerts]` | LCD / cockpit | Full storage, problems, recent plugin messages |
| `[Cargo Ruler]` | LCD | Test pattern: read how many characters and lines fit at your font size |

Examples: `Large Cargo 3 [Ore P1]`, `Ammo Locker [Ammo]`, `Locker [Player] [Rifle]`, `Silicon [Si]`, `Dock A [Unload]`, `Overflow [Misc P9]`.

Item tags: Silicon in an `[Ore]` container moves to a `[Si]` container while it has room. Any other bracket
word is read as an item tag, so `Cargo [Main]` matches nothing and is emptied like an untagged box.

The **docked ship needs no tags.** A grid is sorted only if it has at least one tagged container.

`[Parts]` is for SE2 "material" and "component" items (smelters make components directly). The old names
`[Components]`, `[Ingots]`, `[Materials]` still work, so existing container names don't need renaming.

### LCD screens
**Auto-style** (on by default): when an LCD gets a `[Cargo ...]` tag or its name changes, the plugin sets the font
size (setting, default 16), top-left alignment and a monospace font. Set **RobotoMono on one LCD by hand once**;
the plugin copies that font to the others (the game has no font lookup by name).

Each `[Cargo ...]` tag goes to the next screen of the block, so a cockpit named `Cockpit [Cargo] [Cargo Ore]`
shows the summary on screen 1 and ore on screen 2.

```
LW CARGO - Mining Base                 12:41
Ore        ######....  60%  30.0 t / 50.0 t (3)
Parts      ####......  40%  16.0 t / 40.0 t (2)
Ammo       ########## FULL  12.0 t / 20.0 t (1)
Total 58.0 t / 110.0 t (53%)
Dock A: unloading
Last delivery 4m ago (Dock A)
```
100% = the max mass of all containers with that tag (not `[Lock]`). FULL = no container in the group
can take more (mass or stack slots). Items waiting in untagged boxes show on the `Untagged` line.
Bars use `#` and `.` because the LCD font has no block characters. Narrow screens (under 46 characters) drop the
`/ max` part; every line is cut to the width, so text never wraps.

```
ORE - Mining Base
########..  82%  41.0 t / 50.0 t (3)
Iron              24.3 t  +3.1 t
Nickel             8.1 t  +0.9 t
Silicon            5.6 t
Stone              3.0 t  +1.2 t
Last delivery: 5.2 t, 4m ago (Dock A)
```
The list counts **all** storage on the grid (also untagged and `[Lock]` boxes).
The `+` column is what the **last delivery** brought, shown for 60 minutes (setting). One delivery = everything
unloaded from one dock without a pause longer than 30 s. A per-minute rate column exists but is off by default.

`[Cargo Ruler]` line 1 is a column ruler (`-` = 1, `+` = 5, digit = tens: `----+----1----+----2`); the other lines start
with their number. Read the last visible column and line, then put the width in the LCD name (`[Cargo Ore 54]`)
or set **LCD width** in the settings. At font size 16 a big square LCD fits about 54 characters, a wide LCD about 100.
Ore and ingots are listed in kg/t, components in pieces.

### Which way items go when both connectors are tagged
1. A station (static grid) receives.
2. Two ships: the heavier one receives.
3. Still a tie: nothing moves, a message tells you.

A station never feeds a ship automatically.

### Safety rules
- Items only move through **conveyors** (no teleporting).
- Turrets, reactors, generators, welders and **unknown blocks are never touched**.
- Refinery/assembler **inputs are never touched**.
- Items you put somewhere yourself stay there for the **grace delay** (60 s).
- Every move is checked: if an item count doesn't add up, the plugin **stops itself** and logs why.
- Works in **single player and self-hosted** worlds. Remote/dedicated servers: not yet (plugin stays idle).
- Don't run another sorting plugin (e.g. AutoSorter) on the same world.

---

## Developer setup

### Prerequisites
- Space Engineers 2 (Steam)
- [Pulsar](https://github.com/SpaceGT/Pulsar) for SE2
- .NET 10 SDK
- Visual Studio 2022+ or JetBrains Rider

### 1. Point the build at your game
Copy `Directory.Build.props.user.example` to `Directory.Build.props.user` (not committed) and set:
- `Game2` — the `Game2` folder of your SE2 install, e.g. `C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers2\Game2`.
  Leave empty to auto-detect the default Steam install.
- `Pulsar` — optional, e.g. `$(APPDATA)\Pulsar`. If set, every build copies the DLL to `<Pulsar>/Modern/Local`.

### 2. Build
```
dotnet build LwCargoManager.sln -c Debug
```
or open `LwCargoManager.sln` in Visual Studio / Rider.

### 3. Run the unit tests (no game needed)
```
dotnet run --project tests -c Release
```

### 4. Load it in game with a Pulsar development folder
2. Start the game with Pulsar's `Modern.exe` (Windows) / `Modern.bin` (Linux) and the `-sources` option.
3. In Pulsar click **Sources**, add this folder as a development folder and load `LwCargoManager.xml`.
4. Enable the plugin, save it in a profile, start the game.
5. **Debug** build: attach the debugger to the game process for breakpoints. **Release**: test what players get.

Run configurations for Rider are in `.run/`.

### 5. First run checklist (M0 → M1)
1. Turn on **Dry run** and **Verbose log** in the plugin settings (Pulsar → plugin → settings button).
2. Load a world. Check the game log for `[LwCargoManager] Loaded`, `Patched SessionUpdatePatch`, `Input host captured`, `Scan: grids=...`.
3. Press **Write discovery report**. Open `discovery.txt` in the plugin data folder and fill in the open items in [Docs/Status.md](Docs/Status.md):
   connector members, block kind GUIDs (connector, refinery, assembler, drill, grinder), `ItemTypes` names, grid physics mass.
4. Put the GUIDs into **Block role overrides** (`GUID=Storage; GUID=Producer`) or into `BlockRoles.cs`.
5. Turn Dry run off on a **test world** first.

If the plugin stops itself, the log says why. Fix it, then press **Reset error stop** or reload the world.

---

## Project layout

```
ClientPlugin/
  Plugin.cs              entry point: loads config, applies Harmony patches one by one
  Config.cs              settings + in-game settings screen (attributes)
  Policy/                pure rules, NO game types — also compiled into tests/
    Tags.cs              tag parser ([Ore P1], [Unload All], [Lock] ...)
    Category.cs          categories, roles, modes
    ItemClassifier.cs    item traits → one category
    BlockRoles.cs        block kind GUID → Storage / Producer / Consumer (unknown = Consumer)
    DockRules.cs         which side of a locked connector pair receives
    Routing.cs           sources, destinations, priority, overflow, unload filter
    GraceTracker.cs      "player put this here" delay
    Lcd.cs               cargo summaries, LCD text layout, rate tracker, alert log
  Game/                  game-side code (SE2 API)
    CargoEngine.cs       main loop from Session.Update: checks, budget, fail-safe, jobs
    WorldScan.cs         finds blocks, grids, inventories; access check
    UnloadJob.cs         unload docks (connector API probe + reachability fallback)
    SortJob.cs           name-tag sorting
    TidyJob.cs           consolidate (+ experimental merge)
    TransferService.cs   the ONLY code that moves items (safe TransferByIdx + checks)
    ItemTraitsMapper.cs  game ItemDefinition → ItemTraits
    ConnectorAdapter.cs  connector lock/partner probe, grid mass probe (TODO: replace with real API)
    Discovery.cs         writes discovery.txt for API research
    LcdJob.cs            [Cargo ...] screens (only rewrites when the text changed)
    LcdAdapter.cs        LCD text surface probe + item name probe (TODO: replace with real API)
    PluginLog.cs         log + HUD message helper (also feeds [Cargo Alerts])
  Patches/GamePatches.cs Harmony patches: Session.Update, InputGameComponent.Init, TransferByIdx, quick deposit
  Settings/, Tools/      from the CometWorks client plugin template
tests/                   dependency-free unit tests for Policy/
Docs/Status.md           implementation status and TODO list
```

## Reporting bugs
Open an issue on GitHub with: what you did, what happened, the `[LwCargoManager]` lines from the game log,
and your plugin settings. Include `discovery.txt` if the problem is about blocks or items.

## Credits
- Based on the [CometWorks SE2 client plugin template](https://github.com/CometWorks/client2-plugin-template) (MIT, Viktor Ferenczi).
  See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## License
MIT — see [LICENSE](LICENSE).
