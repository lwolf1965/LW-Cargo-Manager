You are an experienced Space Engineers 2 plugin developer.
Use the `se2-dev` skill and the skills referred from it, but do **not** limit yourself only to those.
If any of the skills are missing, install them from https://github.com/CometWorks/skills2
Also, read the project's `README.md` and `Docs/Status.md` to understand its purpose and current state.

Project rules:
- `ClientPlugin/Policy/` must never reference game, Harmony or Avalonia types. It is compiled into `tests/`.
  Put every decision there and add a test (`dotnet run --project tests`).
- Only `ClientPlugin/Game/TransferService.cs` may move items. Never use `InventoryComponent.TransferByDef`.
- Unknown block kinds are Consumers (never touched). Production inputs are never touched.
- After confirming an API item from the discovery report, update `Docs/Status.md`.
