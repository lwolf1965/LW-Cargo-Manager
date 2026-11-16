# Changelog

All released versions of LW Cargo Manager, newest first.

Version numbers are STAGE.FEATURE.FIX.BUILD. The stage follows the game: 0.1 = game alpha.

## 0.1.1.3

### Fixed

- **Weapon ammo no longer depends on Tidy.** With "Consolidate" and "Merge stacks" both off, ammo was not ordered
  and the `[Turrets]` screen stopped updating. Ammo now has its own timer and runs before sorting and tidy.
- **Tidy and sorting no longer fight over an item.** A container with both a category tag and an item tag
  (`[Ore P2] [Si]`) keeps its item; before, tidy pushed it to a better `[Ore]` container and sorting brought it back.
- **A missing conveyor path is no longer reported as full storage.** The dock line on `[Cargo]` screens now says
  `no conveyor path`, and the "storage full" alert only appears when storage really is full.
- **World reload cleanup.** The rate history ("Show rates per minute") and the conveyor check's log keys are now
  cleared when a world loads, like the other caches. Removed a cleanup branch that the game never reached.

### Changed

- **Docked ships take turns.** With two or more ships docked at unload connectors, a different one goes first on
  each pass, so one large ship cannot hold up the others. A ship that has to wait shows `waiting its turn`.
- **The log says why a docked ship is not unloaded**, once per docking: nothing to unload, no conveyor path,
  no room in storage, or the game refused the move.

## 0.1.1.2 (2026-11-13)

First release, listed in PluginHub-SE2.

- **Unload docks:** a ship locked to a connector named `[Unload]` gives its ore and ice to the base;
  `[Unload All]` takes everything except what the ship needs. Ship-to-ship works too (the heavier grid receives).
- **Sorting by name tags:** `[Ore]`, `[Ice]`, `[Parts]`, `[Ammo]`, `[Tools]`, `[Consumables]`, `[Cells]`, `[Misc]`,
  single-item tags such as `[Si]`, priorities `P1`..`P9`, `[Lock]`, `[NoUnload]`, `[NoTidy]`.
- **Production outputs** of refineries, smelters, assemblers, fabricators and Gearforges are collected.
  Production inputs and cockpits are never touched.
- **Tidy:** the same item is packed into as few containers as possible; split stacks can be joined.
- **LCD screens:** cargo summary, item lists per category (the ice screen also shows hydrogen and oxygen tanks),
  alerts, power, power sources and users, damage and weapon ammo, on LCDs and cockpit screens.
- **Weapon ammo:** a Gearforge named `[Make Ammo]` keeps ammo in stock for turrets, fixed guns and rocket launchers.
- **Conveyor check:** `[Alerts]` screens warn about tagged blocks with no conveyor link.
- **Safety:** items move only through conveyors, every move is verified, and any error stops the plugin
  until the next world load. Works with modded blocks and items (values are read from the game).
