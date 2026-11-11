# LW Cargo Manager

**Automatic cargo handling for Space Engineers 2.** Dock a mining ship and its ore goes to your base.
Name your containers and items sort themselves. Put a cargo screen on any LCD.

A client plugin for [Pulsar](https://github.com/SpaceGT/Pulsar). No scripts and no programmable blocks.
Everything works through block names.

![Version](https://img.shields.io/badge/version-0.1.1.1-blue) ![Game](https://img.shields.io/badge/Space%20Engineers%202-client%20plugin-orange) ![License](https://img.shields.io/badge/license-MIT-green)

> **Early test version.** Unload docks, sorting, production output collection, LCD, power, gas, damage and ammo
> screens and ammo production are confirmed working in single player. Multiplayer is not tested yet (it isn't
> released in Space Engineers 2). Try it on a copy of your world first.
>
> **New here? Read the [User Manual](Docs/UserManual.md)**: a step-by-step setup with every tag and screen.

---

## Features

| | |
|---|---|
| **Unload docks** | Name a station connector `[Unload]`. A ship that locks onto it has its ore and ice moved into your base within seconds. The ship needs no setup. |
| **Tag sorting** | Name containers `[Ore]`, `[Parts]`, `[Ammo]`, `[Player]` … and items move there through your conveyors. |
| **Item tags** | `[Si]`, `[Iron Ore]`, `[Rifle]`: one container for a single item. More specific tags win. |
| **Production output** | Finished items are taken out of smelters, refineries and assemblers. Their inputs are never touched. |
| **LCD screens** | Fill bars per category, item lists, the last delivery and alerts. The LCD style is set for you. |
| **Status screens** | Power (with full-load planning), hydrogen/oxygen, damaged blocks, weapon ammo. |
| **Weapon ammo** | Keeps ammo for turrets, guns and rocket launchers in stock by queuing it in a `[Make Ammo]` Gearforge. |
| **Conveyor check** | `[Alerts]` names tagged blocks that are not connected by conveyors, the most common "why doesn't it sort?". |
| **Safe by design** | Items only move through conveyors. Turrets, reactors and unknown blocks are never touched. Every move is checked. |

---

## Quick start

1. Install the plugin (see [Installation](#installation)) and start your world.
2. Rename a connector on your station: `Dock A [Unload]`.
3. Rename your containers: `Cargo 1 [Ore]`, `Cargo 2 [Parts]`, `Locker [Player]`.
4. Rename an LCD: `Base [Cargo]`.
5. Dock a mining ship at `Dock A`. Its ore moves to `[Ore]` and the LCD shows the delivery.

That's all. Tags are not case-sensitive, so `[ore]`, `[Ore]` and `[ORE]` are the same.

---

## Tags

Put a tag anywhere in a block name, one tag per bracket. You can combine them: `Big Cargo [Ore P1] [NoTidy]`.

### Docks (connectors)

| Tag | What it does |
|---|---|
| `[Unload]` | A docked ship's **ore and ice** move to this grid. |
| `[Unload All]` | Everything moves except what the ship needs (Tools, Consumables, Ammo, Cells; configurable). |
| `[NoUnload]` | Never unload here, for example on a refuel dock. |

Which way items go: a **station always receives**. Between two ships, the bigger one receives.
On a tie nothing moves. A station never fills a ship automatically.

### Storage (containers, lockers)

| Tag | Collects |
|---|---|
| `[Ore]` | All ores (except ice). |
| `[Ice]` | Ice. |
| `[Parts]` | Everything made in your production blocks: refined metals, plates, frames, modules … |
| `[Ammo]` | All ammunition. |
| `[Tools]` | Hand tools and hand weapons. |
| `[Consumables]` | Medkits, O2 recharges, jetpack fuel (H2), suit batteries. |
| `[Player]` | Tools + Consumables: everything a character carries, **except ammo**. |
| `[Cells]` | Battery Power Modules. |
| `[Misc]` | Anything without a home. A good overflow box. |

**Priority:** add `P1` … `P9`, for example `[Ore P1]`. P1 fills first and the default is P5.
When all tagged containers are full, empty untagged containers are used as overflow.

### Item tags

Any other word in brackets means a single item. Item tags beat category tags, so Silicon in an `[Ore]` box moves
to a `[Si]` box while it has room.

| Tag | Collects |
|---|---|
| `[Si]` or `[Silicon]` | Every item named Silicon … |
| `[Si Ore]`, `[Iron Parts]` | Only that category (the last word is a category). |
| `[Iron Ore]`, `[Steel Plate]` | Exactly that item. |
| `[Rifle]`, `[Gatling]`, `[Missile]` | Rifle magazines, Gatling ammo, rockets. |

Element symbols work: Fe, Ni, Si, Co, Mg, Ag, Au, Pt, U, Cu.
Names are the ones shown on the `[Cargo …]` LCD screens.

> A container with an unknown word like `Cargo [Main]` matches no item, so it is emptied like an untagged box.

### Weapon ammo

Turrets, fixed guns and rocket launchers reload by themselves from any inventory they reach through conveyors;
the plugin keeps the **stock** up:
- Target per ammo type = every weapon using it reloaded once **+ 5 minutes** of all of them firing nonstop
  (setting **Ammo reserve (minutes of fire)**). The fire rate, ammo per shot and rounds per item come from the
  game's definitions, so modded weapons work too. If a weapon gives no rate, the fallback is 2 extra reloads.
- Name a Gearforge `Gearforge [Make Ammo]`: when stock is below target and that Gearforge is **idle**, the plugin
  queues up to 25 recipes (setting **Ammo batch**, 1–100). It never orders more than the stock is missing.
  With two or more `[Make Ammo]` Gearforges, different ammo types are made in parallel.
- **Order ammo without materials** (on by default): the order goes in even when parts are missing, and the game
  itself asks connected Assemblers/Fabricators for them. You still need those blocks and ore or parts on the grid. Turn it off
  to queue only what the parts on the grid allow.
- **Ammo comes first.** In a fight, staying alive matters more than the rest of the production: a big ammo order
  keeps the Assemblers busy with ammo parts for a while, and your other queues continue after that.
- Finished ammo goes to `[Ammo]` (or `[Gatling]`, `[Missile]` …) like any production output.
- Untagged Gearforges are never touched, so your own queues stay yours. Switch it off with **Auto ammo production**.

### Control

| Tag | What it does |
|---|---|
| `[Lock]` (or `[NoSort]`) | The plugin never touches this inventory. |
| `[Make Ammo]` | On a Gearforge: may queue weapon ammo there (see Weapon ammo). |
| `[NoTidy]` | Sorted into, but not consolidated. |

### Examples

```
Ore Silo 1 [Ore P1]           first choice for ore
Ore Silo 2 [Ore P2]           when silo 1 is full
Silicon Box [Si]              silicon only, taken out of the ore silos
Airlock Locker [Player] [Rifle]   gear, medkits, O2/H2 and rifle magazines
Turret Supply [Gatling] [Missile] turret ammo only
Overflow [Misc P9]            anything else, last
Dock A [Unload]               mining ship dock
Dock B [Unload All]           cargo ship dock
Personal Box [Lock]           hands off
```

---

## LCD screens

Name an LCD (or a cockpit with screens) with one of these tags:

| Tag | Shows |
|---|---|
| `[Cargo]` | A fill bar for each category on this grid, the dock status and the last delivery. |
| `[Cargo Ore]`, `[Cargo Parts]`, `[Cargo Ice]` … | Every item of that category, biggest first, with what the last delivery brought. |
| `[Ice]` / `[Cargo Ice]` | Ice stock plus hydrogen and oxygen tank fill (`Hydrogen ########.. 81% (12)`, `LOW` under 25 %). |
| `[Cargo Alerts]` (or `[Alerts]`) | Warnings: full storage (`! Ore storage full`), tagged blocks with **no conveyor link** to the rest (`! Cargo 7 [Ore]: no conveyor link`), plugin problems, the last plugin messages (unloads, full mother grid). `All good` when nothing is wrong. |
| `[Cargo Ruler]` | A test pattern that shows how many characters and lines fit. |
| `[Power]` | Generation, use, net, battery charge and time until full or empty. |
| `[Power Sources]` | Reactors, solar, wind and batteries, grouped (`Solar Panel 5 m x25`), on/off state. |
| `[Power Consumers]` (or `[Power Users]`) | Every consumer, biggest draw first, then idle ones (thrusters not firing, machines off). |
| `[Damage]` | Block count, damaged and unfinished blocks, worst first. |
| `[Turrets]` | Per ammo type: weapon count, use per minute at full fire, stock vs target, what the `[Make Ammo]` Gearforge is doing. |

All screens take a width: `[Power 54]`, `[Damage 40]`. Generation is computed as use + battery charging.

**Full-load planning** (on `[Power]` and the ice screen): the game reports every consumer's max draw and every
producer's max output, so the plugin adds up "everything running at once" and compares it with what you can make now
(reactors/engines/O2-H2 generators at max, solar and wind as the sun and wind allow):
```
Peak  use 254.0 MW max / can 115.0 MW
      SHORT 139.0 MW at full load
      batteries cover 20m
      add 2x Reactor 7.5 m (80.0 MW)
H2    use 3.0k max / can 1.0k
      SHORT 2.0k at full load
      add 2x O2/H2 Generator 5 m (1.0k)
```
The suggested block is the biggest steady producer already on the grid, so modded reactors and generators count.

```
LW CARGO - Mining Base             12:41      ORE - Mining Base
Ore        ######....  60%  30.0 t (3)        ########..  82%  41.0 t (3)
Parts      ####......  40%  16.0 t (2)        Copper Ore        28.7 t  +28.7 t
Player     ##........  15%   0.3 t (1)        Silicon Ore        4.3 t
Total 46.3 t (48%)                            Nickel Ore         4.1 t
Last delivery 4m ago (Dock A)                 Last delivery: 28.7 t, now (Dock A)
```

**Setting up a screen:**
1. Once per world, set the font to **RobotoMono** on any one LCD by hand. The plugin copies that font to
   every tagged LCD, together with font size, alignment and padding (**Auto-style**, on by default).
2. Screens differ in size, so add the width in characters to the tag: `[Cargo Ore 54]`.
   Use `[Cargo Ruler]` to read it. At font size 16, a big square LCD fits about 54 characters and a wide LCD about 100.
3. A cockpit with several screens: `Cockpit [Cargo] [Cargo Ore]` puts the summary on screen 1 and ore on screen 2.

Ore and refined metals are shown in kg/t, other parts in pieces (the `[Parts]` screen lists the ones in t first). The ore screen drops the word "Ore" (`Titanium`), ice is just `Ice`, and ores under 1 t get **(Low)** (setting **Low ore**, 0 = off). The `+` column and the "Last delivery" line stay for
60 minutes (setting).

### Getting more on a screen

| Want | Do |
|---|---|
| **Longer names, more columns** | Put the screen's width in the tag: `[Cargo Parts 54]`. Without it the **LCD width** setting (30) is used and the right half stays empty. |
| **More lines** | Raise **LCD max item lines** in the settings (default 12, up to 40). |
| **Smaller text, more of everything** | Lower **LCD font size** (default 16). Then measure again with `[Cargo Ruler]`. |
| **A list longer than one screen** | Add pages: `[Cargo Parts]` on the first LCD, `[Cargo Parts 2]` on the next, `[Cargo Parts 3]` … Each page continues where the previous one stopped. |
| **Several screens in one block** | Cockpits and multi-screen blocks take one tag per screen, in order: `Cockpit [Cargo 24] [Ore 24] [Power 24] [Damage 24]` (the 1.5 m cockpit has 4). `[Skip]` leaves a screen to the game: `Cockpit [Skip] [Ore 24]`. |

Number rule inside a tag: **one digit = page**, **two or three digits = width**. Both together: `[Cargo Parts 2 54]`.

**Short form on LCDs:** an LCD has no storage, so the word `Cargo` can be left out there: `[Parts 54]`, `[Ore]`,
`[Parts 2 54]`, `[Alerts]`. On a cargo container the same words stay storage tags. `[Cargo]` (the summary)
keeps its name.

Measured at font size 16: big square LCD ≈ 54 characters × 24 lines, wide LCD ≈ 100 characters.

Example wall:
```
LCD 1  [Cargo 54]                 summary bars
LCD 2  [Ore 54]                   all ores
LCD 3  [Parts 54]                 parts, page 1
LCD 4  [Parts 2 54]               parts, page 2
LCD 6  [Power 54]  LCD 7 [Power Sources 54]  LCD 8 [Power Users 54]  LCD 9 [Damage 54]
```

---

## Safety rules

- Items only move through **conveyors**. Nothing teleports.
- **Turrets, reactors, generators, welders and unknown blocks are never touched.**
- **Production inputs are never touched.** Only finished output is collected.
- Items you put somewhere by hand stay there for a **grace delay** (60 s).
- Every transfer is checked. If an amount doesn't add up, the plugin **stops itself** and writes the reason to the log.
- **Dry run** mode logs what would move without moving anything.
- Don't run another sorting plugin on the same world.

**Multiplayer:** single player and self-hosted worlds. On remote/dedicated servers the plugin stays idle for now.

---

## Security notes

For players and PluginHub reviewers: what this plugin does and does not do. Everything below can be checked in the source.

**It never:**
- makes network connections (no HTTP, sockets or downloads of any kind),
- starts processes, touches the registry or reads files outside the game,
- moves items without a conveyor connection, or out of turrets, reactors, batteries, production inputs or unknown blocks,
- runs on remote/dedicated servers (it stays idle there).

**It writes only:**
- `Config.xml` and, when you press the button, `discovery.txt` in its own data folder
  (`%AppData%\Pulsar\Modern\Data\LwCargoManager\`),
- items between inventories, always through the game's own `InventoryComponent.TransferByIdx` with an explicit
  target slot (`Game/TransferService.cs`, the only code that moves items). Every move is checked: if the item count
  before and after doesn't match, the plugin stops itself,
- text, font, size and alignment of LCD screens whose name contains a tag (`[Cargo …]`, `[Power]`, `[Ore]` …),
- production orders, only in Gearforges named `[Make Ammo]`, through the game's own `TryEnqueueRecipe`
  (the same call as the production screen; `Game/AmmoJob.cs`).

**Harmony patches** (`Patches/GamePatches.cs`). All of them only observe, except the quick deposit protection:
| Patch | Why |
|---|---|
| `Session.Update(bool)` postfix | Runs the plugin's work a few times per second on the game thread |
| `InputGameComponent.Init` | Captures the game host once at start |
| `InventoryComponent.TransferByIdx` | Notices items a player moves by hand (grace delay). During a quick deposit it **skips** the move of hand tools and weapons (setting **Protect quick deposit**); otherwise it changes nothing |
| `TryDepositAllNonEssentialItemsImpl` | Marks when a quick deposit runs, so the patch above knows when to protect hand tools. On a self-hosted world this runs on the host for **every** player's quick deposit, so all players keep their hand tools |

**Reflection and generated code:** game members that are not public are read through cached reflection
(`Game/Reflect.cs` and the `*Adapter.cs` / `StatusReader.cs` files): read-only, except the LCD text/style setters above.
One small method is generated at runtime (`Game/GeneratorOutput.cs`, `DynamicMethod`): it calls the game's own
`IResourceNode.GetResources` exactly like the game's terminal does (create a `Buffer.Inline4`, fill it, copy it out,
`Dispose`) to read power and gas numbers. It is needed only because that buffer type is a `ref struct`, which
reflection cannot pass. It reads; it changes nothing.

**Fail-safe:** any error in moving items stops the plugin (with the reason in the log and on `[Alerts]` screens). Errors in
LCD screens only affect that screen; sorting and unloading keep running.

## Installation

### From PluginHub (recommended, once listed)
1. Install [Pulsar](https://github.com/SpaceGT/Pulsar) for Space Engineers 2 (one-time).
2. Start the game through Pulsar, tick **LW Cargo Manager** in the plugin list and click **Save**.
3. Restart. Updates arrive automatically.

### From source (developers, or before the PluginHub listing)
1. Install [Pulsar](https://github.com/SpaceGT/Pulsar) for Space Engineers 2 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Clone this repository.
3. Start the game with Pulsar's `Modern.exe -sources`, click **Sources**, add the cloned folder as a
   development folder and load `LwCargoManager.xml`.
4. Enable **LW Cargo Manager** in the plugin list and restart.
5. Pulsar compiles the plugin from source at every game start, so after pulling updates, just restart the game.

The game log (`%AppData%\SpaceEngineers2\Temp\Logs`) shows `[LwCargoManager] Loaded LwCargoManager 0.1.1.1` when it works.

---

## Settings

Open Pulsar → LW Cargo Manager → settings. The most useful ones:

| Setting | Default | |
|---|---|---|
| Dry run | off | Log what would move without moving it. |
| Unload ice | on | Also unload ice at `[Unload]` docks. |
| `[Unload All]` keeps | Tools, Consumables, Ammo, Cells | What a ship keeps at an `[Unload All]` dock. |
| Fallback to untagged cargo | on | Unload into any cargo box if no tagged box has room. |
| Drain producer outputs | on | Collect finished items from production blocks. |
| Grace delay | 60 s | How long items you placed by hand stay put. |
| Auto-style LCDs / LCD font size | on / 16 | LCD look for tagged screens. |
| LCD width | 30 | Characters per line for LCDs without a width in the tag. |
| Show last delivery | 60 min | How long the delivery info stays on the screens. |
| Auto ammo production | on | Queue ammo in `[Make Ammo]` Gearforges. |
| Ammo reserve (minutes of fire) | 5 | Ammo kept in stock, as minutes of all weapons firing. |
| Order ammo without materials | on | Order anyway; the game asks Assemblers for the missing parts. |
| Ammo batch (recipes) | 25 | Most recipes per order. |
| Merge stacks | off | Join split stacks of the same item inside a container. |

Every setting is explained in the [User Manual](Docs/UserManual.md#settings).

Note: the world is paused while the settings screen is open, so the plugin waits until you close it.

---

## Troubleshooting

| Problem | Check |
|---|---|
| Nothing moves | Is **Dry run** off? Does the grid have at least one tagged container? Put up an `[Alerts]` LCD: it names tagged blocks that have no conveyor link. |
| An item stays where it is | The log says `sort: no destination for <item> (<category>)`. Add a container with that tag. |
| Ship doesn't unload | The connector must be **locked**, and the one on the **receiving** side must be named `[Unload]`. The log line `Dock '…' <-> '…': …` says which side receives, or why nothing happens. |
| LCD text wraps or bars look empty | Set RobotoMono once and add the width to the tag (`[Cargo 54]`). |
| Plugin stopped | The log says why. Fix it, then press **Reset error stop**, or load the world again (a newly loaded world starts without the stop). |

---

## Reporting bugs

Open an [issue](https://github.com/lwolf1965/LW-Cargo-Manager/issues) with what you did, what happened, the
`[LwCargoManager]` lines from the game log and your plugin settings. Include `discovery.txt` (settings → **Write
discovery report**) if the problem is about blocks or items.

---

## For developers

```
dotnet build LwCargoManager.sln -c Debug     # needs Directory.Build.props.user with your Game2 path
dotnet run --project tests -c Release        # unit tests, no game needed
```

Copy `Directory.Build.props.user.example` to `Directory.Build.props.user` (not committed) and set `Game2` to your
`SpaceEngineers2\Game2` folder. Optionally set `Pulsar` to copy the DLL on every build.

```
ClientPlugin/
  Policy/      pure rules, no game types: tags, routing, LCD layout (also compiled into tests/)
  Game/        SE2 side: world scan, unload/sort/tidy jobs, LCD writer, the one TransferService that moves items
  Patches/     Harmony patches: Session.Update, input init, transfer and quick deposit hooks
tests/         dependency-free unit tests for Policy/
Docs/          user manual
```

Pull requests are welcome. Please keep game types out of `Policy/` and add a test for every rule change.

---

## Credits

- Built on the [CometWorks SE2 client plugin template](https://github.com/CometWorks/client2-plugin-template) (MIT, Viktor Ferenczi).
- See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

Made by **LoneWolf2K**, [YouTube @LoneWolf2K](https://www.youtube.com/@LoneWolf2K).

## Versioning

`STAGE.FEATURE.FIX.BUILD`, where STAGE follows the game: **0.1** while Space Engineers 2 is in alpha, **0.2** in beta,
**1** after the game's release. FEATURE goes up with new features or tag changes, FIX with bug fixes.
The version is set in one place, `ClientPlugin/Plugin.cs` (`Plugin.Version`).

## License

MIT, see [LICENSE](LICENSE).
