# LW Cargo Manager — User Manual

Version 0.1 (alpha, for Space Engineers 2 alpha). Everything in this manual works by **renaming blocks**.
You never need a script, a programmable block or a timer.

**Contents**

1. [What the plugin does](#1-what-the-plugin-does)
2. [Before you start](#2-before-you-start)
3. [How tags work](#3-how-tags-work)
4. [Setup guides](#4-setup-guides)
   - [4.1 Mining base: unload dock and ore storage](#41-mining-base-unload-dock-and-ore-storage)
   - [4.2 Production: collect output from smelters, refineries and assemblers](#42-production-collect-output)
   - [4.3 Player locker](#43-player-locker)
   - [4.4 Defence: weapon ammo](#44-defence-weapon-ammo)
   - [4.5 A status wall of LCDs](#45-a-status-wall-of-lcds)
5. [Unload docks in detail](#5-unload-docks-in-detail)
6. [Sorting in detail](#6-sorting-in-detail)
7. [LCD screens](#7-lcd-screens)
8. [Status screens: power, gas, damage, weapons](#8-status-screens)
9. [Weapon ammo in detail](#9-weapon-ammo-in-detail)
10. [Alerts and the conveyor check](#10-alerts-and-the-conveyor-check)
11. [Settings](#settings)
12. [Troubleshooting and FAQ](#12-troubleshooting-and-faq)
13. [Safety: what the plugin never does](#13-safety-what-the-plugin-never-does)
14. [Tag cheat sheet](#14-tag-cheat-sheet)

---

## 1. What the plugin does

| Job | In one line |
|---|---|
| **Unload docks** | A ship that locks onto a connector named `[Unload]` gives its ore and ice to the station. |
| **Sorting** | Items move into containers named `[Ore]`, `[Parts]`, `[Ammo]`, `[Si]` … through your conveyors. |
| **Production output** | Finished items are taken out of smelters, refineries, assemblers, fabricators and Gearforges. |
| **Tidy** | The same item is packed into as few containers as possible; split stacks can be joined. |
| **LCD screens** | Cargo, ore, parts, power, gas, damage and weapon ammo on any LCD or cockpit screen. |
| **Weapon ammo** | Ammo for turrets, guns and rocket launchers is kept in stock by a `[Make Ammo]` Gearforge. |
| **Alerts** | Full storage, blocks without a conveyor link, and the last things the plugin did. |

The plugin works on **your** grids (the ones you own) and only moves items **through conveyors**, just like the game does.

---

## 2. Before you start

1. **Install** the plugin (see the [README](../README.md#installation)). In the game log you should see
   `[LwCargoManager] Loaded LwCargoManager 0.1.1.0`.
2. **Try it on a copy of your world** first. It's an early version.
3. **Set the LCD font once.** Pick any LCD, open its settings and choose the font **RobotoMono**. The plugin copies
   this font to every tagged LCD. Without a monospace font the columns on the screens don't line up.
4. **Conveyors:** every container, connector and production block you tag must be connected by conveyors to the
   rest. The `[Alerts]` screen tells you when one isn't (see [section 10](#10-alerts-and-the-conveyor-check)).

> **Restart after updates.** Pulsar builds the plugin when the game starts. A new version only works after a game restart.

---

## 3. How tags work

A tag is a word in square brackets **anywhere in the block name**:

```
Big Cargo 1 [Ore]
[Ore P1] Ore Silo
Dock A [Unload]
Base LCD [Power 54]
```

- **Not case-sensitive:** `[ore]`, `[Ore]` and `[ORE]` are the same.
- **One tag per bracket**, several brackets per block: `Locker [Player] [Rifle]`.
- **Priority** inside the bracket: `[Ore P1]` … `[Ore P9]`. P1 fills first. Without a number the priority is P5.
- **Numbers on LCD tags:** one digit is a **page**, two or three digits are a **width** in characters:
  `[Parts 2 54]` = page 2, 54 characters wide.
- **The rest of the name is yours.** The screens show the name without tags; an LCD named only with tags shows the
  grid name (for example "Black Widow").
- Changes are picked up within a few seconds. No restart needed for renaming.

---

## 4. Setup guides

### 4.1 Mining base: unload dock and ore storage

**Goal:** dock a mining ship, and its ore and ice go into the base by themselves.

1. On the station, rename the connector where miners dock: `Dock A [Unload]`.
2. Rename the containers:
   ```
   Ore Silo 1 [Ore P1]
   Ore Silo 2 [Ore P2]
   Ice Tank Room [Ice]
   ```
3. Add an LCD: `Base [Cargo 54]` and one more: `Ores [Ore 54]`.
4. Dock the mining ship and lock the connector. Within a few seconds:
   - the ore moves into `Ore Silo 1`, then `Ore Silo 2` when the first is full;
   - ice moves into `[Ice]`;
   - the `[Ore]` screen shows `+28.7 t` next to what arrived and a "Last delivery" line.

The ship needs **no tags**. Its tools, ammo, consumables and batteries stay on the ship.
The ship's **cockpit inventory is never touched**, not even at a dock: only cargo containers (and production outputs) are unloaded.

**Single items:** want silicon in its own box? Name a container `Silicon Box [Si]`. Item tags beat category tags,
so silicon goes there first.

### 4.2 Production: collect output

**Goal:** finished parts don't pile up in the machines.

1. Rename containers: `Parts 1 [Parts]`, `Parts 2 [Parts P6]`.
2. That's all. Finished items are taken out of the **output** of smelters, refineries, assemblers, fabricators and
   Gearforges and go to `[Parts]` (or `[Ammo]`, `[Tools]` … depending on the item).
3. **Inputs are never touched**, so the machines keep their ore and their queues.

If no tagged container has room, the output goes to any **untagged** cargo container (setting
**Fallback to untagged cargo**).

### 4.3 Player locker

```
Airlock Locker [Player]
```
`[Player]` collects everything a character carries: hand tools, hand weapons, medkits, O2 and H2 bottles,
suit batteries. **Not ammo:** add `[Rifle]` if you want rifle magazines there too:
```
Airlock Locker [Player] [Rifle]
```

When you put something into a container by hand, it stays there for **60 seconds** (grace delay) before the
plugin may move it. Quick deposit (Alt + right click) keeps your hand tools and weapons on you.

### 4.4 Defence: weapon ammo

**Goal:** turrets, guns and rocket launchers never run dry.

1. Rename a Gearforge: `Gearforge [Make Ammo]`. Two or more Gearforges work in parallel.
2. Have an ammo box: `Ammo Store [Ammo]` (or `[Gatling]` and `[Missile]` separately).
3. Have at least one Assembler or Fabricator connected, plus ore or parts on the grid
   (your smelters and refineries refine ore as usual).
4. Add an LCD: `Weapons [Turrets 54]`.

The plugin counts every weapon on the grid, works out how much ammo they use when all of them fire, and orders
ammo in the `[Make Ammo]` Gearforge until the stock is reached. Weapons reload by themselves from the stock through
conveyors. Details in [section 9](#9-weapon-ammo-in-detail).

### 4.5 A status wall of LCDs

At font size 16, a big square LCD fits about **54 characters**. One way to fill a wall:

```
LCD 1  [Cargo 54]            summary: fill bars per category
LCD 2  [Ore 54]              every ore
LCD 3  [Parts 54]            parts, page 1
LCD 4  [Parts 2 54]          parts, page 2
LCD 5  [Power 54]            power and full-load planning
LCD 6  [Power Sources 54]    reactors, solar, wind, batteries
LCD 7  [Power Consumers 54]  consumers, biggest first
LCD 8  [Damage 54]           damaged and unfinished blocks
LCD 9  [Turrets 54]          weapon ammo
LCD 10 [Alerts 54]           warnings
```

Not sure how wide your screen is? Name it `[Ruler]` for a moment: it shows a numbered test pattern.

---

## 5. Unload docks in detail

| Tag on a connector | What happens when a ship locks on |
|---|---|
| `[Unload]` | **Ore and ice** move to this grid. |
| `[Unload All]` | **Everything** moves, except what a ship needs: Tools, Consumables, Ammo, Cells (setting **[Unload All] keeps**). |
| `[NoUnload]` | Never unload here, even with the setting **Unload mode = All Station Connectors**. |

**Direction:**
- A **station** (static grid) always receives.
- Between two **ships**, the bigger one (more blocks) receives. On a tie nothing moves.
- The plugin never fills a ship automatically.

**Where it goes:** into the station's tagged containers with the normal sorting rules (item tags first, then
categories, then priority). If nothing has room: untagged cargo containers (setting **Fallback to untagged cargo**).

**Settings:** **Unload ice** (on), **Unload mode** (Tagged / All Station Connectors), **Unload interval** (1 s).

---

## 6. Sorting in detail

### Which grids are sorted
A grid is sorted when it has **at least one tagged container**. Production outputs are collected on every grid of yours.
A ship with no tags is left alone (except at an unload dock).

### Where an item goes, step by step
1. Blocks with `[Lock]` (or `[NoSort]`) are never touched.
2. **Item tags** first: `[Si]`, `[Steel Plate]`, `[Rifle]`. The lowest P number wins.
3. Then **category tags**: `[Ore]`, `[Parts]`, `[Ammo]` … The lowest P number wins.
4. If every matching container is full: an **empty untagged** container (setting **Overflow to empty**).
5. Nothing found: the item stays where it is, and the log says `sort: no destination for <item>`.

Items you placed by hand stay for the **grace delay** (60 s). Items already in a fitting container stay there, except that
**Consolidate** (see Tidy) moves them up to a container with a better priority while it has room.

### Categories

| Tag | Collects |
|---|---|
| `[Ore]` | All ores except ice. |
| `[Ice]` | Ice. |
| `[Parts]` | Everything made in production blocks: refined metals, plates, frames, modules … |
| `[Ammo]` | All ammunition. |
| `[Tools]` | Hand tools and hand weapons. |
| `[Consumables]` | Medkits, O2 and H2 bottles, suit batteries. |
| `[Player]` | Tools + Consumables, not ammo. |
| `[Cells]` | Battery Power Modules. |
| `[Misc]` | Anything without a home. Good as `[Misc P9]` overflow. |

### Item tags

| Tag | Collects |
|---|---|
| `[Si]`, `[Silicon]` | Every item named Silicon (the ore and the refined metal). |
| `[Si Ore]`, `[Iron Parts]` | Only that category of the item (the last word is a category). |
| `[Iron Ore]`, `[Steel Plate]` | Exactly that item. |
| `[Rifle]`, `[Gatling]`, `[Missile]` | Rifle magazines, Gatling ammo, rockets. |

Element symbols: Fe, Ni, Si, Co, Mg, Ag, Au, Pt, U, Cu. Item names are the ones on the LCD screens.

> A word the plugin doesn't know, like `Cargo [Main]`, matches **no item**, so that container is emptied like an
> untagged one. Use `[Lock]` to keep a container as it is.

### Tidy
- **Consolidate** (on): items move up to the same-tag container with the best priority (P1 before P5) until it is
  full, so the same item ends up in as few containers as possible. Example: `[Ore P1]` takes the ore out of `[Ore]`
  until it is full. Want two containers to share the load? Give them the same priority, or turn Consolidate off.
- **Merge stacks** (off): joins split stacks of the same item inside a container.
- `[NoTidy]`: the container still receives items, but is not consolidated.

### Auto categories (off by default)
With **Auto categories** on, an untagged container that holds only one kind of item (for example only ore) is used
as a destination for that kind. Tags always win.

---

## 7. LCD screens

### Setting up a screen
1. Set **RobotoMono** once on any LCD (see [section 2](#2-before-you-start)).
2. Rename the LCD with a screen tag and its width: `Base [Cargo 54]`.
3. The plugin sets the font, font size (16), alignment and padding when the tag appears (**Auto-style LCDs**).
4. Screens update every 3 seconds, and only when the text changed.

A screen shows **its own grid**: an LCD on the station shows the station, an LCD on the ship shows the ship.

### Cargo screens

| Tag | Shows |
|---|---|
| `[Cargo]` | A fill bar per category, dock status, last delivery. |
| `[Ore]` / `[Cargo Ore]` | Every ore, biggest first, `+` what the last delivery brought, `(Low)` under 1 t. |
| `[Ice]` / `[Cargo Ice]` | Ice, hydrogen and oxygen tanks, gas planning. |
| `[Parts]` / `[Cargo Parts]` | All parts: the ones counted in t first (refined metals), then the ones counted in pieces. |
| `[Cargo Ammo]`, `[Cargo Tools]`, `[Cargo Consumables]`, `[Cargo Cells]`, `[Cargo Misc]` | Items of that category. |
| `[Alerts]` / `[Cargo Alerts]` | Warnings and recent messages. |
| `[Ruler]` / `[Cargo Ruler]` | A test pattern to measure the screen. |

**Short form:** on an LCD the word `Cargo` can be left out (`[Ore 54]`). On a container the same words are
storage tags, and `[Cargo]` alone is always the summary screen.

`[Cargo 54]`:
```
LW CARGO - Mining Base                   12:41
Ore        ########..  82%  41.0 t / 50.0 t (3)
Ice        #####.....  47%  9.3 t / 20.0 t (1)
Parts      ####......  40%  16.0 t / 40.0 t (2)
Player     ##........  15%  300 kg / 2.0 t (1)
Ammo       ########## FULL  1.9 t / 2.0 t (1)
Dock A: Miner 1 docked
Last delivery 4m ago (Dock A)
```

`[Ore 54]`:
```
ORE - Mining Base                        12:41
########.. 82%  41.0 t / 50.0 t (3)
Copper                  28.7 t  +28.7 t
Iron                     6.2 t
Silicon                  4.3 t  +1.2 t
Nickel                   1.1 t
Cobalt                  640 kg (Low)
Last delivery: 29.9 t, 4m ago (Dock A)
```

The ore screen drops the word "Ore" (`Titanium`), and ice is just `Ice`.

### Getting more on a screen

| Want | Do |
|---|---|
| Use the whole width | Put the width in the tag: `[Parts 54]`. Without it the **LCD width** setting (30) is used. |
| More lines | Raise **LCD max item lines** (default 12, up to 40). |
| Smaller text | Lower **LCD font size**, then measure again with `[Ruler]`. |
| A list longer than one screen | Pages: `[Parts]` on the first LCD, `[Parts 2]` on the next, `[Parts 3]` … |
| Several screens in one block | One tag per screen, in order: `Cockpit [Cargo 24] [Ore 24] [Power 24] [Damage 24]`. |
| Leave a screen to the game | `[Skip]`: `Cockpit [Skip] [Ore 24]`. |

Measured at font size 16: big square LCD ≈ 54 characters × 24 lines, wide LCD ≈ 100 characters.

---

## 8. Status screens

All status screens take a width (`[Power 54]`) and can be written with `Status` in front (`[Status Power]`).

### `[Power]`
```
POWER - Mining Base                      12:41
Make  12.5 MW
Can   115.0 MW now / 180.0 MW max
Use   ########.. 10.1 MW
Net   +2.4 MW
Batt  #######... 72%  (2)
      14.4 MWh / 20.0 MWh
      charging 2.4 MW  full in 2h 20m
Peak  use 80.2 MW max / can 115.0 MW
      OK, 34.8 MW spare at full load
```

| Line | Meaning |
|---|---|
| Make | What is produced right now (use + battery charging). |
| Can | What the generators could give **now** (sun and wind as they are) / their **max** (nameplate). |
| Use | What is used right now. |
| Net | Plus = batteries charging, minus = draining. |
| Batt | Battery charge and the time until full or empty. |
| **Peak** | **Full-load planning:** everything running at once (thrusters full, every machine busy) against what you can make now. |

When there isn't enough power at full load, Peak says how big the gap is, how long the batteries cover it, and what to build:
```
Peak  use 254.0 MW max / can 115.0 MW
      SHORT 139.0 MW at full load
      batteries cover 20m
      add 2x Reactor 7.5 m (80.0 MW)
```
The suggested block is the biggest steady producer **already on your grid**, so modded reactors count. Solar and wind
count only with what they give right now. All numbers come from the game, so modded blocks show their real values.

### `[Power Sources]`
```
SOURCES - Mining Base                    12:41
Reactor 2.5 m          12.4 MW / 20.0 MW
Solar Panel 10 m x10   9.0 MW / 160.0 MW
Battery 2.5 m x2        72% +2.4 MW
```
Same blocks are grouped (`x10`). A generator the game reports no output for shows `no output`.

### `[Power Consumers]` (or `[Power Users]`)
```
CONSUMERS - Mining Base                  12:41
Now 10.1 MW  max 80.2 MW  (6 blocks)
Refinery 7.5 m               6.0 MW /  12.0 MW
Assembler 5 m x2             4.0 MW /   8.0 MW
Gatling Turret               100 kW /   200 kW
Ion Thruster 5 m x2            idle /  60.0 MW
```
Now / max per block, biggest first, idle blocks last. Hydrogen thrusters don't use power, so they aren't listed here.

### Gas on the ice screen (`[Ice]`)
```
Hydrogen ########..  81%  162 / 200 (2)
Oxygen   ##........  20%  20 / 100 (1)  LOW
```
Tank amounts have no unit because the game shows none. `LOW` under 25 %. Below that, the same full-load
planning as power: H2/O2 use when everything runs (hydrogen thrusters full) against what the O2/H2 generators make,
with "add Nx O2/H2 Generator" when you're short.

### `[Damage]`
```
DAMAGE - Mining Base                     12:41
Blocks 1 245  damaged 3  unfinished 1
Battery 2.5 m            #.....  23%
Connector                build  40%
Armor Block x2           ####.. 61-63%
```
Worst first. Blocks of the same type within 5 % are grouped. `build` = not finished welding.
A clean grid shows `No damage`.

### `[Turrets]`
```
TURRETS - Black Widow                    22:17
Gatling Ammo  33 weapons
  #....... 400 / 2 233 LOW
  uses 440/min all firing
  making 25 (Gearforge, parts requested: Steel Plate)
Missile  4 weapons
  ######## 1 900 / 1 804 OK
  uses 360/min all firing
```
Per ammo type: number of weapons, stock / target, how much they use at full fire, and what the Gearforge is doing.

---

## 9. Weapon ammo in detail

### What counts
Every weapon block on the grid: turrets, fixed guns, rocket launchers. Hand weapons don't count.
Weapons reload by themselves from any inventory they reach through conveyors, so the plugin never puts ammo into
a weapon. It only keeps the **stock** in your containers up.

### The target
```
target = one reload for every weapon + (use per minute at full fire × reserve minutes)
```
- **Use per minute** comes from the game: fire rate × ammo per shot ÷ rounds per ammo item. Modded weapons work.
- **Reserve minutes:** setting **Ammo reserve (minutes of fire)**, default **5**.
- Example: 33 Gatling weapons use 440 items/min. Target = 33 + 440 × 5 = **2 233**.
- If a weapon gives no fire rate, the target is one reload + 2 more reloads (setting **Ammo reserve fallback**).

### Production
- Only Gearforges named `[Make Ammo]` are used. Your other Gearforges and their queues are never touched.
- The plugin orders when the stock is below target **and the Gearforge is idle**, at most **25 recipes** at once
  (setting **Ammo batch**, 1–100), never more than the stock is missing.
- With two or more `[Make Ammo]` Gearforges, different ammo types are made in parallel.
- **Order ammo without materials** (on): the order goes in even when parts are missing. The game then asks connected
  Assemblers and Fabricators to make those parts. You need those blocks, and ore or parts on the grid. The `[Turrets]` screen shows
  `parts requested: Steel Plate`.
- With that setting off, the plugin orders only what the parts on the grid allow and shows `short: Steel Plate`.

**Ammo comes first.** In a fight, staying alive matters more than anything else on the production line. A big ammo
order keeps the Assemblers busy with ammo parts for a while; your own queues continue after that.

Finished ammo is collected from the Gearforge like any production output and goes to `[Ammo]` (or `[Gatling]`, `[Missile]`).

---

## 10. Alerts and the conveyor check

`[Alerts]` shows what needs your attention:
```
ALERTS - Mining Base                     12:41
! Cargo 7 [Ore]: no conveyor link
! Ammo storage full
-- recent --
12:37 Dock A: 29.9 t unloaded from Miner 1
```

| Alert | What to do |
|---|---|
| `<block>: no conveyor link` | The tagged block isn't connected to the grid's main conveyor network. Check conveyors, conveyor ports, and that the block is built and powered where needed. |
| `<category> storage full` | Every container with that tag is full. Add a container or raise the priority of another. |
| `Connector API unknown (fallback mode)` | The game changed. Unload still works by conveyor reach; report it. |

**How the conveyor check works:** it looks at every tagged container, tagged connector and `[Make Ammo]` Gearforge
on the grid and asks the game which of them can reach each other. The biggest connected group is the "main network";
every tagged block outside it is reported. It's checked every 30 seconds.

> Rare false alarm: the check tests with one item from your grid. If a sorter blocks exactly that item, a block
> behind it may be reported. The game log names the item used.

---

## Settings

Open them in the game: **Pulsar → LW Cargo Manager → settings**. The world is paused while the settings screen is
open, so the plugin waits until you close it.

### General
| Setting | Default | What it does |
|---|---|---|
| Enabled | on | Turns the whole plugin on or off. |
| Dry run | off | Only logs what would move. Nothing moves. LCDs still update. |
| Verbose log | off | Logs every planned and done transfer. |

### Unload docks
| Setting | Default | What it does |
|---|---|---|
| Unload mode | Tagged | Tagged: only `[Unload]` connectors. All Station Connectors: every connector on a station except `[NoUnload]`. |
| Unload ice | on | Also unload ice at `[Unload]` docks. |
| [Unload All] keeps | Tools, Consumables, Ammo, Cells | Categories a ship keeps at an `[Unload All]` dock. |
| Fallback to untagged cargo | on | Ships and production outputs unload into untagged cargo when no tagged container has room. |

### Sorting
| Setting | Default | What it does |
|---|---|---|
| Sorting | on | Sort items into tagged containers. |
| Drain producer outputs | on | Take finished items out of production blocks. Inputs are never touched. |
| Overflow to empty | on | When all tagged containers are full, use empty untagged ones. |
| Auto categories | off | An untagged container with one kind of item becomes a destination for it. |
| Grace delay (s) | 60 | Items you put somewhere by hand stay there this long. 0 = off. |
| Protect quick deposit | on | Quick deposit keeps hand tools and weapons on the character. On a self-hosted world this applies to every player's quick deposit. |
| Default priority | 5 | Priority of tags without P1…P9. |

### Tidy
| Setting | Default | What it does |
|---|---|---|
| Consolidate | on | Pack the same item into as few same-tag containers as possible. |
| Merge stacks | off | Join split stacks of the same item inside a container. |

### Weapon ammo
| Setting | Default | What it does |
|---|---|---|
| Auto ammo production | on | Order ammo in `[Make Ammo]` Gearforges. |
| Ammo reserve (minutes of fire) | 5 | Stock = one reload per weapon + this many minutes of all weapons firing. |
| Order ammo without materials | on | Order even when parts are missing; the game requests them from Assemblers. |
| Ammo reserve fallback (x reload) | 2 | Only for weapons without a fire rate: extra reloads in stock. |
| Ammo batch (recipes) | 25 | Most recipes per order. |

### LCD screens
| Setting | Default | What it does |
|---|---|---|
| LCD screens | on | Write the screens. |
| LCD update (s) | 3 | How often screens are refreshed (only changed text is written). |
| LCD width (chars) | 30 | Width for LCDs without a width in the tag. |
| Auto-style LCDs | on | Set font, size, alignment and padding on tagged LCDs. |
| LCD font size | 16 | Font size used by auto-style. |
| LCD max item lines | 12 | Item rows per page on item screens. |
| Show last delivery (min) | 60 | How long the `+` column and "Last delivery" stay. |
| Low ore (t) | 1 | Ores below this get `(Low)`. 0 = off. |
| Show rates per minute | off | `+/-` per minute next to items (ore arrives in batches, so it's off). |

### Performance
| Setting | Default | What it does |
|---|---|---|
| Unload interval (s) | 1 | How often docks are checked. Skipped when no `[Unload]` connector exists. |
| Sort interval (s) | 5 | How often sorting runs. |
| Tidy interval (s) | 10 | How often tidy and ammo production run. |
| Max transfers per pass | 64 | Upper limit of moves per pass. |
| Max item checks per pass | 256 | Upper limit of items looked at per pass. |
| Time budget (ms) | 10 | Soft time limit per pass, so the game stays smooth. |

### Advanced
| Setting | What it does |
|---|---|
| Block role overrides | For modded blocks the plugin doesn't recognise: `BlockKind GUID=Storage` (or `Producer`, `Consumer`), separated by `;`. The GUIDs are in the discovery report. |
| Write discovery report | Writes `discovery.txt` (block kinds, items, components) to the plugin data folder. Attach it to bug reports. |
| Reset error stop | Starts the plugin again after it stopped itself because of an error. |

---

## 12. Troubleshooting and FAQ

| Problem | Check |
|---|---|
| Nothing moves at all | **Enabled** on and **Dry run** off? Does the grid have a tagged container? Restarted the game after installing? |
| One container gets nothing | Put up an `[Alerts]` LCD: is it listed as `no conveyor link`? Is the tag spelled right (`[Ore]`, not `[Ores ]`)? |
| An item stays where it is | The log says `sort: no destination for <item>`. Add a container with that tag, or `[Misc]`. |
| A container is being emptied | It has no tag or an unknown word in brackets. Tag it, or use `[Lock]`. |
| Ship doesn't unload | The connector must be **locked**, and the connector on the **receiving** side (station, or the ship that should get the items) must be named `[Unload]`. Renaming while docked works. The game log has a `Dock …` line that says which side receives, or why nothing happens. |
| Item I put in moved away | The grace delay (60 s) ended. Use `[Lock]` on personal boxes. |
| LCD columns don't line up | Set **RobotoMono** once on any LCD; the plugin copies it. |
| LCD text cut with `~` | The line is longer than the width. Add the right width (`[Ore 54]`); measure with `[Ruler]`. |
| Half the LCD stays empty | Add the width to the tag. Without it 30 characters are used. |
| No ammo is made | Gearforge named `[Make Ammo]`? An Assembler/Fabricator connected, with ore or parts on the grid? **Auto ammo production** on? Look at the `[Turrets]` screen. |
| Ammo is made slowly | Raise **Ammo batch**, add another `[Make Ammo]` Gearforge, add Assemblers for the parts. |
| A modded block is ignored | Write the discovery report and add a **Block role override**, or open an issue with the report. |
| "Plugin stopped" | The log says why. Fix it, then **Reset error stop**, or load the world again (a newly loaded world starts without the stop). |
| Does it work in multiplayer? | Not tested yet (SE2 multiplayer isn't released). On remote/dedicated servers the plugin stays idle. |
| Does it slow down my game? | It does a little work several times per second. Every 30 s the log shows how long a pass takes (`scan avg … ms / max … ms`). Big bases: raise the intervals. |
| Can I use it with another sorting plugin or script? | Don't. Two sorters fight over the same items. |

**Game log:** `%AppData%\SpaceEngineers2\Temp\Logs`, lines start with `[LwCargoManager]`.

---

## 13. Safety: what the plugin never does

- Items only move **through conveyors**. Nothing teleports.
- **Turrets, reactors, generators, batteries, welders and unknown blocks** are never emptied.
- **Production inputs** are never touched; only finished output is collected.
- Only Gearforges named `[Make Ammo]` get production orders.
- Every move is checked. If an amount doesn't add up, the plugin stops itself and writes the reason to the log.
- No internet connections, no files outside its own data folder. See the [Security notes](../README.md#security-notes).

---

## 14. Tag cheat sheet

```
DOCKS (connector)        [Unload]  [Unload All]  [NoUnload]
STORAGE (container)      [Ore] [Ice] [Parts] [Ammo] [Tools] [Consumables] [Player] [Cells] [Misc]
  priority               [Ore P1] … [Ore P9]       (P1 first, default P5)
ITEM TAGS                [Si] [Iron Ore] [Steel Plate] [Rifle] [Gatling] [Missile]
CONTROL                  [Lock] [NoSort] [NoTidy] [Make Ammo]
LCD: cargo               [Cargo] [Ore] [Ice] [Parts] [Cargo Ammo] [Cargo Tools]
LCD: status              [Power] [Power Sources] [Power Consumers] [Damage] [Turrets]
LCD: tools               [Alerts] [Ruler] [Skip]
LCD numbers              [Parts 2 54] = page 2, 54 characters wide
```
