# Contributing

Thanks for helping with LW Cargo Manager. Ideas, bug reports and pull requests are all welcome.

## Ideas and bug reports

- **Ideas:** open an issue with the *Suggestion* form. Say what you want to achieve in the game, not only how.
- **Bugs:** open an issue with the *Bug report* form. The plugin version and the lines with `[LwCargoManager]`
  from the game log are the most useful part (`%AppData%\SpaceEngineers2\Temp\Logs`).
- The plugin runs on the server side of the game. Multiplayer is untested until the game releases it.

## What fits this plugin

**One plugin, one job.** This one manages cargo: unloading, sorting, tidy, cargo and status screens, weapon ammo.
An idea outside that (build planning, cameras, autopilot ...) belongs in a separate plugin, so it will be declined
here even when it is a good idea.

## Pull requests

Please open an issue first for anything bigger than a small fix, so we can agree on the approach.

Rules the code follows:

- **Decisions live in `ClientPlugin/Policy/`**, which never references game, Harmony or Avalonia types.
  Every decision gets a test.
- **Only `ClientPlugin/Game/TransferService.cs` moves items.** Every move is verified.
- **Values come from the game, never from tables**, so modded blocks and items keep working.
- **Unknown block kinds are never touched.** Production inputs and cockpits are never touched.
- **No network access and no file access** outside the game's own log and the plugin settings.
- **Fail safe:** an unexpected error stops the plugin until the next world load instead of guessing.

Before you open the pull request:

1. Run the tests: `dotnet run --project tests`. All must pass.
2. Test in the game through Pulsar from your local folder: load a world, go back to the menu, load again,
   and check the log for errors.
3. Do not change the version number or `LwCargoManager.xml`; the maintainer does that at release.
4. Update `Docs/UserManual.md` if players will see the change. The Russian manual can follow later.

## Building

See *For developers* in the [README](README.md#for-developers). Copy `Directory.Build.props.user.example` to
`Directory.Build.props.user` and set your game folder.

## Licence

By contributing you agree that your work is released under the project's [MIT licence](LICENSE).
